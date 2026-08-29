using PdfWerk.Core.Limits;

namespace PdfWerk.Core.Abstractions;

/// <summary>One request as the admin portal shows it.</summary>
public sealed record RequestLogRecord(
    long Id,
    DateTimeOffset At,
    string Address,
    string Method,
    string Path,
    int StatusCode,
    int ElapsedMs,
    string? UserAgent,
    string ClientId,
    string? Action,
    bool Blocked);

/// <summary>What to write when a request finishes.</summary>
public sealed record RequestLogEntry
{
    public required string Address { get; init; }

    public required string Method { get; init; }

    public required string Path { get; init; }

    public required int StatusCode { get; init; }

    public required int ElapsedMs { get; init; }

    public string? UserAgent { get; init; }

    public Guid? ApiKeyId { get; init; }

    public string ClientId { get; init; } = string.Empty;

    public PdfWerkAction? Action { get; init; }

    public bool Blocked { get; init; }
}

/// <summary>
/// Records requests and reads them back.
/// </summary>
/// <remarks>
/// Writing is fire-and-forget by contract, not by accident: a request must not wait on the audit
/// trail, and losing the last few rows to a hard shutdown is a better outcome than adding a
/// database round trip to the latency of every call.
/// </remarks>
public interface IRequestLog
{
    void Record(RequestLogEntry entry);

    Task<IReadOnlyList<RequestLogRecord>> RecentAsync(int take, string? address = null, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>Deletes anything older than the cutoff. Returns how many rows went.</summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}

public sealed record IpBlockRecord(
    Guid Id,
    string Cidr,
    string Reason,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ExpiresAt,
    bool Active);

/// <summary>
/// The block list, and the fast path that consults it.
/// </summary>
/// <remarks>
/// <see cref="IsBlocked"/> runs on every single request, so it answers from an in-memory snapshot
/// rather than the database. The snapshot is refreshed when the list changes and on a timer, so a
/// block added on one instance reaches the others without a restart.
/// </remarks>
public interface IIpBlockList
{
    bool IsBlocked(string address);

    Task<IReadOnlyList<IpBlockRecord>> ListAsync(CancellationToken ct = default);

    Task<IpBlockRecord> AddAsync(string cidr, string reason, string addedBy, DateTimeOffset? expiresAt, CancellationToken ct = default);

    Task RemoveAsync(Guid id, CancellationToken ct = default);

    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>An editable limit, as the portal presents it.</summary>
public sealed record LimitSetting(
    string Tier,
    string Action,
    int PerMinute,
    int PerHour,
    int PerDay,
    int Concurrent,
    long MaxUploadBytes,
    int MaxPages,
    int MaxBatch,
    int MaxCharacters,
    bool IsOverride);

/// <summary>
/// Rate limits as they currently stand, including anything changed from the portal.
/// </summary>
/// <remarks>
/// Overrides are layered over the file configuration rather than replacing it. Only values
/// someone actually changed are stored, so a limit left alone keeps following appsettings and a
/// deployment can still move defaults without an operator's months-old tweak silently winning.
/// </remarks>
public interface IRateLimitSettings
{
    /// <summary>The effective options, with overrides applied. Cached; cheap to call.</summary>
    RateLimitOptions Current { get; }

    Task<IReadOnlyList<LimitSetting>> ListAsync(CancellationToken ct = default);

    Task SaveAsync(LimitSetting setting, string updatedBy, CancellationToken ct = default);

    /// <summary>Drops an override so the value follows configuration again.</summary>
    Task ResetAsync(string tier, string action, CancellationToken ct = default);

    Task RefreshAsync(CancellationToken ct = default);
}
