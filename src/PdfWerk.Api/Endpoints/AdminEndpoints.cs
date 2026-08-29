using PdfWerk.Api.Infrastructure;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// The administrative surface: the request log, the block list, and the rate limits.
/// </summary>
/// <remarks>
/// Every route here is gated on a key flagged as an administrator's. The check is a filter on the
/// whole group rather than a line in each handler, because the failure mode of the second is that
/// one handler eventually gets written without it and nothing says so.
///
/// Kept out of the OpenAPI document. It is not part of the public contract, and publishing the
/// shape of the administrative endpoints only helps someone probing for them.
/// </remarks>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/v1/admin")
            .AddEndpointFilter(RequireAdminAsync)
            .AddEndpointFilter(TranslateErrorsAsync)
            .ExcludeFromDescription();

        // ---- request log ---------------------------------------------------

        admin.MapGet("/requests", async (
            IRequestLog log,
            int? take,
            string? address,
            CancellationToken ct) =>
        {
            var rows = await log.RecentAsync(take ?? 100, address, ct).ConfigureAwait(false);
            return Results.Ok(new { total = await log.CountAsync(ct).ConfigureAwait(false), requests = rows });
        });

        // ---- blocks --------------------------------------------------------

        admin.MapGet("/blocks", async (IIpBlockList blocks, CancellationToken ct) =>
            Results.Ok(await blocks.ListAsync(ct).ConfigureAwait(false)));

        admin.MapPost("/blocks", async (
            BlockRequest request,
            IIpBlockList blocks,
            HttpContext context,
            CancellationToken ct) =>
        {
            var address = context.Items["pdfwerk.adminLabel"] as string ?? "admin";

            var record = await blocks
                .AddAsync(request.Cidr, request.Reason ?? "no reason given", address, request.ExpiresAt, ct)
                .ConfigureAwait(false);

            return Results.Ok(record);
        });

        admin.MapDelete("/blocks/{id:guid}", async (Guid id, IIpBlockList blocks, CancellationToken ct) =>
        {
            await blocks.RemoveAsync(id, ct).ConfigureAwait(false);
            return Results.Ok(new { unblocked = true });
        });

        // ---- rate limits ---------------------------------------------------

        admin.MapGet("/limits", async (IRateLimitSettings limits, CancellationToken ct) =>
            Results.Ok(await limits.ListAsync(ct).ConfigureAwait(false)));

        admin.MapPut("/limits", async (
            LimitSetting setting,
            IRateLimitSettings limits,
            HttpContext context,
            CancellationToken ct) =>
        {
            var by = context.Items["pdfwerk.adminLabel"] as string ?? "admin";

            await limits.SaveAsync(setting, by, ct).ConfigureAwait(false);
            return Results.Ok(new { saved = true });
        });

        admin.MapDelete("/limits/{tier}/{action?}", async (
            string tier,
            string? action,
            IRateLimitSettings limits,
            CancellationToken ct) =>
        {
            await limits.ResetAsync(tier, action ?? string.Empty, ct).ConfigureAwait(false);
            return Results.Ok(new { reset = true });
        });

        // ---- who am I ------------------------------------------------------

        // Lets the portal find out whether the key it holds is an admin one without having to
        // call something destructive to see whether it is refused.
        admin.MapGet("/me", (HttpContext context) => Results.Ok(new
        {
            admin = true,
            label = context.Items["pdfwerk.adminLabel"] as string ?? "admin",
        }));
    }

    /// <summary>
    /// Turns a domain exception into the response it describes.
    /// </summary>
    /// <remarks>
    /// The operation endpoints get this from ActionRunner, which the admin group does not go
    /// through. Without it a rejected CIDR came back as a stack trace with a 500 attached — which
    /// tells the caller nothing useful and tells an attacker rather a lot.
    /// </remarks>
    private static async ValueTask<object?> TranslateErrorsAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(invocation).ConfigureAwait(false);
        }
        catch (PdfWerkException ex)
        {
            return Results.Json(
                new { error = ex.StatusCode == 404 ? "not_found" : "bad_request", message = ex.Message },
                statusCode: ex.StatusCode);
        }
    }

    /// <summary>
    /// Refuses anything without a key flagged as an administrator's.
    /// </summary>
    /// <remarks>
    /// Answers 404 rather than 401 when the key is not an admin one. There is nothing to be
    /// gained by confirming to an unauthenticated caller that an admin API exists here, and a
    /// legitimate administrator knows it does.
    /// </remarks>
    private static async ValueTask<object?> RequireAdminAsync(
        EndpointFilterInvocationContext invocation,
        EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var keys = context.RequestServices.GetRequiredService<IApiKeyStore>();

        var presented = context.Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrWhiteSpace(presented))
            presented = context.Request.Headers.Authorization.ToString().Replace("Bearer ", string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(presented))
        {
            var record = await keys.ValidateAsync(presented, context.RequestAborted).ConfigureAwait(false);

            if (record is { IsAdmin: true })
            {
                context.Items["pdfwerk.adminLabel"] = record.Label;
                return await next(invocation).ConfigureAwait(false);
            }
        }

        return Results.NotFound(new { error = "not_found", message = "No such endpoint." });
    }
}

public sealed record BlockRequest(string Cidr, string? Reason, DateTimeOffset? ExpiresAt);
