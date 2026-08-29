using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// Rate limits as configured, with anything changed from the admin portal layered on top.
/// </summary>
/// <remarks>
/// Only the values someone actually changed are stored. A limit left alone keeps following
/// appsettings, so a deployment can still move defaults without an operator's one-off tweak from
/// months ago silently winning — which is the failure mode of copying the whole settings blob
/// into a database and editing that instead.
///
/// <see cref="Current"/> is read on every request, so it hands back a snapshot built at refresh
/// time rather than composing one per call.
/// </remarks>
public sealed class EfRateLimitSettings(
    IDbContextFactory<PdfWerkDbContext> factory,
    IOptions<RateLimitOptions> configured,
    ILogger<EfRateLimitSettings> logger) : IRateLimitSettings
{
    private volatile RateLimitOptions current = configured.Value;

    public RateLimitOptions Current => current;

    public async Task<IReadOnlyList<LimitSetting>> ListAsync(CancellationToken ct = default)
    {
        var overrides = await LoadOverridesAsync(ct).ConfigureAwait(false);
        var settings = new List<LimitSetting>();

        foreach (var tier in Enum.GetValues<QuotaTier>())
        {
            var policy = current.For(tier);

            settings.Add(Describe(tier, string.Empty, policy.Default, overrides));

            foreach (var (action, limit) in policy.Actions.OrderBy(a => a.Key, StringComparer.Ordinal))
                settings.Add(Describe(tier, action, limit, overrides));
        }

        return settings;
    }

    public async Task SaveAsync(LimitSetting setting, string updatedBy, CancellationToken ct = default)
    {
        if (!Enum.TryParse<QuotaTier>(setting.Tier, ignoreCase: true, out _))
            throw new PdfWerkException($"'{setting.Tier}' is not a tier.");

        if (!string.IsNullOrEmpty(setting.Action) &&
            !Enum.TryParse<PdfWerkAction>(setting.Action, ignoreCase: true, out _))
        {
            throw new PdfWerkException($"'{setting.Action}' is not an action.");
        }

        // Negative counters would not fail loudly; they would quietly refuse every request with a
        // limit nobody can be under.
        foreach (var (name, value) in new[]
                 {
                     ("perMinute", setting.PerMinute), ("perHour", setting.PerHour),
                     ("perDay", setting.PerDay), ("concurrent", setting.Concurrent),
                     ("maxPages", setting.MaxPages), ("maxBatch", setting.MaxBatch),
                     ("maxCharacters", setting.MaxCharacters),
                 })
        {
            if (value < 0) throw new PdfWerkException($"{name} cannot be negative.");
        }

        if (setting.MaxUploadBytes < 0) throw new PdfWerkException("maxUploadBytes cannot be negative.");

        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var row = await context.RateLimitOverrides
            .FirstOrDefaultAsync(o => o.Tier == setting.Tier && o.Action == setting.Action, ct)
            .ConfigureAwait(false)
            ?? context.RateLimitOverrides.Add(new RateLimitOverrideEntity
            {
                Id = Guid.NewGuid(),
                Tier = setting.Tier,
                Action = setting.Action,
            }).Entity;

        row.PerMinute = setting.PerMinute;
        row.PerHour = setting.PerHour;
        row.PerDay = setting.PerDay;
        row.Concurrent = setting.Concurrent;
        row.MaxUploadBytes = setting.MaxUploadBytes;
        row.MaxPages = setting.MaxPages;
        row.MaxBatch = setting.MaxBatch;
        row.MaxCharacters = setting.MaxCharacters;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedBy = updatedBy;

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        await RefreshAsync(ct).ConfigureAwait(false);

        logger.LogWarning(
            "Rate limit for {Tier}/{Action} changed by {By}.",
            setting.Tier,
            string.IsNullOrEmpty(setting.Action) ? "default" : setting.Action,
            updatedBy);
    }

    public async Task ResetAsync(string tier, string action, CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        await context.RateLimitOverrides
            .Where(o => o.Tier == tier && o.Action == (action ?? string.Empty))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var overrides = await LoadOverridesAsync(ct).ConfigureAwait(false);

        // Rebuilt from the configured values every time, never from the last snapshot: applying
        // overrides on top of already-overridden values would make a reset impossible to undo.
        var rebuilt = Clone(configured.Value);

        foreach (var row in overrides.Values)
        {
            if (!Enum.TryParse<QuotaTier>(row.Tier, ignoreCase: true, out var tier)) continue;

            var policy = rebuilt.For(tier);
            var target = string.IsNullOrEmpty(row.Action)
                ? policy.Default
                : policy.Actions.TryGetValue(row.Action, out var existing)
                    ? existing
                    : policy.Actions[row.Action] = policy.Default.Clone();

            target.PerMinute = row.PerMinute;
            target.PerHour = row.PerHour;
            target.PerDay = row.PerDay;
            target.Concurrent = row.Concurrent;
            target.MaxUploadBytes = row.MaxUploadBytes;
            target.MaxPages = row.MaxPages;
            target.MaxBatch = row.MaxBatch;
            target.MaxCharacters = row.MaxCharacters;
        }

        current = rebuilt;
    }

    private async Task<Dictionary<string, RateLimitOverrideEntity>> LoadOverridesAsync(CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var rows = await context.RateLimitOverrides.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        return rows.ToDictionary(r => Key(r.Tier, r.Action), StringComparer.OrdinalIgnoreCase);
    }

    private static LimitSetting Describe(
        QuotaTier tier,
        string action,
        ActionLimit limit,
        Dictionary<string, RateLimitOverrideEntity> overrides) =>
        new(
            tier.ToString(),
            action,
            limit.PerMinute,
            limit.PerHour,
            limit.PerDay,
            limit.Concurrent,
            limit.MaxUploadBytes,
            limit.MaxPages,
            limit.MaxBatch,
            limit.MaxCharacters,
            overrides.ContainsKey(Key(tier.ToString(), action)));

    private static string Key(string tier, string action) => $"{tier}|{action}";

    /// <summary>
    /// A deep copy of the configured options, so applying overrides never mutates the values that
    /// came from the file — those have to stay intact for a reset to mean anything.
    /// </summary>
    private static RateLimitOptions Clone(RateLimitOptions source)
    {
        var copy = new RateLimitOptions
        {
            Enabled = source.Enabled,
            FailClosed = source.FailClosed,
            KeyPrefix = source.KeyPrefix,
        };

        // Every tier is materialised, including ones configuration left implicit. `For` falls back
        // to the built-in defaults, and an override has to have something concrete to be written
        // onto — otherwise editing a tier that was never in the file would silently do nothing.
        foreach (var tier in Enum.GetValues<QuotaTier>())
        {
            var from = source.For(tier);
            var policy = new TierPolicy { Default = from.Default.Clone() };

            foreach (var (action, limit) in from.Actions)
                policy.Actions[action] = limit.Clone();

            copy.Tiers[tier.ToString()] = policy;
        }

        return copy;
    }
}
