using PdfWerk.Core.Limits;

namespace PdfWerk.Core.Abstractions;

/// <summary>An issued API key. The secret itself is never stored, only its hash.</summary>
public sealed record ApiKeyRecord(
    Guid Id,
    string Label,
    QuotaTier Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    long TotalCalls,
    // Separate from the tier: a tier says how much quota a caller gets, which is a different
    // question from what they are allowed to see.
    bool IsAdmin = false)
{
    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}

/// <summary>The plaintext secret, returned exactly once at creation time.</summary>
public sealed record IssuedApiKey(ApiKeyRecord Record, string Secret);

public interface IApiKeyStore
{
    /// <summary>Mints a key, returning the only copy of the plaintext secret that will ever exist.</summary>
    Task<IssuedApiKey> CreateAsync(string label, QuotaTier tier, TimeSpan? lifetime, CancellationToken ct = default);

    /// <summary>Mints an administrator's key. Separate so it can never be reached self-service.</summary>
    Task<IssuedApiKey> CreateAdminAsync(string label, string? secret = null, CancellationToken ct = default);

    /// <summary>True when at least one admin key exists, used to decide whether to bootstrap.</summary>
    Task<bool> AnyAdminAsync(CancellationToken ct = default);

    /// <summary>Resolves a presented secret. Returns null for unknown, expired or revoked keys.</summary>
    Task<ApiKeyRecord?> ValidateAsync(string secret, CancellationToken ct = default);

    Task RevokeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Fire-and-forget usage write. Never blocks the response path.</summary>
    Task RecordUsageAsync(Guid? apiKeyId, string clientId, PdfWerkAction action, bool allowed, int bytesIn, int bytesOut, int elapsedMs, CancellationToken ct = default);
}
