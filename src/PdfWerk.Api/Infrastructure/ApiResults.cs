using System.Globalization;
using PdfWerk.Core;
using PdfWerk.Core.Limits;
using PdfWerk.Core.Models;

namespace PdfWerk.Api.Infrastructure;

/// <summary>
/// Turns engine output into HTTP responses, honouring the caller's chosen delivery mode.
/// </summary>
/// <remarks>
/// Every producing endpoint supports the same three modes, because the same endpoint serves a
/// browser download, an iframe preview and a server-to-server integration. The plugin relies on
/// this: it embeds the identical API and only varies <c>delivery</c>.
/// </remarks>
public static class ApiResults
{
    /// <summary>Reads the delivery mode from the query string, defaulting to a file download.</summary>
    public static DeliveryMode DeliveryFrom(HttpRequest request)
    {
        var raw = request.Query["delivery"].ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            // A caller that asked for JSON in the Accept header gets the JSON envelope.
            return request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                ? DeliveryMode.Json
                : DeliveryMode.Download;
        }

        return raw.ToLowerInvariant() switch
        {
            "stream" or "inline" => DeliveryMode.Stream,
            "json" or "base64" => DeliveryMode.Json,
            _ => DeliveryMode.Download,
        };
    }

    /// <summary>The JSON envelope, for callers that want metadata alongside the document.</summary>
    public sealed record DocumentEnvelope(
        string FileName,
        string ContentType,
        int ByteCount,
        string Base64,
        IReadOnlyDictionary<string, object>? Meta);

    public static IResult Document(
        PdfArtifact artifact,
        DeliveryMode delivery,
        IReadOnlyDictionary<string, object>? meta = null)
    {
        if (delivery == DeliveryMode.Json)
        {
            return Results.Ok(new DocumentEnvelope(
                artifact.FileName,
                artifact.ContentType,
                artifact.ByteCount,
                Convert.ToBase64String(artifact.Content),
                meta));
        }

        // Download sets a filename so the browser saves it; stream omits it so the document
        // renders in place, which is what an embedded preview needs.
        return Results.File(
            artifact.Content,
            artifact.ContentType,
            fileDownloadName: delivery == DeliveryMode.Download ? artifact.FileName : null);
    }

    /// <summary>
    /// Publishes quota state on every response, so integrators can back off before being
    /// rejected rather than discovering the limit by hitting it.
    /// </summary>
    public static void WriteQuotaHeaders(HttpResponse response, PdfWerkAction action, RateLimitDecision decision)
    {
        response.Headers["X-PdfWerk-Action"] = action.ToString();

        if (decision.Limit == int.MaxValue)
            return;

        response.Headers["X-RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        response.Headers["X-RateLimit-Reset"] = decision.ResetsAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(decision.Window))
            response.Headers["X-RateLimit-Window"] = decision.Window;
    }

    /// <summary>The 429 body, describing which window tripped and when it frees up.</summary>
    public static IResult TooManyRequests(PdfWerkAction action, RateLimitDecision decision)
    {
        var retryAfter = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));

        return Results.Json(
            new
            {
                error = "rate_limited",
                action = action.ToString(),
                window = decision.Window,
                limit = decision.Limit,
                retryAfterSeconds = retryAfter,
                resetsAt = decision.ResetsAt,
                message = decision.Window == "concurrent"
                    ? $"You already have {decision.Limit} {action} request(s) in flight. Wait for one to finish."
                    : $"Rate limit reached for {action}: {decision.Limit} per {decision.Window}. Try again in {retryAfter}s.",
            },
            statusCode: StatusCodes.Status429TooManyRequests);
    }
}
