using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai.Providers;

/// <summary>
/// Groq, through its OpenAI-compatible chat completions endpoint.
/// </summary>
/// <remarks>
/// Much faster than the alternatives and free to use with a key, at the cost of a smaller
/// context window — so long documents are chunked and merged more often here than on Gemini.
/// </remarks>
public sealed class GroqProvider(
    IHttpClientFactory factory,
    IOptions<AiOptions> options,
    ILogger<GroqProvider> logger) : IAiProvider
{
    private readonly ProviderOptions _options = options.Value.Groq;

    public string Key => "groq";

    public string Model => _options.Model;

    public int ContextTokens => _options.ContextTokens;

    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(!string.IsNullOrWhiteSpace(_options.ApiKey));

    public async Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiUnavailableException("Groq is not configured on this server.");

        var host = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.groq.com/openai/v1"
            : _options.BaseUrl.TrimEnd('/');

        var request = new ChatRequest
        {
            Model = _options.Model,
            Temperature = prompt.Temperature,
            MaxTokens = prompt.MaxOutputTokens,
            Messages =
            [
                new ChatMessage { Role = "system", Content = prompt.System },
                new ChatMessage { Role = "user", Content = prompt.User },
            ],
        };

        var client = factory.CreateClient(nameof(GroqProvider));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{host}/chat/completions")
        {
            Content = JsonContent.Create(request, options: AiJson.Options),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Exhausted retries, or ran out of time. Either way it is an upstream problem, and
            // the caller deserves a 503 saying so rather than an opaque 500.
            throw new AiUnavailableException(
                "Groq did not respond in time. Its free tier is often busy — try again in a moment, " +
                "or configure a second provider to fall back to.");
        }

        using (response)
        {
        await AiJson.EnsureSuccessAsync(response, "Groq", logger, ct).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<ChatResponse>(AiJson.Options, ct)
            .ConfigureAwait(false);

        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
            throw new AiUnavailableException("Groq returned an empty response.");

        return new AiCompletion(
            text.Trim(),
            payload?.Model ?? _options.Model,
            payload?.Usage?.PromptTokens,
            payload?.Usage?.CompletionTokens);
        }
    }

    // ---- wire format (OpenAI-compatible) ---------------------------------

    internal sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    internal sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    internal sealed class ChatResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public ChatUsage? Usage { get; set; }
    }

    internal sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    internal sealed class ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
