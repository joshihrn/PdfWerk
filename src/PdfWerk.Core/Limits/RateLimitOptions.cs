namespace PdfWerk.Core.Limits;

/// <summary>Who the caller is, which decides how generous their quota is.</summary>
public enum QuotaTier
{
    /// <summary>No API key. Identified by hashed IP, and kept deliberately tight.</summary>
    Anonymous,

    /// <summary>Self-service API key, free of charge.</summary>
    Free,

    /// <summary>Elevated key, issued manually.</summary>
    Pro,

    /// <summary>Internal / self-hosted key. Guards still apply; counters do not.</summary>
    Unlimited,
}

/// <summary>
/// The full limit set for one action at one tier. Counter windows and hard guards live
/// together because they are always configured as a unit.
/// </summary>
public sealed class ActionLimit
{
    /// <summary>Requests allowed in a rolling 60-second window. 0 disables the window.</summary>
    public int PerMinute { get; set; }

    /// <summary>Requests allowed in a rolling 60-minute window.</summary>
    public int PerHour { get; set; }

    /// <summary>Requests allowed in a rolling 24-hour window.</summary>
    public int PerDay { get; set; }

    /// <summary>Requests this caller may have in flight simultaneously.</summary>
    public int Concurrent { get; set; } = 2;

    /// <summary>Largest single upload accepted, in bytes.</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Largest document accepted, in pages. Rejected after a cheap header read.</summary>
    public int MaxPages { get; set; } = 100;

    /// <summary>Cap on list-shaped input: files to merge, fields to add, replacements to apply.</summary>
    public int MaxBatch { get; set; } = 10;

    /// <summary>Cap on characters of text accepted for creation, and of extracted text sent to a model.</summary>
    public int MaxCharacters { get; set; } = 200_000;

    public ActionLimit Clone() => (ActionLimit)MemberwiseClone();
}

/// <summary>A tier's default limits plus any per-action overrides.</summary>
public sealed class TierPolicy
{
    public ActionLimit Default { get; set; } = new();

    /// <summary>Keyed by <see cref="PdfWerkAction"/> name. Missing entries inherit <see cref="Default"/>.</summary>
    public Dictionary<string, ActionLimit> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ActionLimit For(PdfWerkAction action) =>
        Actions.TryGetValue(action.ToString(), out var limit) ? limit : Default;
}

