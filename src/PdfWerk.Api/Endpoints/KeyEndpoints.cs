using PdfWerk.Api.Infrastructure;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Infrastructure.Data;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// Self-service API key issuance.
/// </summary>
/// <remarks>
/// Anyone can mint a free-tier key without an account, which is the point: the API is meant to
/// be usable without building anything. That makes issuance itself an abuse target, so it is
/// rate limited harder than any document action — otherwise a caller could simply mint a fresh
/// key whenever they exhausted the last one, and the quotas would mean nothing.
/// </remarks>
public static class KeyEndpoints
{
    public sealed record CreateKeyRequest(string? Label);

    public static void MapKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var keys = app.MapGroup("/v1/keys").WithTags("API keys");

        keys.MapPost("/", async (
                HttpContext context,
                CreateKeyRequest? body,
                IApiKeyStore store,
                IRateLimiter limiter,
                ClientResolver clients,
                CancellationToken ct) =>
            {
                var client = await clients.ResolveAsync(context, ct).ConfigureAwait(false);

                // Issuance is charged against the caller's address, never against a key they
                // already hold, so holding a key does not buy the ability to mint more.
                var anonymous = ClientIdentity.Anonymous(client.Id.StartsWith("ip:", StringComparison.Ordinal)
                    ? client.Id[3..]
                    : client.Id);

                var decision = await limiter.AcquireAsync(anonymous, PdfWerkAction.Inspect, ct).ConfigureAwait(false);
                if (!decision.Allowed)
                    return ApiResults.TooManyRequests(PdfWerkAction.Inspect, decision);

                await using var lease = decision.Lease;

                var label = string.IsNullOrWhiteSpace(body?.Label) ? "self-service key" : body.Label;

                try
                {
                    var issued = await store
                        .CreateAsync(label, QuotaTier.Free, TimeSpan.FromDays(365), ct)
                        .ConfigureAwait(false);

                    return Results.Ok(new
                    {
                        id = issued.Record.Id,
                        label = issued.Record.Label,
                        tier = issued.Record.Tier.ToString(),
                        createdAt = issued.Record.CreatedAt,
                        expiresAt = issued.Record.ExpiresAt,

                        // The one and only time this value is ever returned.
                        key = issued.Secret,
                        warning = "Store this key now. It is hashed on the server and cannot be shown again.",
                        usage = "Send it as 'X-Api-Key: <key>' or 'Authorization: Bearer <key>'.",
                    });
                }
                catch (PdfWerkException ex)
                {
                    return Results.Json(new { error = "bad_request", message = ex.Message }, statusCode: ex.StatusCode);
                }
            })
            .WithName("CreateApiKey")
            .WithSummary("Mint a free-tier API key. The secret is shown once and never again.");

        keys.MapGet("/me", async (HttpContext context, ClientResolver clients, EfApiKeyStore store, CancellationToken ct) =>
            {
                var client = await clients.ResolveAsync(context, ct).ConfigureAwait(false);

                if (client.ApiKeyId is null)
                {
                    return Results.Json(
                        new
                        {
                            tier = client.Tier.ToString(),
                            authenticated = false,
                            message = await clients.HasInvalidKeyAsync(context, ct).ConfigureAwait(false)
                                ? "The key you presented is unknown, expired or revoked, so you are being served as anonymous."
                                : "No API key was presented. You are on the anonymous tier.",
                        },
                        statusCode: StatusCodes.Status200OK);
                }

                var record = await store.FindAsync(client.ApiKeyId.Value, ct).ConfigureAwait(false);
                if (record is null)
                    return Results.NotFound();

                return Results.Ok(new
                {
                    id = record.Id,
                    label = record.Label,
                    tier = record.Tier.ToString(),
                    authenticated = true,
                    createdAt = record.CreatedAt,
                    expiresAt = record.ExpiresAt,
                    lastUsedAt = record.LastUsedAt,
                    totalCalls = record.TotalCalls,
                });
            })
            .WithName("DescribeApiKey")
            .WithSummary("Describe the key presented with this request.");

        keys.MapDelete("/me", async (HttpContext context, ClientResolver clients, IApiKeyStore store, CancellationToken ct) =>
            {
                var client = await clients.ResolveAsync(context, ct).ConfigureAwait(false);

                // Only the holder of a key can revoke it, and only by presenting it. There is no
                // way to revoke someone else's key by guessing an id.
                if (client.ApiKeyId is null)
                    return Results.Json(new { error = "unauthorized", message = "Present the key you want to revoke." }, statusCode: 401);

                try
                {
                    await store.RevokeAsync(client.ApiKeyId.Value, ct).ConfigureAwait(false);
                    return Results.Ok(new { revoked = true, id = client.ApiKeyId });
                }
                catch (PdfWerkException ex)
                {
                    return Results.Json(new { error = "not_found", message = ex.Message }, statusCode: ex.StatusCode);
                }
            })
            .WithName("RevokeApiKey")
            .WithSummary("Revoke the key presented with this request.");
    }
}
