namespace PdfWerk.Ai;

/// <summary>Settings shared by every provider.</summary>
public class ProviderOptions
{
    /// <summary>Credential. An empty value means the provider is simply not configured.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id to call.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Approximate input budget in tokens, used to decide whether a document must be chunked.
    /// Set conservatively: overshooting means a hard failure from the provider mid-request.
    /// </summary>
    public int ContextTokens { get; set; }

    /// <summary>Override the API host, for a proxy or a self-hosted gateway.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Models to try, in order, when <see cref="Model"/> is retired or shedding load. Free
    /// tiers do both routinely, and a chain keeps the feature working through either.
    /// </summary>
    public string[] FallbackModels { get; set; } = [];

    /// <summary>
    /// Overall budget for a completion, retries included. Must exceed the retry handler's
    /// per-attempt timeout multiplied by its attempt count, or the last attempt is cancelled
    /// before it can finish.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Ollama needs no key, so availability is decided by whether the host answers.</summary>
public sealed class OllamaOptions : ProviderOptions
{
    public OllamaOptions()
    {
        BaseUrl = "http://localhost:11434";
        Model = "llama3.1";
        ContextTokens = 100_000;
    }
}

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Provider key used when a request does not name one. If it is unconfigured at runtime,
    /// the registry falls back to any provider that is.
    /// </summary>
    public string DefaultProvider { get; set; } = "gemini";

    public ProviderOptions Gemini { get; set; } = new()
    {
        // A specific model first, then the aliases. Measured against a live free-tier key, the
        // "-latest" aliases were consistently the most congested — everything defaults to them —
        // while a named current model answered in about two seconds.
        Model = "gemini-3.5-flash",
        FallbackModels = ["gemini-flash-latest", "gemini-3.1-flash-lite", "gemini-flash-lite-latest"],
        ContextTokens = 1_000_000,
    };

    public ProviderOptions Groq { get; set; } = new()
    {
        Model = "llama-3.3-70b-versatile",
        ContextTokens = 120_000,
    };

    public OllamaOptions Ollama { get; set; } = new();
}
