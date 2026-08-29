using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai;

/// <summary>
/// Resolves which model backend handles a request.
/// </summary>
/// <remarks>
/// Configuration names a default, but availability is a runtime property — a key can be revoked,
/// a free tier can be exhausted, a local Ollama can be down. So the default is used only if it is
/// actually usable, and otherwise any configured provider is taken instead. A caller that names a
/// provider explicitly gets that one or a clear error, never a silent substitution: pinning is
/// usually done for a reason, such as keeping documents on-premises.
/// </remarks>
public sealed class AiProviderRegistry(
    IEnumerable<IAiProvider> providers,
    IOptions<AiOptions> options,
    ILogger<AiProviderRegistry> logger) : IAiProviderRegistry
{
    private readonly AiOptions _options = options.Value;

    public IReadOnlyList<IAiProvider> All { get; } = providers.ToList();

    public async Task<IAiProvider> ResolveAsync(string? key, CancellationToken ct = default)
    {
        if (All.Count == 0)
            throw new AiUnavailableException("No AI provider is registered on this server.");

        if (!string.IsNullOrWhiteSpace(key))
            return await ResolveNamedAsync(key.Trim(), ct).ConfigureAwait(false);

        var preferred = All.FirstOrDefault(p =>
            string.Equals(p.Key, _options.DefaultProvider, StringComparison.OrdinalIgnoreCase));

        if (preferred is not null && await preferred.IsConfiguredAsync(ct).ConfigureAwait(false))
            return preferred;

        foreach (var provider in All)
        {
            if (await provider.IsConfiguredAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Default AI provider '{Default}' is unavailable; using '{Fallback}'.",
                    _options.DefaultProvider, provider.Key);

                return provider;
            }
        }

        throw new AiUnavailableException(
            "No AI provider is configured. Set an API key for Gemini or Groq, or run Ollama locally. " +
            $"Known providers: {string.Join(", ", All.Select(p => p.Key))}.");
    }

    private async Task<IAiProvider> ResolveNamedAsync(string key, CancellationToken ct)
    {
        var provider = All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new PdfWerkException(
                $"'{key}' is not a known AI provider. Available: {string.Join(", ", All.Select(p => p.Key))}.");
        }

        if (!await provider.IsConfiguredAsync(ct).ConfigureAwait(false))
            throw new AiUnavailableException($"The '{key}' provider is not configured on this server.");

        return provider;
    }
}
