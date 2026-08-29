using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai.Providers;

/// <summary>
/// Google Gemini through the Generative Language API.
/// </summary>
/// <remarks>
/// The default provider: its free tier needs only a key from aistudio.google.com, and the
/// context window is large enough that most documents are summarised in a single call rather
/// than being chunked and merged.
/// </remarks>
public sealed class GeminiProvider(
    IHttpClientFactory factory,
    IOptions<AiOptions> options,
    ILogger<GeminiProvider> logger) : IAiProvider
{
    private readonly ProviderOptions _options = options.Value.Gemini;

    public string Key => "gemini";

    public string Model => _options.Model;

    public int ContextTokens => _options.ContextTokens;

    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(!string.IsNullOrWhiteSpace(_options.ApiKey));

    public async Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiUnavailableException("Gemini is not configured on this server.");

        var host = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://generativelanguage.googleapis.com"
            : _options.BaseUrl.TrimEnd('/');

        var request = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = [new GeminiPart { Text = prompt.System }] },
            Contents = [new GeminiContent { Role = "user", Parts = [new GeminiPart { Text = prompt.User }] }],
            GenerationConfig = new GeminiConfig
            {
                Temperature = prompt.Temperature,
                MaxOutputTokens = prompt.MaxOutputTokens,
            },
        };

        var client = factory.CreateClient(nameof(GeminiProvider));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        // The key travels as a header rather than a query parameter, so it cannot leak into
        // access logs or proxy traces.
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"{host}/v1beta/models/{_options.Model}:generateContent")
        {
            Content = JsonContent.Create(request, options: AiJson.Options),
        };

        message.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var response = await client.SendAsync(message, ct).ConfigureAwait(false);
        await AiJson.EnsureSuccessAsync(response, "Gemini", logger, ct).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<GeminiResponse>(AiJson.Options, ct)
            .ConfigureAwait(false);

        var text = payload?.Candidates?
            .FirstOrDefault()?.Content?.Parts?
            .FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            // An empty candidate list almost always means a safety filter fired.
            var reason = payload?.Candidates?.FirstOrDefault()?.FinishReason
                         ?? payload?.PromptFeedback?.BlockReason;

            throw new AiUnavailableException(
                reason is null
                    ? "Gemini returned an empty response."
                    : $"Gemini declined to answer ({reason}).");
        }

        return new AiCompletion(
            text.Trim(),
            _options.Model,
            payload?.UsageMetadata?.PromptTokenCount,
            payload?.UsageMetadata?.CandidatesTokenCount);
    }

    // ---- wire format -----------------------------------------------------

    private sealed class GeminiRequest
    {
        [JsonPropertyName("system_instruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GeminiConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class GeminiConfig
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsage? UsageMetadata { get; set; }

        [JsonPropertyName("promptFeedback")]
        public GeminiFeedback? PromptFeedback { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
    }

    private sealed class GeminiUsage
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }
    }

    private sealed class GeminiFeedback
    {
        [JsonPropertyName("blockReason")]
        public string? BlockReason { get; set; }
    }
}

/// <summary>Shared JSON settings and upstream error handling for the providers.</summary>
internal static class AiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Converts an upstream failure into a message the caller can act on, without leaking the
    /// provider's raw response — which can echo the request, and with it the document text.
    /// </summary>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string provider,
        ILogger logger,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        logger.LogWarning("{Provider} returned {Status}: {Body}", provider, (int)response.StatusCode, Truncate(body));

        throw new AiUnavailableException(response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                $"{provider} rejected the configured API key.",
            System.Net.HttpStatusCode.TooManyRequests =>
                $"{provider}'s free tier rate limit was reached. Try again shortly, or configure another provider.",
            System.Net.HttpStatusCode.RequestEntityTooLarge =>
                $"The document was too large for {provider} to process.",
            _ => $"{provider} is currently unavailable ({(int)response.StatusCode}).",
        });
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500] + "…";
}
