using System.Text.Json;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Limits;
using PdfWerk.Pdf;

namespace PdfWerk.Api.Infrastructure;

/// <summary>
/// The common wrapper around every action: identify the caller, charge their quota, apply the
/// hard input guards, run the work, and translate failures into clean HTTP.
/// </summary>
/// <remarks>
/// Centralising this is what makes the per-action limits trustworthy. If each endpoint enforced
/// its own quota, the one that forgot would be the one that gets abused, and on a public service
/// that is only a matter of time.
/// </remarks>
public sealed class ActionRunner(
    IRateLimiter limiter,
    ClientResolver clients,
    IOptions<RateLimitOptions> options,
    ILogger<ActionRunner> logger)
{
    private readonly RateLimitOptions _options = options.Value;

    public async Task<IResult> RunAsync(
        HttpContext context,
        PdfWerkAction action,
        Func<ActionLimit, CancellationToken, Task<IResult>> handler)
    {
        var client = clients.Resolve(context);
        var limit = _options.Limit(client.Tier, action);
        var ct = context.RequestAborted;

        var decision = await limiter.AcquireAsync(client, action, ct).ConfigureAwait(false);
        ApiResults.WriteQuotaHeaders(context.Response, action, decision);

        if (!decision.Allowed)
        {
            logger.LogInformation(
                "Rate limited {Client} on {Action} ({Window}).", client.Label, action, decision.Window);

            return ApiResults.TooManyRequests(action, decision);
        }

        // Releases the concurrency slot however this request ends.
        await using var lease = decision.Lease;

        try
        {
            return await handler(limit, ct).ConfigureAwait(false);
        }
        catch (PdfWerkException ex)
        {
            // A fault the caller can act on: say exactly what was wrong.
            return Results.Json(
                new { error = Slug(ex), action = action.ToString(), message = ex.Message },
                statusCode: ex.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client hung up; there is nobody left to answer.
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            // Anything else is our fault, and its detail is not the caller's business.
            logger.LogError(ex, "Unhandled failure in {Action}.", action);

            return Results.Json(
                new { error = "internal_error", action = action.ToString(), message = "Something went wrong handling this document." },
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string Slug(PdfWerkException ex) => ex switch
    {
        InvalidPdfException => "invalid_pdf",
        LimitExceededException => "limit_exceeded",
        AiUnavailableException => "ai_unavailable",
        _ => "bad_request",
    };
}

/// <summary>Input guards derived from the caller's tier, applied before any work begins.</summary>
public static class RequestGuard
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Reads an uploaded file, rejecting it if it exceeds the tier's size ceiling.</summary>
    public static async Task<byte[]> ReadAsync(IFormFile? file, ActionLimit limit, CancellationToken ct, string what = "file")
    {
        if (file is null || file.Length == 0)
            throw new PdfWerkException($"No {what} was uploaded.");

        if (file.Length > limit.MaxUploadBytes)
        {
            throw new LimitExceededException(
                $"'{file.FileName}' is {Megabytes(file.Length)} MB; the limit for your tier is {Megabytes(limit.MaxUploadBytes)} MB.");
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Rejects a PDF with more pages than the tier allows.</summary>
    public static void RequirePageBudget(byte[] pdf, ActionLimit limit, string fileName)
    {
        var pages = PdfProbe.PageCount(pdf);

        if (pages > limit.MaxPages)
        {
            throw new LimitExceededException(
                $"'{fileName}' has {pages} pages; the limit for your tier is {limit.MaxPages}.");
        }
    }

    public static void RequireBatchBudget(int count, ActionLimit limit, string what)
    {
        if (count > limit.MaxBatch)
            throw new LimitExceededException($"{count} {what} supplied; the limit for your tier is {limit.MaxBatch}.");
    }

    public static void RequireTextBudget(string? text, ActionLimit limit)
    {
        if (text is not null && text.Length > limit.MaxCharacters)
        {
            throw new LimitExceededException(
                $"The supplied text is {text.Length:N0} characters; the limit for your tier is {limit.MaxCharacters:N0}.");
        }
    }

    /// <summary>
    /// Reads a JSON options blob carried alongside a file in a multipart request.
    /// </summary>
    /// <remarks>
    /// Multipart is required for the file, but the options are a structured object, so they ride
    /// in a form field as JSON rather than being flattened into dozens of string fields.
    /// </remarks>
    public static T ReadJsonPart<T>(IFormCollection form, string field, T fallback)
    {
        if (!form.TryGetValue(field, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;

        try
        {
            return JsonSerializer.Deserialize<T>(raw.ToString(), Json) ?? fallback;
        }
        catch (JsonException ex)
        {
            throw new PdfWerkException($"The '{field}' field is not valid JSON: {ex.Message}");
        }
    }

    public static T RequireJsonPart<T>(IFormCollection form, string field)
    {
        if (!form.TryGetValue(field, out var raw) || string.IsNullOrWhiteSpace(raw))
            throw new PdfWerkException($"The '{field}' field is required and must contain JSON.");

        try
        {
            return JsonSerializer.Deserialize<T>(raw.ToString(), Json)
                   ?? throw new PdfWerkException($"The '{field}' field was empty.");
        }
        catch (JsonException ex)
        {
            throw new PdfWerkException($"The '{field}' field is not valid JSON: {ex.Message}");
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.#");
}
