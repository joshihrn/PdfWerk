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

    public int TimeoutSeconds { get; set; } = 90;
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
        // 2.0 Flash has a genuinely free tier and a context large enough that most documents
        // need no chunking at all.
        Model = "gemini-2.0-flash",
        ContextTokens = 900_000,
    };

    public ProviderOptions Groq { get; set; } = new()
    {
        Model = "llama-3.3-70b-versatile",
        ContextTokens = 120_000,
    };

    public OllamaOptions Ollama { get; set; } = new();
}
