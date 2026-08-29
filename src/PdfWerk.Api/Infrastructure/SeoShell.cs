using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PdfWerk.Api.Infrastructure;

/// <summary>
/// Serves the single-page app's shell with the metadata for the page actually being requested.
/// </summary>
/// <remarks>
/// The app renders in the browser, so a crawler that does not run JavaScript sees an empty
/// container and one title shared by every route. Google executes JavaScript; Bing is uneven at
/// it, and no social scraper does — a link to /merge posted anywhere would preview as the generic
/// site description.
///
/// So the shell is rewritten per route before it goes out: title, description, canonical, Open
/// Graph, Twitter card, and a schema.org block. Alongside them goes a short summary — the page's
/// own heading, its own introduction, and links to the other tools — which Vue discards the
/// moment it mounts. That is deliberately a faithful precis of the page rather than extra
/// keywords: the point is that a crawler without JavaScript sees what a reader sees, not more.
///
/// Rendered once per path and cached; the shell only changes when the app is rebuilt.
/// </remarks>
public sealed class SeoShell(SeoCatalogue catalogue, string indexPath)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly ConcurrentDictionary<string, string> rendered = new(StringComparer.OrdinalIgnoreCase);

    private string? shell;

    /// <summary>The shell for a path, or null when there is no metadata or no file to work from.</summary>
    public string? Render(string path)
    {
        var page = catalogue.Find(path);
        if (page is null) return null;

        return rendered.GetOrAdd(page.Path, _ => Build(page));
    }

    private string Build(SeoPage page)
    {
        shell ??= File.ReadAllText(indexPath);

        var canonical = catalogue.Absolute(page.Path);
        var image = catalogue.BaseUrl + catalogue.Document.DefaultImage;
        var site = catalogue.Document.SiteName;

        var head = new StringBuilder();

        head.AppendLine($"    <link rel=\"canonical\" href=\"{Escape(canonical)}\" />");

        head.AppendLine($"    <meta property=\"og:type\" content=\"website\" />");
        head.AppendLine($"    <meta property=\"og:site_name\" content=\"{Escape(site)}\" />");
        head.AppendLine($"    <meta property=\"og:title\" content=\"{Escape(page.Title)}\" />");
        head.AppendLine($"    <meta property=\"og:description\" content=\"{Escape(page.Description)}\" />");
        head.AppendLine($"    <meta property=\"og:url\" content=\"{Escape(canonical)}\" />");
        head.AppendLine($"    <meta property=\"og:image\" content=\"{Escape(image)}\" />");

        head.AppendLine($"    <meta name=\"twitter:card\" content=\"summary_large_image\" />");
        head.AppendLine($"    <meta name=\"twitter:title\" content=\"{Escape(page.Title)}\" />");
        head.AppendLine($"    <meta name=\"twitter:description\" content=\"{Escape(page.Description)}\" />");
        head.AppendLine($"    <meta name=\"twitter:image\" content=\"{Escape(image)}\" />");

        if (!string.IsNullOrWhiteSpace(catalogue.Document.Twitter))
            head.AppendLine($"    <meta name=\"twitter:site\" content=\"{Escape(catalogue.Document.Twitter!)}\" />");

        head.AppendLine($"    <script type=\"application/ld+json\">{StructuredData(page, canonical)}</script>");

        var html = ReplaceTag(shell, "<title>", "</title>", Escape(page.Title));
        html = ReplaceMeta(html, "description", page.Description);
        html = html.Replace("</head>", head + "  </head>", StringComparison.Ordinal);

        return html.Replace("<div id=\"app\"></div>", Summary(page), StringComparison.Ordinal);
    }

    /// <summary>
    /// A crawlable precis, replaced by the application as soon as it mounts.
    /// </summary>
    /// <remarks>
    /// Carries the page's real heading and introduction, plus links to the other tools so a
    /// crawler without JavaScript can still find them. Nothing here says anything the rendered
    /// page does not.
    /// </remarks>
    private string Summary(SeoPage page)
    {
        var links = string.Join(
            "\n        ",
            catalogue.Pages
                .Where(p => p.Path != page.Path)
                .Select(p => $"<li><a href=\"{Escape(p.Path)}\">{Escape(p.Heading ?? p.Title)}</a></li>"));

        return $"""
            <div id="app">
                <h1>{Escape(page.Heading ?? catalogue.Document.SiteName)}</h1>
                <p>{Escape(page.Intro ?? page.Description)}</p>
                <nav aria-label="Tools">
                  <ul>
                    {links}
                  </ul>
                </nav>
              </div>
            """;
    }

    private string StructuredData(SeoPage page, string canonical)
    {
        object payload = page.Type == "webapp"
            ? new
            {
                context = "https://schema.org",
                type = "WebApplication",
                name = page.Heading ?? page.Title,
                description = page.Description,
                url = canonical,
                applicationCategory = "BusinessApplication",
                operatingSystem = "Any",
                browserRequirements = "Requires JavaScript",
                isAccessibleForFree = true,
                offers = new { type = "Offer", price = "0", priceCurrency = "USD" },
                publisher = new { type = "Organization", name = catalogue.Document.SiteName, url = catalogue.BaseUrl },
            }
            : new
            {
                context = "https://schema.org",
                type = "WebSite",
                name = catalogue.Document.SiteName,
                description = page.Description,
                url = canonical,
                publisher = new { type = "Organization", name = catalogue.Document.SiteName, url = catalogue.BaseUrl },
            };

        // schema.org uses @-prefixed keys, which are not valid C# identifiers.
        return JsonSerializer.Serialize(payload, Json)
            .Replace("\"context\":", "\"@context\":", StringComparison.Ordinal)
            .Replace("\"type\":", "\"@type\":", StringComparison.Ordinal);
    }

    private static string ReplaceTag(string html, string open, string close, string value)
    {
        var start = html.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return html;

        var from = start + open.Length;
        var end = html.IndexOf(close, from, StringComparison.OrdinalIgnoreCase);

        return end < 0 ? html : html[..from] + value + html[end..];
    }

    /// <summary>Rewrites a named meta tag's content, leaving the rest of the document alone.</summary>
    private static string ReplaceMeta(string html, string name, string value)
    {
        var marker = $"name=\"{name}\"";
        var at = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return html;

        var start = html.LastIndexOf("<meta", at, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf('>', at);
        if (start < 0 || end < 0) return html;

        return html[..start] + $"<meta name=\"{name}\" content=\"{Escape(value)}\" />" + html[(end + 1)..];
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
