using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Ai.Providers;

/// <summary>
/// A local Ollama instance.
/// </summary>
/// <remarks>
/// The option for anyone who cannot send documents to a third party: nothing leaves the host,
/// and there is no key or quota. Availability cannot be decided from configuration — an endpoint
/// is always configured — so it is probed, and the result cached briefly so that a summarize
/// request does not pay for a health check every time.
/// </remarks>
public sealed class OllamaProvider(
    IHttpClientFactory factory,
    IOptions<AiOptions> options,
    ILogger<OllamaProvider> logger) : IAiProvider
{
    private readonly OllamaOptions _options = options.Value.Ollama;

    private static readonly TimeSpan ProbeCacheFor = TimeSpan.FromSeconds(30);

    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;
    private bool _reachable;

    public string Key => "ollama";

    public string Model => _options.Model;

    public int ContextTokens => _options.ContextTokens;

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _probedAt < ProbeCacheFor)
            return _reachable;

        _probedAt = DateTimeOffset.UtcNow;
        _reachable = false;

        try
        {
            var client = factory.CreateClient(nameof(OllamaProvider));

            // Deliberately short: an unreachable local model must not delay the decision to
            // fall back to another provider.
            client.Timeout = TimeSpan.FromSeconds(2);

            using var response = await client
                .GetAsync($"{_options.BaseUrl.TrimEnd('/')}/api/tags", ct)
                .ConfigureAwait(false);

            _reachable = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            logger.LogDebug("Ollama is not reachable at {Endpoint}.", _options.BaseUrl);
        }

        return _reachable;
    }

    public async Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken ct = default)
    {
        var request = new OllamaRequest
        {
            Model = _options.Model,
            Stream = false,
            Messages =
            [
                new OllamaMessage { Role = "system", Content = prompt.System },
                new OllamaMessage { Role = "user", Content = prompt.User },
            ],
            Options = new OllamaTuning
            {
                Temperature = prompt.Temperature,
                NumPredict = prompt.MaxOutputTokens,
            },
        };

        var client = factory.CreateClient(nameof(OllamaProvider));
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        HttpResponseMessage response;
        try
        {
            response = await client
                .PostAsJsonAsync($"{_options.BaseUrl.TrimEnd('/')}/api/chat", request, AiJson.Options, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _reachable = false;
            throw new AiUnavailableException($"The local Ollama instance at {_options.BaseUrl} did not respond.");
        }

        using (response)
        {
            await AiJson.EnsureSuccessAsync(response, "Ollama", logger, ct).ConfigureAwait(false);

            var payload = await response.Content
                .ReadFromJsonAsync<OllamaResponse>(AiJson.Options, ct)
                .ConfigureAwait(false);

            var text = payload?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text))
                throw new AiUnavailableException("Ollama returned an empty response.");

            return new AiCompletion(
                text.Trim(),
                payload?.Model ?? _options.Model,
                payload?.PromptEvalCount,
                payload?.EvalCount);
        }
    }

    // ---- wire format -----------------------------------------------------

    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaTuning? Options { get; set; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class OllamaTuning
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }
    }
}
