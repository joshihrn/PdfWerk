using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Limits;

namespace PdfWerk.Infrastructure.RateLimiting;

/// <summary>
/// A sliding-window rate limiter held in process memory.
/// </summary>
/// <remarks>
/// <para>
/// Used for local development and single-instance self-hosting. It is accurate for one process
/// and nothing more: run two instances behind a load balancer and each enforces the quota
/// separately, so the effective limit doubles. The public deployment must use the Redis-backed
/// limiter instead, and the host logs a warning at startup when this one is selected outside
/// development.
/// </para>
/// <para>
/// Timestamps are kept per (client, action) and pruned on access, which is exact rather than the
/// approximation a fixed-window counter would give — worth it because the windows here are small
/// and the memory is bounded by the day limit.
/// </para>
/// </remarks>
public sealed class InMemoryRateLimiter(IOptions<RateLimitOptions> options) : IRateLimiter, IDisposable
{
    private readonly RateLimitOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    /// <summary>Entries untouched for this long are eligible for eviction.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromHours(25);

    private DateTimeOffset _lastSweep = DateTimeOffset.UtcNow;

    private sealed class Bucket : IDisposable
    {
        public readonly Lock Gate = new();

        /// <summary>Request times, oldest first, covering at most the last day.</summary>
        public readonly Queue<DateTimeOffset> Hits = new();

        public SemaphoreSlim? Concurrency;

        public DateTimeOffset LastSeen = DateTimeOffset.UtcNow;

        public void Dispose() => Concurrency?.Dispose();
    }

    private readonly record struct Window(string Name, int Limit, TimeSpan Period);

