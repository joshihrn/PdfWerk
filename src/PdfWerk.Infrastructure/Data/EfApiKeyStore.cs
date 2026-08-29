using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// Issues and validates API keys, backed by EF Core.
/// </summary>
/// <remarks>
/// <para>
/// The plaintext secret exists exactly once, in the response to the call that created it. Only a
/// SHA-256 hash is stored, so a database dump does not hand over working credentials. A plain
/// hash is appropriate here — unlike a password, the secret is 32 bytes of cryptographic
/// randomness, so there is nothing for an attacker to guess or precompute.
/// </para>
/// <para>
/// Usage records are written on a detached scope and never awaited by the request, because
/// telemetry must not be able to fail or slow down the work the caller actually asked for.
/// </para>
/// </remarks>
public sealed class EfApiKeyStore(
    IDbContextFactory<PdfWerkDbContext> factory,
    IServiceScopeFactory scopes,
    ILogger<EfApiKeyStore> logger) : IApiKeyStore
{
    /// <summary>Marks a PdfWerk key so it is recognisable if it leaks into a log or a repo.</summary>
    private const string KeyPrefix = "pw_";

    public async Task<IssuedApiKey> CreateAsync(string label, QuotaTier tier, TimeSpan? lifetime, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new PdfWerkException("A key needs a label so you can tell it apart later.");

        if (label.Length > 120)
            throw new PdfWerkException("Key labels are limited to 120 characters.");

        var secret = GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            SecretHash = Hash(secret),
            Prefix = secret[..Math.Min(11, secret.Length)],
            Label = label.Trim(),
            Tier = tier,
            CreatedAt = now,
            ExpiresAt = lifetime is null ? null : now + lifetime.Value,
        };

        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new IssuedApiKey(ToRecord(entity), secret);
    }

    public async Task<ApiKeyRecord?> ValidateAsync(string secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return null;

        var hash = Hash(secret);

        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.SecretHash == hash, ct)
            .ConfigureAwait(false);

        if (entity is null || !entity.IsUsable(DateTimeOffset.UtcNow))
            return null;

        TouchInBackground(entity.Id);
        return ToRecord(entity);
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Captured into a local: EF cannot translate DateTimeOffset.UtcNow inside SetProperty.
        DateTimeOffset? revokedAt = DateTimeOffset.UtcNow;

        var updated = await db.ApiKeys
            .Where(k => k.Id == id && k.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(k => k.RevokedAt, revokedAt), ct)
            .ConfigureAwait(false);

        if (updated == 0)
            throw new PdfWerkException("That key does not exist, or was already revoked.", 404);
    }

    public Task RecordUsageAsync(
        Guid? apiKeyId,
        string clientId,
        PdfWerkAction action,
        bool allowed,
        int bytesIn,
        int bytesOut,
        int elapsedMs,
        CancellationToken ct = default)
    {
        // Deliberately not awaited: see the class remarks.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();

                await using var context = await db.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);

                context.Usage.Add(new UsageEntity
                {
                    ApiKeyId = apiKeyId,
                    ClientId = clientId,
                    Action = action,
                    Allowed = allowed,
                    BytesIn = bytesIn,
                    BytesOut = bytesOut,
                    ElapsedMs = elapsedMs,
                    At = DateTimeOffset.UtcNow,
                });

                await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not record usage for {Action}.", action);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>Updates last-used and the call counter without holding up the response.</summary>
    private void TouchInBackground(Guid id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();

                await using var context = await db.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);

                DateTimeOffset? usedAt = DateTimeOffset.UtcNow;

                await context.ApiKeys
                    .Where(k => k.Id == id)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(k => k.LastUsedAt, usedAt)
                        .SetProperty(k => k.TotalCalls, k => k.TotalCalls + 1), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not update last-used for key {KeyId}.", id);
            }
        }, CancellationToken.None);
    }

    /// <summary>Looks up a key by id, for the "show me my key" endpoint.</summary>
    public async Task<ApiKeyRecord?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : ToRecord(entity);
    }

    private static string GenerateSecret()
    {
        // 32 bytes of randomness, URL-safe so it survives being pasted into a header or query.
        var bytes = RandomNumberGenerator.GetBytes(32);

        var body = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return KeyPrefix + body;
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static ApiKeyRecord ToRecord(ApiKeyEntity e) =>
        new(e.Id, e.Label, e.Tier, e.CreatedAt, e.ExpiresAt, e.RevokedAt, e.LastUsedAt, e.TotalCalls);
}

internal static class ApiKeyEntityExtensions
{
    public static bool IsUsable(this ApiKeyEntity key, DateTimeOffset now) =>
        key.RevokedAt is null && (key.ExpiresAt is null || key.ExpiresAt > now);
}
