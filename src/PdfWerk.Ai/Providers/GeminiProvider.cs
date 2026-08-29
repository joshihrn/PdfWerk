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
/// <para>
/// The default provider: its free tier needs only a key from aistudio.google.com, and the context
/// window is large enough that most documents are summarised in one call.
/// </para>
/// <para>
/// Free-tier model availability is genuinely unreliable, in two distinct ways. Models get retired
/// (a pinned name starts returning 404 and the feature dies silently), and popular models shed
/// load (503 "experiencing high demand"). Neither pinning nor tracking a "-latest" alias survives
/// both — in testing the aliases were the <em>most</em> congested, because everything defaults to
/// them. So the provider walks a list of models and uses the first that answers.
/// </para>
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

        var models = new List<string> { _options.Model };
        models.AddRange(_options.FallbackModels.Where(m => !string.IsNullOrWhiteSpace(m)));

        AiUnavailableException? last = null;

        for (var i = 0; i < models.Count; i++)
        {
            try
            {
                return await CompleteWithAsync(models[i], prompt, ct).ConfigureAwait(false);
            }
            catch (AiUnavailableException ex) when (i < models.Count - 1)
            {
                // Retired or overloaded. Both are reasons to try the next model rather than to
                // fail the caller's request.
                logger.LogWarning(
                    "Gemini model '{Model}' unavailable ({Reason}); trying '{Next}'.",
                    models[i], ex.Message, models[i + 1]);

                last = ex;
            }
        }

        throw last ?? new AiUnavailableException("Gemini is unavailable.");
    }

    private async Task<AiCompletion> CompleteWithAsync(string model, AiPrompt prompt, CancellationToken ct)
    {
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
            $"{host}/v1beta/models/{model}:generateContent")
        {
            Content = JsonContent.Create(request, options: AiJson.Options),
        };

        message.Headers.Add("x-goog-api-key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Retries exhausted, or the budget ran out. An upstream problem either way, and the
            // caller deserves a 503 saying so rather than an opaque 500.
            throw new AiUnavailableException(
                $"Gemini did not respond in time using '{model}'. Its free tier is often busy — " +
                "try again shortly, or configure another provider.");
        }

        using (response)
        {
            await AiJson.EnsureSuccessAsync(response, "Gemini", logger, ct).ConfigureAwait(false);

            var payload = await response.Content
                .ReadFromJsonAsync<GeminiResponse>(AiJson.Options, ct)
                .ConfigureAwait(false);

            var candidate = payload?.Candidates?.FirstOrDefault();
            var text = candidate?.Content?.Parts?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text))?.Text;

            // A truncated reply is worse than no reply: it looks like a summary but stops
            // mid-sentence, and on a JSON-shaped response it does not parse at all. Reasoning
            // models make this easy to hit, because thinking is billed against the same budget.
            if (candidate?.FinishReason == "MAX_TOKENS")
            {
                throw new AiUnavailableException(
                    "Gemini ran out of output tokens before completing the summary. " +
                    "Reasoning models spend part of that budget on thinking — raise the limit or shorten the target length.");
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                return new AiCompletion(
                    text.Trim(),
                    model,
                    payload?.UsageMetadata?.PromptTokenCount,
                    payload?.UsageMetadata?.CandidatesTokenCount);
            }

            var reason = candidate?.FinishReason ?? payload?.PromptFeedback?.BlockReason;

            // MAX_TOKENS means the answer was cut off before any text survived — a budget
            // problem on our side rather than a refusal, and worth saying distinctly.
            throw new AiUnavailableException(reason switch
            {
                "MAX_TOKENS" => "Gemini ran out of output tokens before returning a summary. Try a shorter target length.",
                null => "Gemini returned an empty response.",
                _ => $"Gemini declined to answer ({reason}).",
            });
        }
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

            // A 404 here is nearly always a retired or misspelled model name rather than an
            // outage, and saying "unavailable" sends the operator looking in the wrong place.
            System.Net.HttpStatusCode.NotFound =>
                $"{provider} does not recognise the configured model. It may have been retired — " +
                "check the model name against the provider's current model list.",

            System.Net.HttpStatusCode.ServiceUnavailable =>
                $"{provider} is shedding load right now. This is usually brief.",

            _ => $"{provider} is currently unavailable ({(int)response.StatusCode}).",
        });
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500] + "…";
}