    public Task<RateLimitDecision> AcquireAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default)
    {
        if (!_options.Enabled || client.Tier == QuotaTier.Unlimited)
            return Task.FromResult(RateLimitDecision.Allow(int.MaxValue, int.MaxValue, DateTimeOffset.UtcNow));

        var limit = _options.Limit(client.Tier, action);
        var windows = WindowsOf(limit);
        var bucket = _buckets.GetOrAdd(Key(client, action), _ => new Bucket());

        var now = DateTimeOffset.UtcNow;
        Sweep(now);

        RateLimitDecision decision;

        lock (bucket.Gate)
        {
            bucket.LastSeen = now;
            Prune(bucket, now, windows);

            // Every window is checked before any is consumed, so a rejected call does not
            // deepen the caller's hole.
            foreach (var window in windows)
            {
                var used = CountWithin(bucket, now, window.Period);
                if (used < window.Limit)
                    continue;

                var oldest = OldestWithin(bucket, now, window.Period);
                var resetsAt = (oldest ?? now) + window.Period;

                return Task.FromResult(RateLimitDecision.Deny(window.Name, window.Limit, resetsAt));
            }

            bucket.Hits.Enqueue(now);

            var tightest = Tightest(bucket, now, windows);
            decision = RateLimitDecision.Allow(
                tightest.Limit,
                Math.Max(tightest.Limit - CountWithin(bucket, now, tightest.Period), 0),
                now + tightest.Period);
        }

        // Concurrency is taken outside the lock: it can block, and holding the bucket lock
        // while waiting would serialise every other caller sharing it.
        if (limit.Concurrent <= 0)
            return Task.FromResult(decision);

        var slots = EnsureSemaphore(bucket, limit.Concurrent);

        if (!slots.Wait(0))
        {
            // Give back the counter unit: the request never ran.
            lock (bucket.Gate)
            {
                RemoveNewest(bucket, decision.ResetsAt);
            }

            return Task.FromResult(RateLimitDecision.Deny(
                "concurrent", limit.Concurrent, DateTimeOffset.UtcNow.AddSeconds(1)));
        }

        return Task.FromResult(decision with { Lease = new Lease(slots) });
    }

    public Task<IReadOnlyDictionary<string, int>> PeekAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default)
    {
        var limit = _options.Limit(client.Tier, action);
        var windows = WindowsOf(limit);
        var now = DateTimeOffset.UtcNow;

        IReadOnlyDictionary<string, int> remaining;

        if (!_options.Enabled || client.Tier == QuotaTier.Unlimited)
        {
            remaining = windows.ToDictionary(w => w.Name, _ => int.MaxValue, StringComparer.Ordinal);
            return Task.FromResult(remaining);
        }

        if (!_buckets.TryGetValue(Key(client, action), out var bucket))
        {
            remaining = windows.ToDictionary(w => w.Name, w => w.Limit, StringComparer.Ordinal);
            return Task.FromResult(remaining);
        }

        lock (bucket.Gate)
        {
            remaining = windows.ToDictionary(
                w => w.Name,
                w => Math.Max(w.Limit - CountWithin(bucket, now, w.Period), 0),
                StringComparer.Ordinal);
        }

        return Task.FromResult(remaining);
    }

    // ---- helpers ---------------------------------------------------------

    private static string Key(ClientIdentity client, PdfWerkAction action) => $"{client.Id}|{action}";

    private static List<Window> WindowsOf(ActionLimit limit)
    {
        var windows = new List<Window>(3);

        // A limit of zero disables that window rather than blocking everything.
        if (limit.PerMinute > 0) windows.Add(new Window("minute", limit.PerMinute, TimeSpan.FromMinutes(1)));
        if (limit.PerHour > 0) windows.Add(new Window("hour", limit.PerHour, TimeSpan.FromHours(1)));
        if (limit.PerDay > 0) windows.Add(new Window("day", limit.PerDay, TimeSpan.FromDays(1)));

        return windows;
    }

    private static void Prune(Bucket bucket, DateTimeOffset now, List<Window> windows)
    {
        if (windows.Count == 0)
        {
            bucket.Hits.Clear();
            return;
        }

        var longest = windows.Max(w => w.Period);
        while (bucket.Hits.Count > 0 && now - bucket.Hits.Peek() > longest)
            bucket.Hits.Dequeue();
    }

    private static int CountWithin(Bucket bucket, DateTimeOffset now, TimeSpan period)
    {
        var cutoff = now - period;
        var count = 0;

        foreach (var hit in bucket.Hits)
        {
            if (hit > cutoff)
                count++;
        }

        return count;
    }

    private static DateTimeOffset? OldestWithin(Bucket bucket, DateTimeOffset now, TimeSpan period)
    {
        var cutoff = now - period;

        foreach (var hit in bucket.Hits)
        {
            if (hit > cutoff)
                return hit;
        }

        return null;
    }

    /// <summary>The window closest to exhaustion, which is what the quota headers report.</summary>
    private static Window Tightest(Bucket bucket, DateTimeOffset now, List<Window> windows)
    {
        if (windows.Count == 0)
            return new Window("none", int.MaxValue, TimeSpan.FromMinutes(1));

        return windows
            .OrderBy(w => w.Limit - CountWithin(bucket, now, w.Period))
            .First();
    }

    /// <summary>Undoes the most recent consumption when a later guard rejects the request.</summary>
    private static void RemoveNewest(Bucket bucket, DateTimeOffset _)
    {
        if (bucket.Hits.Count == 0)
            return;

        // Queue has no pop-from-back, so it is rebuilt without its last element.
        var kept = bucket.Hits.ToArray();
        bucket.Hits.Clear();

        for (var i = 0; i < kept.Length - 1; i++)
            bucket.Hits.Enqueue(kept[i]);
    }

    private static SemaphoreSlim EnsureSemaphore(Bucket bucket, int permits)
    {
        lock (bucket.Gate)
        {
            return bucket.Concurrency ??= new SemaphoreSlim(permits, permits);
        }
    }

    /// <summary>Drops buckets nobody has touched for a day, so idle clients do not accumulate.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(10))
            return;

        _lastSweep = now;

        foreach (var (key, bucket) in _buckets)
        {
            if (now - bucket.LastSeen <= Idle)
                continue;

            if (_buckets.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            // Guard against a double dispose releasing a permit that was never taken.
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();

            return ValueTask.CompletedTask;
        }
    }

    public void Dispose()
    {
        foreach (var bucket in _buckets.Values)
            bucket.Dispose();

        _buckets.Clear();
    }
}
