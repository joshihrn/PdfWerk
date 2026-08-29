using System.Diagnostics;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Api.Infrastructure;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Mints an admin key at startup if none exists. Set it once, sign in, then remove it —
    /// a bootstrap credential left in configuration is a standing back door.
    /// </summary>
    public string BootstrapKey { get; set; } = string.Empty;

    /// <summary>
    /// Days to keep request log rows. 0 keeps them indefinitely.
    /// </summary>
    /// <remarks>
    /// Indefinite by default because that is what was asked for, but worth a decision rather than
    /// a shrug: addresses are personal data in the UK and EU, an unbounded table is a growing
    /// storage cost, and "we kept everything forever" is a poor answer to why.
    /// </remarks>
    public int RetentionDays { get; set; }
}

/// <summary>
/// Refuses blocked addresses, and records every request that matters.
/// </summary>
/// <remarks>
/// Placed ahead of everything else it can be. A blocked caller should not reach the rate limiter,
/// the key store or the PDF engine — the point of a block is that it costs nothing to enforce.
///
/// Static assets are skipped. Logging them would multiply every page view by ten or more, bury
/// the interesting rows under stylesheets, and tell an administrator nothing they could act on.
/// </remarks>
public sealed class RequestAuditMiddleware(
    RequestDelegate next,
    IIpBlockList blocks,
    IRequestLog log,
    ClientResolver clients)
{
    private static readonly string[] IgnoredExtensions =
        [".css", ".js", ".mjs", ".map", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".webp", ".woff", ".woff2", ".ttf"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (IsAsset(path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var address = clients.AddressFor(context);

        if (blocks.IsBlocked(address))
        {
            // Recorded before refusing, so the log shows what a blocked caller kept trying — which
            // is the evidence for whether the block is working and whether it is still needed.
            log.Record(new RequestLogEntry
            {
                Address = address,
                Method = context.Request.Method,
                Path = path,
                StatusCode = StatusCodes.Status403Forbidden,
                ElapsedMs = 0,
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                Blocked = true,
            });

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            await context.Response
                .WriteAsync("""{"error":"blocked","message":"This address has been blocked."}""")
                .ConfigureAwait(false);

            return;
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            // In a finally block so a request that threw is still recorded. Those are the ones
            // worth seeing.
            log.Record(new RequestLogEntry
            {
                Address = address,
                Method = context.Request.Method,
                Path = path,
                StatusCode = context.Response.StatusCode,
                ElapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                ApiKeyId = context.Items["pdfwerk.apiKeyId"] as Guid?,
                ClientId = context.Items["pdfwerk.clientId"] as string ?? string.Empty,
                Action = context.Items["pdfwerk.action"] as PdfWerkAction?,
                Blocked = false,
            });
        }
    }

    /// <summary>
    /// True for the files a browser fetches to render a page, as opposed to the page itself.
    /// </summary>
    private static bool IsAsset(string path)
    {
        var lastDot = path.LastIndexOf('.');
        if (lastDot < 0) return false;

        var extension = path[lastDot..].ToLowerInvariant();
        return Array.IndexOf(IgnoredExtensions, extension) >= 0;
    }
}
