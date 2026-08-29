using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// The block list, answered from memory and backed by the database.
/// </summary>
/// <remarks>
/// <see cref="IsBlocked"/> is consulted on every request, so it never touches the database: it
/// walks a snapshot rebuilt when the list changes and on a timer. The timer is what lets a block
/// added on one instance take effect on the others, which matters as soon as there is more than
/// one — and a block that only applies to whichever instance you happened to add it on is worse
/// than useless, because it looks like it worked.
/// </remarks>
public sealed class EfIpBlockList(
    IDbContextFactory<PdfWerkDbContext> factory,
    ILogger<EfIpBlockList> logger) : IIpBlockList
{
    private sealed record Rule(IPAddress Network, int PrefixLength, DateTimeOffset? ExpiresAt);

    private volatile Rule[] rules = [];

    public bool IsBlocked(string address)
    {
        var snapshot = rules;
        if (snapshot.Length == 0) return false;

        if (!IPAddress.TryParse(address, out var parsed)) return false;

        var now = DateTimeOffset.UtcNow;

        foreach (var rule in snapshot)
        {
            // Expiry is checked here rather than by removing rows, so a lapsed block stops
            // applying immediately instead of waiting for the next refresh.
            if (rule.ExpiresAt is not null && rule.ExpiresAt <= now) continue;

            if (CidrRange.Contains(rule.Network, rule.PrefixLength, parsed)) return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<IpBlockRecord>> ListAsync(CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Ordered after materialising, not in SQL: SQLite refuses to ORDER BY a DateTimeOffset,
        // and a block list is small enough that sorting it here costs nothing.
        var rows = await context.IpBlocks
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        return rows.OrderByDescending(b => b.CreatedAt).Select(b => new IpBlockRecord(
            b.Id,
            b.Cidr,
            b.Reason,
            b.CreatedAt,
            b.CreatedBy,
            b.ExpiresAt,
            b.IsActive(now))).ToList();
    }

    public async Task<IpBlockRecord> AddAsync(
        string cidr,
        string reason,
        string addedBy,
        DateTimeOffset? expiresAt,
        CancellationToken ct = default)
    {
        if (!CidrRange.TryParse(cidr, out var network, out var prefix, out var family))
        {
            throw new PdfWerkException(
                $"'{cidr}' is not an address or range. Use 203.0.113.4 for one address, " +
                "or 203.0.113.0/24 for a range.");
        }

        // A /0 matches every address there is, including the administrator adding it. Refusing is
        // kinder than letting someone lock the entire internet, themselves included, out.
        if (prefix == 0)
            throw new PdfWerkException("A /0 range blocks every address, including your own. Narrow it.");

        var canonical = prefix == (family == 4 ? 32 : 128)
            ? network.ToString()
            : $"{network}/{prefix}";

        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var existing = await context.IpBlocks
            .FirstOrDefaultAsync(b => b.Network == network.ToString() && b.PrefixLength == prefix, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Re-adding a range updates it rather than failing on the unique index: the intent is
            // plainly "block this", and a duplicate-key error helps nobody.
            existing.Reason = reason;
            existing.CreatedBy = addedBy;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            existing.ExpiresAt = expiresAt;
        }
        else
        {
            existing = new IpBlockEntity
            {
                Id = Guid.NewGuid(),
                Cidr = canonical,
                Network = network.ToString(),
                PrefixLength = prefix,
                AddressFamily = family,
                Reason = reason,
                CreatedBy = addedBy,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
            };

            context.IpBlocks.Add(existing);
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);

        logger.LogWarning("Blocked {Cidr} ({Reason}) by {AddedBy}.", canonical, reason, addedBy);

        return new IpBlockRecord(
            existing.Id,
            existing.Cidr,
            existing.Reason,
            existing.CreatedAt,
            existing.CreatedBy,
            existing.ExpiresAt,
            existing.IsActive(DateTimeOffset.UtcNow));
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var removed = await context.IpBlocks
            .Where(b => b.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (removed == 0)
            throw new PdfWerkException("There is no block with that id.", 404);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var rows = await context.IpBlocks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        rules = rows
            .Select(b => IPAddress.TryParse(b.Network, out var network)
                ? new Rule(network, b.PrefixLength, b.ExpiresAt)
                : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();
    }
}
