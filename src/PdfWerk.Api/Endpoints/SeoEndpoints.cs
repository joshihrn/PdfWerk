using System.Text;
using System.Xml.Linq;
using PdfWerk.Api.Infrastructure;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// robots.txt, sitemap.xml, and the per-route shell.
/// </summary>
/// <remarks>
/// Both files are generated from the same catalogue the pages themselves use, rather than
/// written out by hand. A sitemap maintained separately from the routes drifts the first time a
/// page is added, and a stale sitemap is worse than none — it asks a crawler to spend its budget
/// on URLs that no longer exist.
/// </remarks>
public static class SeoEndpoints
{
    public static void MapSeoEndpoints(this WebApplication app)
    {
        var catalogue = app.Services.GetService<SeoCatalogue>();
        if (catalogue is null) return;

        var shell = app.Services.GetRequiredService<SeoShell>();

        app.MapGet("/robots.txt", () =>
            {
                var body = new StringBuilder()
                    .AppendLine("User-agent: *")
                    .AppendLine("Allow: /")
                    .AppendLine()
                    // The API is for programs, not for crawlers. Indexing it wastes crawl budget
                    // on responses that are documents and JSON rather than pages, and every fetch
                    // of an operation endpoint would spend a caller's quota.
                    .AppendLine("Disallow: /v1/")
                    .AppendLine("Disallow: /health")
                    .AppendLine("Disallow: /admin")
                    .AppendLine()
                    .AppendLine($"Sitemap: {catalogue.BaseUrl}/sitemap.xml")
                    .ToString();

                return Results.Text(body, "text/plain", Encoding.UTF8);
            })
            .ExcludeFromDescription();

        app.MapGet("/sitemap.xml", () =>
            {
                XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

                // Excludes anything marked noindex. Listing the administrative page in a sitemap
                // would be advertising exactly where to go looking.
                var urls = catalogue.PublicPages.Select(page => new XElement(
                    ns + "url",
                    new XElement(ns + "loc", catalogue.Absolute(page.Path)),
                    // The landing page is the entry point; the tools are each a destination in
                    // their own right, which is the whole reason they have their own metadata.
                    new XElement(ns + "priority", page.Path == "/" ? "1.0" : "0.8"),
                    new XElement(ns + "changefreq", "weekly")));

                var document = new XDocument(new XElement(ns + "urlset", urls));

                // Written out rather than round-tripped through XDocument.Declaration, so there
                // is no question of the document being emitted without one: a sitemap whose first
                // byte is not "<" or "<?xml" is rejected outright by Search Console.
                var xml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{Environment.NewLine}{document}";

                return Results.Text(xml, "application/xml", Encoding.UTF8);
            })
            .ExcludeFromDescription();

        // Replaces MapFallbackToFile. A deep link to /merge has no file behind it, and until now
        // it was handed the shell with the site's generic title on it.
        app.MapFallback(async context =>
        {
            var html = shell.Render(context.Request.Path);

            if (catalogue.Find(context.Request.Path) is { NoIndex: true })
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

            if (html is null)
            {
                // An unknown path still gets the shell, so the router can redirect it — but it is
                // told not to index, because there is nothing there to index.
                context.Response.Headers["X-Robots-Tag"] = "noindex";

                var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
                if (!File.Exists(index))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                html = await File.ReadAllTextAsync(index);
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(html);
        });
    }
}
