namespace PdfWerk.Core.Abstractions;

/// <summary>A single completion request against a text model.</summary>
public sealed record AiPrompt(string System, string User, int MaxOutputTokens = 2048, double Temperature = 0.2);

public sealed record AiCompletion(string Text, string Model, int? PromptTokens = null, int? CompletionTokens = null);

/// <summary>
/// A free-tier text model backend. Implementations are registered by <see cref="Key"/> and
/// selected per-request, so callers can pin a provider or take the configured default.
/// </summary>
public interface IAiProvider
{
    /// <summary>Lowercase identifier used in config and in the API's `provider` parameter.</summary>
    string Key { get; }

    /// <summary>Model id this instance will call.</summary>
    string Model { get; }

    /// <summary>Approximate input budget, used to decide whether a document needs chunking.</summary>
    int ContextTokens { get; }

    /// <summary>False when the provider has no credentials or its host is unreachable.</summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken ct = default);
}

/// <summary>Resolves providers by key and knows which one is the configured default.</summary>
public interface IAiProviderRegistry
{
    IReadOnlyList<IAiProvider> All { get; }

    /// <summary>Returns the named provider, or the default when <paramref name="key"/> is null/empty.</summary>
    /// <exception cref="AiUnavailableException">No such provider, or none configured at all.</exception>
    Task<IAiProvider> ResolveAsync(string? key, CancellationToken ct = default);
}

/// <summary>Turns extracted document text into a structured summary, chunking when necessary.</summary>
public interface IPdfSummarizer
{
    Task<Models.SummarizeResult> SummarizeAsync(
        byte[] pdf,
        Models.SummarizeRequest request,
        CancellationToken ct = default);
}
