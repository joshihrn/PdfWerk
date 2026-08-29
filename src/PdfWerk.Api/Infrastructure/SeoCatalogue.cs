using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfWerk.Api.Infrastructure;

/// <summary>
/// Page metadata, read from the same <c>seo.json</c> the web app imports.
/// </summary>
/// <remarks>
/// One file, two consumers, on purpose. The browser needs it to update the title as the router
/// moves between pages; the server needs it because most crawlers and every social scraper of
/// consequence fetch the HTML without executing any JavaScript, and would otherwise see the one
/// generic title the shell ships with on all ten pages.
///
/// Keeping two copies in step by discipline never works, so there is exactly one and the build
/// copies it into wwwroot alongside the app it belongs to.
/// </remarks>
public sealed class SeoCatalogue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, SeoPage> byPath;

    private SeoCatalogue(SeoDocument document, string baseUrl)
    {
        Document = document;
        BaseUrl = baseUrl.TrimEnd('/');

        byPath = document.Routes.ToDictionary(r => Normalise(r.Path), StringComparer.OrdinalIgnoreCase);
    }

    public SeoDocument Document { get; }

    /// <summary>Absolute origin used for canonical links, the sitemap and Open Graph URLs.</summary>
    public string BaseUrl { get; }

    public IReadOnlyList<SeoPage> Pages => Document.Routes;

    /// <summary>The pages that belong in a sitemap and in on-page links to other tools.</summary>
    public IEnumerable<SeoPage> PublicPages => Document.Routes.Where(p => !p.NoIndex);

    /// <summary>
    /// Loads the catalogue, or returns null when the file is absent — which is the normal state
    /// when the API runs without the web app built alongside it.
    /// </summary>
    public static SeoCatalogue? Load(string webRoot, string baseUrl)
    {
        var file = Path.Combine(webRoot, "seo.json");
        if (!File.Exists(file)) return null;

        try
        {
            var document = JsonSerializer.Deserialize<SeoDocument>(File.ReadAllText(file), Json);
            if (document is null || document.Routes.Count == 0) return null;

            return new SeoCatalogue(document, baseUrl);
        }
        catch (JsonException)
        {
            // Malformed metadata is not worth refusing to start over; the shell's own tags stand.
            return null;
        }
    }

    /// <summary>The page for a request path, or null when nothing is registered for it.</summary>
    public SeoPage? Find(string path) => byPath.GetValueOrDefault(Normalise(path));

    public string Absolute(string path) => path == "/" ? BaseUrl + "/" : BaseUrl + Normalise(path);

    /// <summary>Trailing slashes and casing are not meaningful here; "/create/" is "/create".</summary>
    private static string Normalise(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";

        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}

public sealed record SeoDocument
{
    [JsonPropertyName("siteName")]
    public string SiteName { get; init; } = "PdfWerk";

    [JsonPropertyName("twitter")]
    public string? Twitter { get; init; }

    [JsonPropertyName("defaultImage")]
    public string DefaultImage { get; init; } = "/og.png";

    [JsonPropertyName("routes")]
    public IReadOnlyList<SeoPage> Routes { get; init; } = [];
}

public sealed record SeoPage
{
    public required string Path { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    /// <summary>The page's own h1, repeated in the crawlable summary the shell carries.</summary>
    public string? Heading { get; init; }

    /// <summary>A faithful paragraph of what the page does, for the same summary.</summary>
    public string? Intro { get; init; }

    /// <summary>"website" or "webapp"; decides which schema.org type is declared.</summary>
    public string Type { get; init; } = "website";

    /// <summary>
    /// Keeps the page out of the sitemap and asks crawlers to skip it.
    /// </summary>
    /// <remarks>
    /// The administrative page needs this. It is not a secret — the server refuses anyone without
    /// a key regardless — but listing it in a sitemap advertises where to go looking, and having
    /// it turn up in results serves nobody.
    /// </remarks>
    public bool NoIndex { get; init; }
}
