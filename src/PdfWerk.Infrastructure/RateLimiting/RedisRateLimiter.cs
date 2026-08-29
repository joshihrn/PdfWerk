using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Limits;
using StackExchange.Redis;

namespace PdfWerk.Infrastructure.RateLimiting;

/// <summary>
/// A sliding-window rate limiter backed by Redis, correct across many instances.
/// </summary>
/// <remarks>
/// <para>
/// Each window is a sorted set of request timestamps, trimmed on every call. That gives a true
/// sliding window rather than the burst-at-the-boundary behaviour of fixed counters, where a
/// caller can send two full allowances either side of a window edge.
/// </para>
/// <para>
/// The check and the consume happen inside one Lua script, so they are atomic. Doing them as
/// separate round trips would let two concurrent requests both read "under the limit" and both
/// proceed — precisely the race an attacker exercises by firing in parallel. The script also
/// refuses to consume anything unless <em>every</em> window has room, so a rejected call does
/// not deepen the caller's hole.
/// </para>
/// </remarks>
public sealed class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RedisRateLimiter> _logger;

    /// <summary>
    /// Checks every window, and consumes one unit from each only if all of them have room.
    /// </summary>
    /// <remarks>
    /// KEYS  : one key per window.
    /// ARGV  : now_ms, member, then (limit, period_ms) per window.
    /// Result: {allowed, trippedIndex, limit, remaining, resetAtMs}
    /// </remarks>
    private const string SlidingWindowScript =
        """
        local now = tonumber(ARGV[1])
        local member = ARGV[2]
        local windows = #KEYS

        local counts = {}
        local limits = {}
        local periods = {}

        for i = 1, windows do
          local limit  = tonumber(ARGV[2 + (i * 2) - 1])
          local period = tonumber(ARGV[2 + (i * 2)])
          limits[i] = limit
          periods[i] = period

          redis.call('ZREMRANGEBYSCORE', KEYS[i], 0, now - period)
          counts[i] = redis.call('ZCARD', KEYS[i])

          if counts[i] >= limit then
            -- Reset is when the oldest entry in this window falls out of it.
            local oldest = redis.call('ZRANGE', KEYS[i], 0, 0, 'WITHSCORES')
            local resetAt = now + period
            if oldest[2] then resetAt = tonumber(oldest[2]) + period end
            return { 0, i, limit, 0, resetAt }
          end
        end

        -- Every window has room, so charge them all.
        local tightest, tightestRemaining, tightestReset = 1, nil, now + periods[1]
        for i = 1, windows do
          redis.call('ZADD', KEYS[i], now, member)
          redis.call('PEXPIRE', KEYS[i], periods[i])

          local remaining = limits[i] - (counts[i] + 1)
          if tightestRemaining == nil or remaining < tightestRemaining then
            tightestRemaining = remaining
            tightest = i
            tightestReset = now + periods[i]
          end
        end

        return { 1, tightest, limits[tightest], tightestRemaining, tightestReset }
        """;

    /// <summary>Takes a concurrency slot, releasing it if the ceiling is already reached.</summary>
    private const string ConcurrencyScript =
        """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
          redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[2]))
        end
        if current > tonumber(ARGV[1]) then
          redis.call('DECR', KEYS[1])
          return 0
        end
        return 1
        """;

    public RedisRateLimiter(
        IConnectionMultiplexer redis,
        IOptions<RateLimitOptions> options,
        ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RateLimitDecision> AcquireAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default)
    {
        if (!_options.Enabled || client.Tier == QuotaTier.Unlimited)
            return RateLimitDecision.Allow(int.MaxValue, int.MaxValue, DateTimeOffset.UtcNow);

        var limit = _options.Limit(client.Tier, action);
        var windows = Windows(limit);

        if (windows.Count == 0)
            return await AcquireConcurrencyOnlyAsync(client, action, limit).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        try
        {
            var db = _redis.GetDatabase();

            var keys = windows
                .Select(w => (RedisKey)$"{_options.KeyPrefix}:{client.Id}:{action}:{w.Name}")
                .ToArray();

            var args = new List<RedisValue>
            {
                now.ToUnixTimeMilliseconds(),

                // Members must be unique or two requests in the same millisecond collapse into
                // one entry, silently granting a free call.
                $"{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}",
            };

            foreach (var window in windows)
            {
                args.Add(window.Limit);
                args.Add((long)window.Period.TotalMilliseconds);
            }

            var raw = (RedisResult[]?)await db
                .ScriptEvaluateAsync(SlidingWindowScript, keys, [.. args])
                .ConfigureAwait(false);

            if (raw is null || raw.Length < 5)
                return Fallback(action, "the limiter returned an unexpected result");

            var allowed = (int)raw[0] == 1;
            var index = (int)raw[1];
            var ceiling = (int)raw[2];
            var remaining = (int)raw[3];
            var resetAt = DateTimeOffset.FromUnixTimeMilliseconds((long)raw[4]);

            var windowName = windows[Math.Clamp(index - 1, 0, windows.Count - 1)].Name;

            if (!allowed)
                return RateLimitDecision.Deny(windowName, ceiling, resetAt);

            var lease = await TakeConcurrencySlotAsync(client, action, limit).ConfigureAwait(false);

            if (lease is null && limit.Concurrent > 0)
            {
                // Hand back the unit we just charged: the request will not run.
                await RefundAsync(keys, args[1]).ConfigureAwait(false);

                return RateLimitDecision.Deny("concurrent", limit.Concurrent, DateTimeOffset.UtcNow.AddSeconds(1));
            }

            return RateLimitDecision.Allow(ceiling, Math.Max(remaining, 0), resetAt, lease);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or ObjectDisposedException)
        {
            _logger.LogError(ex, "Redis was unreachable while rate limiting {Action}.", action);
            return Fallback(action, "the rate limiter is unavailable");
        }
    }

    public async Task<IReadOnlyDictionary<string, int>> PeekAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default)
    {
        var limit = _options.Limit(client.Tier, action);
        var windows = Windows(limit);
        var results = new Dictionary<string, int>(StringComparer.Ordinal);

        if (!_options.Enabled || client.Tier == QuotaTier.Unlimited)
        {
            foreach (var window in windows)
                results[window.Name] = int.MaxValue;

            return results;
        }

        try
        {
            var db = _redis.GetDatabase();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            foreach (var window in windows)
            {
                var key = (RedisKey)$"{_options.KeyPrefix}:{client.Id}:{action}:{window.Name}";

                // Counting the live range leaves the set untouched, so a peek costs no quota.
                var used = await db
                    .SortedSetLengthAsync(key, now - window.Period.TotalMilliseconds, now)
                    .ConfigureAwait(false);

                results[window.Name] = Math.Max(window.Limit - (int)used, 0);
            }
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex, "Redis was unreachable while reading quota.");

            foreach (var window in windows)
                results[window.Name] = window.Limit;
        }

        return results;
    }

    // ---- concurrency -----------------------------------------------------

    private async Task<IAsyncDisposable?> TakeConcurrencySlotAsync(ClientIdentity client, PdfWerkAction action, ActionLimit limit)
    {
        if (limit.Concurrent <= 0)
            return null;

        var key = (RedisKey)$"{_options.KeyPrefix}:{client.Id}:{action}:inflight";
        var db = _redis.GetDatabase();

        // The expiry is a safety net: a process that dies mid-request must not leak a slot
        // forever, so the counter self-heals after a few minutes.
        var granted = (int)await db
            .ScriptEvaluateAsync(ConcurrencyScript, [key], [limit.Concurrent, (long)TimeSpan.FromMinutes(5).TotalMilliseconds])
            .ConfigureAwait(false);

        return granted == 1 ? new RedisLease(db, key, _logger) : null;
    }

    private async Task<RateLimitDecision> AcquireConcurrencyOnlyAsync(ClientIdentity client, PdfWerkAction action, ActionLimit limit)
    {
        var lease = await TakeConcurrencySlotAsync(client, action, limit).ConfigureAwait(false);

        return lease is null && limit.Concurrent > 0
            ? RateLimitDecision.Deny("concurrent", limit.Concurrent, DateTimeOffset.UtcNow.AddSeconds(1))
            : RateLimitDecision.Allow(int.MaxValue, int.MaxValue, DateTimeOffset.UtcNow, lease);
    }

    private async Task RefundAsync(RedisKey[] keys, RedisValue member)
    {
        try
        {
            var db = _redis.GetDatabase();
            foreach (var key in keys)
                await db.SortedSetRemoveAsync(key, member).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            // Losing a refund costs the caller one unit of quota, which is preferable to
            // failing a request that was otherwise fine.
            _logger.LogDebug(ex, "Could not refund a rate limit unit.");
        }
    }

    /// <summary>
    /// What to do when Redis is down. Failing closed protects the service at the cost of an
    /// outage; failing open keeps it running with no protection at all. The default is closed,
    /// because an unguarded public endpoint is the worse failure.
    /// </summary>
    private RateLimitDecision Fallback(PdfWerkAction action, string reason)
    {
        if (!_options.FailClosed)
            return RateLimitDecision.Allow(int.MaxValue, int.MaxValue, DateTimeOffset.UtcNow);

        _logger.LogWarning("Rejecting {Action} because {Reason}.", action, reason);

        return RateLimitDecision.Deny("unavailable", 0, DateTimeOffset.UtcNow.AddSeconds(5));
    }

    private readonly record struct Window(string Name, int Limit, TimeSpan Period);

    private static List<Window> Windows(ActionLimit limit)
    {
        var windows = new List<Window>(3);

        if (limit.PerMinute > 0) windows.Add(new Window("minute", limit.PerMinute, TimeSpan.FromMinutes(1)));
        if (limit.PerHour > 0) windows.Add(new Window("hour", limit.PerHour, TimeSpan.FromHours(1)));
        if (limit.PerDay > 0) windows.Add(new Window("day", limit.PerDay, TimeSpan.FromDays(1)));

        return windows;
    }

    private sealed class RedisLease(IDatabase db, RedisKey key, ILogger logger) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            try
            {
                await db.StringDecrementAsync(key).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
            {
                // The key's expiry will reclaim the slot even if this decrement is lost.
                logger.LogDebug(ex, "Could not release a concurrency slot.");
            }
        }
    }
}
