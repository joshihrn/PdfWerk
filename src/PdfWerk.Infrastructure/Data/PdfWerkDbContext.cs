using Microsoft.EntityFrameworkCore;
using PdfWerk.Core;
using PdfWerk.Core.Limits;

namespace PdfWerk.Infrastructure.Data;

/// <summary>An issued API key. The secret itself is never stored, only its hash.</summary>
public sealed class ApiKeyEntity
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the secret, hex encoded. The only copy of the credential we hold.</summary>
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>
    /// The leading characters of the secret, kept so a user can recognise which key is which in
    /// a list. Short enough to be useless to an attacker on its own.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public QuotaTier Tier { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public long TotalCalls { get; set; }
}

/// <summary>One recorded call, for usage history and abuse investigation.</summary>
public sealed class UsageEntity
{
    public long Id { get; set; }

    public Guid? ApiKeyId { get; set; }

    /// <summary>The rate-limit identity: a key reference, or a salted address hash.</summary>
    public string ClientId { get; set; } = string.Empty;

    public PdfWerkAction Action { get; set; }

    public bool Allowed { get; set; }

    public int BytesIn { get; set; }

    public int BytesOut { get; set; }

    public int ElapsedMs { get; set; }

    public DateTimeOffset At { get; set; }
}

public sealed class PdfWerkDbContext(DbContextOptions<PdfWerkDbContext> options) : DbContext(options)
{
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    public DbSet<UsageEntity> Usage => Set<UsageEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ApiKeyEntity>(key =>
        {
            key.HasKey(k => k.Id);

            // Validation looks a key up by hash on every authenticated request, so this index
            // is on the hot path.
            key.HasIndex(k => k.SecretHash).IsUnique();

            key.Property(k => k.SecretHash).HasMaxLength(64).IsRequired();
            key.Property(k => k.Prefix).HasMaxLength(16).IsRequired();
            key.Property(k => k.Label).HasMaxLength(120).IsRequired();
            key.Property(k => k.Tier).HasConversion<string>().HasMaxLength(20);
        });

        model.Entity<UsageEntity>(usage =>
        {
            usage.HasKey(u => u.Id);
            usage.Property(u => u.ClientId).HasMaxLength(80).IsRequired();
            usage.Property(u => u.Action).HasConversion<string>().HasMaxLength(30);

            // Supports both "what has this caller been doing" and time-bounded pruning.
            usage.HasIndex(u => new { u.ClientId, u.At });
            usage.HasIndex(u => u.At);
        });
    }
}
