namespace PdfWerk.Core.Limits;

/// <summary>The resolved caller behind a request.</summary>
/// <param name="Id">Stable counter key: "key:{id}" for API keys, "ip:{sha256-prefix}" otherwise.</param>
/// <param name="Tier">Quota tier the caller's limits come from.</param>
/// <param name="ApiKeyId">Database id of the key, when one was presented.</param>
/// <param name="Label">Display name for logs and usage history.</param>
public sealed record ClientIdentity(string Id, QuotaTier Tier, Guid? ApiKeyId, string Label)
{
    public static ClientIdentity Anonymous(string hashedIp) =>
        new($"ip:{hashedIp}", QuotaTier.Anonymous, null, "anonymous");
}

/// <summary>Which window tripped, and what the caller should be told.</summary>
public sealed record RateLimitDecision
{
    public required bool Allowed { get; init; }

    /// <summary>"minute", "hour", "day" or "concurrent". Empty when allowed.</summary>
    public string Window { get; init; } = string.Empty;

    /// <summary>Ceiling for the window that is closest to being exhausted.</summary>
    public int Limit { get; init; }

    /// <summary>Calls left in that window.</summary>
    public int Remaining { get; init; }

    /// <summary>When the tightest window frees up.</summary>
    public DateTimeOffset ResetsAt { get; init; }

    public TimeSpan RetryAfter { get; init; }

    /// <summary>Disposed when the request finishes, releasing the concurrency slot.</summary>
    public IAsyncDisposable? Lease { get; init; }

    public static RateLimitDecision Allow(int limit, int remaining, DateTimeOffset resetsAt, IAsyncDisposable? lease = null) =>
        new() { Allowed = true, Limit = limit, Remaining = remaining, ResetsAt = resetsAt, Lease = lease };

    public static RateLimitDecision Deny(string window, int limit, DateTimeOffset resetsAt) =>
        new()
        {
            Allowed = false,
            Window = window,
            Limit = limit,
            Remaining = 0,
            ResetsAt = resetsAt,
            RetryAfter = resetsAt - DateTimeOffset.UtcNow is { Ticks: > 0 } d ? d : TimeSpan.FromSeconds(1),
        };
}

/// <summary>
/// Counts requests per (client, action) across several rolling windows at once.
/// Implementations must be atomic across instances — the public deployment runs more than one.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Consumes one unit against every configured window. On denial, nothing is consumed,
    /// so a rejected call does not deepen the caller's hole.
    /// </summary>
    Task<RateLimitDecision> AcquireAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default);

    /// <summary>Reports remaining quota without consuming any. Powers the /v1/quota endpoint.</summary>
    Task<IReadOnlyDictionary<string, int>> PeekAsync(ClientIdentity client, PdfWerkAction action, CancellationToken ct = default);
}