/// <summary>Root configuration bound from the "RateLimits" section.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Master switch. Only ever set false for local development or self-hosting.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When Redis is unreachable: true rejects traffic (safe), false lets it through (available).
    /// Public deployments should keep this true — an open door is worse than an outage.
    /// </summary>
    public bool FailClosed { get; set; } = true;

    /// <summary>Prefix for every Redis key, so one Redis can host several environments.</summary>
    public string KeyPrefix { get; set; } = "pdfwerk:rl";

    /// <summary>Keyed by <see cref="QuotaTier"/> name.</summary>
    public Dictionary<string, TierPolicy> Tiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TierPolicy For(QuotaTier tier) =>
        Tiers.TryGetValue(tier.ToString(), out var policy) ? policy : Defaults.For(tier);

    public ActionLimit Limit(QuotaTier tier, PdfWerkAction action) => For(tier).For(action);

    /// <summary>
    /// Built-in policy, used when configuration supplies nothing. Deliberately conservative:
    /// this project is public, and the AI actions cost the most per call.
    /// </summary>
    public static class Defaults
    {
        public static TierPolicy For(QuotaTier tier) => tier switch
        {
            QuotaTier.Anonymous => Anonymous(),
            QuotaTier.Free => Free(),
            QuotaTier.Pro => Pro(),
            QuotaTier.Unlimited => Unlimited(),
            _ => Anonymous(),
        };

        private static TierPolicy Anonymous() => new()
        {
            Default = new ActionLimit
            {
                PerMinute = 5, PerHour = 40, PerDay = 120, Concurrent = 2,
                MaxUploadBytes = 10 * 1024 * 1024, MaxPages = 50, MaxBatch = 5, MaxCharacters = 50_000,
            },
            Actions =
            {
                // Far tighter than anything else, but not so tight that a person cannot correct a
                // mistake. The counter is spent before the handler runs, so a mistyped address or
                // a message a few characters short costs a send — three an hour would lock someone
                // out for fixing a typo twice. The hourly figure does the real work — ten an hour
                // is nothing to a spammer — so the per-minute one only needs to stop a tight
                // burst, and can be loose enough that correcting a mistake never trips it.
                //
                // The same ceiling applies at every tier: a paid key is not a reason to send more
                // email to us.
                [nameof(PdfWerkAction.Contact)] = new ActionLimit
                {
                    PerMinute = 5, PerHour = 10, PerDay = 20, Concurrent = 1,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 4_000,
                },
                [nameof(PdfWerkAction.Summarize)] = new ActionLimit
                {
                    PerMinute = 2, PerHour = 8, PerDay = 20, Concurrent = 1,
                    MaxUploadBytes = 8 * 1024 * 1024, MaxPages = 30, MaxBatch = 1, MaxCharacters = 60_000,
                },
                [nameof(PdfWerkAction.DraftDocument)] = new ActionLimit
                {
                    PerMinute = 2, PerHour = 8, PerDay = 20, Concurrent = 1,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 8_000,
                },
                [nameof(PdfWerkAction.CreateFromWord)] = new ActionLimit
                {
                    PerMinute = 3, PerHour = 20, PerDay = 60, Concurrent = 1,
                    MaxUploadBytes = 15 * 1024 * 1024, MaxPages = 100, MaxBatch = 1, MaxCharacters = 200_000,
                },
                [nameof(PdfWerkAction.Inspect)] = new ActionLimit
                {
                    PerMinute = 20, PerHour = 200, PerDay = 600, Concurrent = 4,
                    MaxUploadBytes = 10 * 1024 * 1024, MaxPages = 500, MaxBatch = 1, MaxCharacters = 200_000,
                },
            },
        };

        private static TierPolicy Free() => new()
        {
            Default = new ActionLimit
            {
                PerMinute = 20, PerHour = 300, PerDay = 1_500, Concurrent = 4,
                MaxUploadBytes = 25 * 1024 * 1024, MaxPages = 300, MaxBatch = 20, MaxCharacters = 300_000,
            },
            Actions =
            {
                // Far tighter than anything else, but not so tight that a person cannot correct a
                // mistake. The counter is spent before the handler runs, so a mistyped address or
                // a message a few characters short costs a send — three an hour would lock someone
                // out for fixing a typo twice. The hourly figure does the real work — ten an hour
                // is nothing to a spammer — so the per-minute one only needs to stop a tight
                // burst, and can be loose enough that correcting a mistake never trips it.
                //
                // The same ceiling applies at every tier: a paid key is not a reason to send more
                // email to us.
                [nameof(PdfWerkAction.Contact)] = new ActionLimit
                {
                    PerMinute = 5, PerHour = 10, PerDay = 20, Concurrent = 1,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 4_000,
                },
                [nameof(PdfWerkAction.Summarize)] = new ActionLimit
                {
                    PerMinute = 6, PerHour = 60, PerDay = 250, Concurrent = 2,
                    MaxUploadBytes = 20 * 1024 * 1024, MaxPages = 150, MaxBatch = 1, MaxCharacters = 300_000,
                },
                [nameof(PdfWerkAction.DraftDocument)] = new ActionLimit
                {
                    PerMinute = 6, PerHour = 60, PerDay = 250, Concurrent = 2,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 8_000,
                },
                [nameof(PdfWerkAction.CreateFromWord)] = new ActionLimit
                {
                    PerMinute = 10, PerHour = 120, PerDay = 500, Concurrent = 2,
                    MaxUploadBytes = 40 * 1024 * 1024, MaxPages = 500, MaxBatch = 1, MaxCharacters = 500_000,
                },
                [nameof(PdfWerkAction.Inspect)] = new ActionLimit
                {
                    PerMinute = 60, PerHour = 1_200, PerDay = 6_000, Concurrent = 8,
                    MaxUploadBytes = 25 * 1024 * 1024, MaxPages = 2_000, MaxBatch = 1, MaxCharacters = 500_000,
                },
            },
        };

        private static TierPolicy Pro() => new()
        {
            Default = new ActionLimit
            {
                PerMinute = 120, PerHour = 3_000, PerDay = 30_000, Concurrent = 16,
                MaxUploadBytes = 100 * 1024 * 1024, MaxPages = 2_000, MaxBatch = 100, MaxCharacters = 2_000_000,
            },
            Actions =
            {
                // Far tighter than anything else, but not so tight that a person cannot correct a
                // mistake. The counter is spent before the handler runs, so a mistyped address or
                // a message a few characters short costs a send — three an hour would lock someone
                // out for fixing a typo twice. The hourly figure does the real work — ten an hour
                // is nothing to a spammer — so the per-minute one only needs to stop a tight
                // burst, and can be loose enough that correcting a mistake never trips it.
                //
                // The same ceiling applies at every tier: a paid key is not a reason to send more
                // email to us.
                [nameof(PdfWerkAction.Contact)] = new ActionLimit
                {
                    PerMinute = 5, PerHour = 10, PerDay = 20, Concurrent = 1,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 4_000,
                },
                [nameof(PdfWerkAction.Summarize)] = new ActionLimit
                {
                    PerMinute = 30, PerHour = 600, PerDay = 5_000, Concurrent = 8,
                    MaxUploadBytes = 100 * 1024 * 1024, MaxPages = 1_000, MaxBatch = 1, MaxCharacters = 2_000_000,
                },
                [nameof(PdfWerkAction.DraftDocument)] = new ActionLimit
                {
                    PerMinute = 30, PerHour = 600, PerDay = 5_000, Concurrent = 8,
                    MaxUploadBytes = 0, MaxPages = 0, MaxBatch = 1, MaxCharacters = 8_000,
                },
            },
        };

        private static TierPolicy Unlimited() => new()
        {
            Default = new ActionLimit
            {
                PerMinute = 0, PerHour = 0, PerDay = 0, Concurrent = 64,
                MaxUploadBytes = 250L * 1024 * 1024, MaxPages = 10_000, MaxBatch = 500, MaxCharacters = 10_000_000,
            },
        };
    }
}
