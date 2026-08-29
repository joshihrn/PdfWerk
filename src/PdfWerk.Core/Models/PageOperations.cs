namespace PdfWerk.Core.Models;

/// <summary>
/// A page selection, expressed the way people write it: "1-3,7,10-" or a named shorthand.
/// </summary>
/// <remarks>
/// Parsing lives in Core rather than in the API layer because the same syntax is used by split,
/// rotate and watermark, and a selection that means different things to different endpoints
/// would be a nasty surprise.
/// </remarks>
public static class PageRange
{
    /// <summary>
    /// Resolves a selection against a document of <paramref name="pageCount"/> pages.
    /// </summary>
    /// <returns>Distinct 1-based page numbers, in ascending order.</returns>
    /// <exception cref="PdfWerkException">The expression is malformed or selects nothing.</exception>
    public static IReadOnlyList<int> Resolve(string? expression, int pageCount)
    {
        if (pageCount <= 0)
            throw new PdfWerkException("The document has no pages.");

        var text = (expression ?? "all").Trim();

        if (text.Length == 0 || text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return [.. Enumerable.Range(1, pageCount)];

        if (text.Equals("odd", StringComparison.OrdinalIgnoreCase))
            return [.. Enumerable.Range(1, pageCount).Where(p => p % 2 == 1)];

        if (text.Equals("even", StringComparison.OrdinalIgnoreCase))
            return [.. Enumerable.Range(1, pageCount).Where(p => p % 2 == 0)];

        if (text.Equals("first", StringComparison.OrdinalIgnoreCase))
            return [1];

        if (text.Equals("last", StringComparison.OrdinalIgnoreCase))
            return [pageCount];

        var pages = new SortedSet<int>();

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var page in ParsePart(part, pageCount, text))
                pages.Add(page);
        }

        if (pages.Count == 0)
            throw new PdfWerkException($"'{text}' selected no pages in a {pageCount}-page document.");

        return [.. pages];
    }

    private static IEnumerable<int> ParsePart(string part, int pageCount, string whole)
    {
        var dash = part.IndexOf('-');

        // A bare number.
        if (dash < 0)
        {
            var page = ParsePage(part, pageCount, whole);
            return [page];
        }

        var fromText = part[..dash].Trim();
        var toText = part[(dash + 1)..].Trim();

        // "-5" means up to page 5; "5-" means page 5 to the end.
        var from = fromText.Length == 0 ? 1 : ParsePage(fromText, pageCount, whole);
        var to = toText.Length == 0 ? pageCount : ParsePage(toText, pageCount, whole);

        if (from > to)
            throw new PdfWerkException($"'{part}' is backwards; the start page must not be after the end page.");

        return Enumerable.Range(from, to - from + 1);
    }

    private static int ParsePage(string text, int pageCount, string whole)
    {
        if (!int.TryParse(text, out var page))
            throw new PdfWerkException($"'{whole}' is not a valid page range. Use something like 1-3,7,10- or all/odd/even.");

        if (page < 1 || page > pageCount)
            throw new PdfWerkException($"Page {page} is out of range; the document has {pageCount} page(s).");

        return page;
    }
}

/// <summary>How a split should divide the document.</summary>
public enum SplitMode
{
    /// <summary>One output containing exactly the selected pages.</summary>
    Extract,

    /// <summary>One output per selected page.</summary>
    Burst,

    /// <summary>One output per comma-separated group in the range expression.</summary>
    Groups,
}

public sealed record SplitRequest
{
    /// <summary>Page selection, e.g. "1-3,7" or "all". Defaults to the whole document.</summary>
    public string? Pages { get; init; }

    public SplitMode Mode { get; init; } = SplitMode.Extract;
}

/// <summary>One document produced by a split.</summary>
public sealed record SplitPart(string Name, byte[] Content, IReadOnlyList<int> Pages);

public sealed record RotateRequest
{
    public string? Pages { get; init; }

    /// <summary>Clockwise degrees. Must be a quarter turn: 90, 180 or 270 (or -90).</summary>
    public int Degrees { get; init; } = 90;

    /// <summary>Replace the page's existing rotation instead of adding to it.</summary>
    public bool Absolute { get; init; }
}

public enum WatermarkPosition { Diagonal, Horizontal, Vertical }

public sealed record WatermarkRequest
{
    public required string Text { get; init; }

    public string? Pages { get; init; }

    /// <summary>0 to 1. Values above about 0.4 make body text hard to read.</summary>
    public double Opacity { get; init; } = 0.15;

    /// <summary>Point size. Null scales the text to span roughly three quarters of the page.</summary>
    public double? FontSize { get; init; }

    public string Color { get; init; } = "#FF0000";

    public WatermarkPosition Position { get; init; } = WatermarkPosition.Diagonal;

    /// <summary>
    /// Draw beneath the page content rather than over it. Legible either way, but underneath
    /// keeps text fully readable — at the cost of being hidden by opaque backgrounds.
    /// </summary>
    public bool BehindContent { get; init; }
}
