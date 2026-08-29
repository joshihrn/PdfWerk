using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddHttpClient(nameof(GeminiProvider));
        services.AddHttpClient(nameof(GroqProvider));
        services.AddHttpClient(nameof(OllamaProvider));

        services.AddSingleton<IAiProvider, GeminiProvider>();
        services.AddSingleton<IAiProvider, GroqProvider>();
        services.AddSingleton<IAiProvider, OllamaProvider>();

        services.AddSingleton<IAiProviderRegistry, AiProviderRegistry>();
        services.AddSingleton<IPdfSummarizer, PdfSummarizer>();

        return services;
    }
}
