using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai.Providers;

/// <summary>
/// The operator's own self-hosted model, through its OpenAI-compatible chat completions
/// endpoint.
/// </summary>
/// <remarks>
/// Same wire format as <see cref="GroqProvider"/> - this is just a different host behind the
/// same Bearer-key contract, so document text never has to leave infrastructure the operator
/// controls, and this product pays nothing per token.
/// </remarks>
public sealed class JambuProvider(
    IHttpClientFactory factory,
    IOptions<AiOptions> options,
    ILogger<JambuProvider> logger) : IAiProvider
{
    private readonly ProviderOptions _options = options.Value.Jambu;

    public string Key => "jambu";

    public string Model => _options.Model;

    public int ContextTokens => _options.ContextTokens;

    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        ValueTask.FromResult(!string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.BaseUrl));

    public async Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new AiUnavailableException("Jambu is not configured on this server.");

        var host = _options.BaseUrl.TrimEnd('/');

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

        var client = factory.CreateClient(nameof(JambuProvider));
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
            // The workstation this points at can be off, asleep, or between restarts - that is
            // an upstream problem for the caller, same as any other provider going dark.
            throw new AiUnavailableException("Jambu did not respond in time.");
        }

        using (response)
        {
            await AiJson.EnsureSuccessAsync(response, "Jambu", logger, ct).ConfigureAwait(false);

            var payload = await response.Content
                .ReadFromJsonAsync<ChatResponse>(AiJson.Options, ct)
                .ConfigureAwait(false);

            var text = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text))
                throw new AiUnavailableException("Jambu returned an empty response.");

            return new AiCompletion(
                text.Trim(),
                payload?.Model ?? _options.Model,
                payload?.Usage?.PromptTokens,
                payload?.Usage?.CompletionTokens);
        }
    }

    // ---- wire format (OpenAI-compatible, identical shape to GroqProvider's) -------------

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
