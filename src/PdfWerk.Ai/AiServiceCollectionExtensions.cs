using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfWerk.Ai.Providers;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers every provider unconditionally, whether or not it holds credentials.
    /// </summary>
    /// <remarks>
    /// Registration is not the same as availability here: each provider decides at request time
    /// whether it can serve, which is what lets a key be added or an Ollama instance started
    /// without a restart, and what lets the API report the full provider list either way.
    /// </remarks>
    public static IServiceCollection AddPdfWerkAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        // Every provider gets the same retry policy: free tiers shed load routinely, and a
        // single 503 should not become a failed request for the caller.
        foreach (var name in new[] { nameof(GeminiProvider), nameof(GroqProvider), nameof(OllamaProvider) })
        {
            services.AddHttpClient(name).AddHttpMessageHandler(provider =>
                new TransientRetryHandler(
                    provider.GetRequiredService<ILoggerFactory>().CreateLogger($"PdfWerk.Ai.{name}")));
        }

        services.AddSingleton<IAiProvider, GeminiProvider>();
        services.AddSingleton<IAiProvider, GroqProvider>();
        services.AddSingleton<IAiProvider, OllamaProvider>();

        services.AddSingleton<IAiProviderRegistry, AiProviderRegistry>();
        services.AddSingleton<IPdfSummarizer, PdfSummarizer>();

        return services;
    }
}
