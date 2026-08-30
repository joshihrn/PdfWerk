namespace PdfWerk.Core.Models;

/// <summary>Page geometry for newly created documents.</summary>
public enum PagePreset { A4, Letter, Legal, A3, A5 }

public enum PageOrientation { Portrait, Landscape }

/// <summary>Input format for <see cref="PdfWerkAction.CreateFromText"/>.</summary>
public enum TextFormat
{
    /// <summary>Treat input literally; blank lines separate paragraphs.</summary>
    Plain,

    /// <summary>Interpret a practical subset of Markdown: headings, lists, emphasis, rules, code, quotes, tables.</summary>
    Markdown,
}

public sealed record CreateFromTextRequest
{
    public required string Content { get; init; }

    public TextFormat Format { get; init; } = TextFormat.Markdown;

    public string? Title { get; init; }

    public string? Author { get; init; }

    public PagePreset Page { get; init; } = PagePreset.A4;

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>Page margin in millimetres, applied on all four sides.</summary>
    public double MarginMm { get; init; } = 20;

    public string FontFamily { get; init; } = "Helvetica";

    public double FontSize { get; init; } = 11;

    /// <summary>Render "Page N of M" in the footer.</summary>
    public bool PageNumbers { get; init; } = true;

}

/// <summary>What to draw onto a page.</summary>
public enum AnnotateItemType { Text, Rectangle, Line }

/// <summary>
/// One thing to draw, positioned from the top-left of the page in points.
/// </summary>
/// <remarks>
/// Top-left rather than PDF's bottom-left origin, matching <see cref="FieldRect"/> and the
/// designer's canvas — so a coordinate taken from one part of the UI means the same thing
/// everywhere.
/// </remarks>
public sealed record AnnotateItem
{
    public AnnotateItemType Type { get; init; } = AnnotateItemType.Text;

    /// <summary>1-based page number.</summary>
    public required int Page { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    /// <summary>For text, the wrap width; zero draws a single unwrapped line.</summary>
    public double Width { get; init; }

    /// <summary>For text, the clip height; zero lets it run as long as it needs.</summary>
    public double Height { get; init; }

    public string? Text { get; init; }

    public double FontSize { get; init; } = 11;

    public string FontFamily { get; init; } = "Helvetica";

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>#RRGGBB. Defaults to black.</summary>
    public string? Color { get; init; }

    public double Opacity { get; init; } = 1;

    /// <summary>Stroke width for lines and unfilled rectangles.</summary>
    public double LineWidth { get; init; } = 1;

    /// <summary>Fill a rectangle rather than outline it.</summary>
    public bool Filled { get; init; }
}

/// <summary>Draw text and shapes onto an existing document.</summary>
public sealed record AnnotateRequest
{
    public required IReadOnlyList<AnnotateItem> Items { get; init; }
}

/// <summary>A brief to draft a document from.</summary>
public sealed record DraftRequest
{
    /// <summary>What the document should say, in the caller's own words.</summary>
    public required string Brief { get; init; }

    /// <summary>Steers the draft. Printed by the composer later, not by the drafter.</summary>
    public string? Title { get; init; }

    /// <summary>Provider key, or null for the configured default.</summary>
    public string? Provider { get; init; }
}

/// <summary>One find-and-replace instruction for <see cref="PdfWerkAction.EditText"/>.</summary>
public sealed record TextReplacement
{
    public required string Find { get; init; }

    public required string Replace { get; init; }

    public bool MatchCase { get; init; } = true;

    /// <summary>Restrict to a single 1-based page; null applies the replacement document-wide.</summary>
    public int? Page { get; init; }
}

public sealed record EditTextRequest
{
    public required IReadOnlyList<TextReplacement> Replacements { get; init; }

    /// <summary>Fail the whole request if any instruction matched nothing, rather than silently no-op.</summary>
    public bool FailOnNoMatch { get; init; } = true;
}

public sealed record EditFormFieldsRequest
{
    /// <summary>Fields to create. Names must not already exist unless <see cref="Replace"/> is set.</summary>
    public IReadOnlyList<FormFieldSpec> Add { get; init; } = [];

    /// <summary>Names of existing fields to delete, along with their widget annotations.</summary>
    public IReadOnlyList<string> Remove { get; init; } = [];

    /// <summary>Treat an added field whose name already exists as a replacement instead of an error.</summary>
    public bool Replace { get; init; }
}

public sealed record FillFormRequest
{
    /// <summary>Field name to value. Checkboxes accept "true"/"false"; choice fields accept an option value.</summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }

    /// <summary>Bake the values into page content and drop the form, producing a non-editable document.</summary>
    public bool Flatten { get; init; }

    /// <summary>Reject the request if a supplied key does not exist in the form.</summary>
    public bool StrictFieldNames { get; init; } = true;
}

public enum SummaryStyle { Brief, Detailed, Bullets, ExecutiveSummary }

public sealed record SummarizeRequest
{
    public SummaryStyle Style { get; init; } = SummaryStyle.Brief;

    /// <summary>Soft target for the summary length. The provider is asked, not forced, to respect it.</summary>
    public int MaxWords { get; init; } = 250;

    /// <summary>Optional focus, e.g. "the payment terms and any termination clauses".</summary>
    public string? Focus { get; init; }

    /// <summary>Override the configured default provider by key, e.g. "gemini", "groq", "ollama".</summary>
    public string? Provider { get; init; }

    /// <summary>Also return the extracted text alongside the summary.</summary>
    public bool IncludeExtractedText { get; init; }
}

public sealed record SummarizeResult(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    int PageCount,
    int WordCount,
    string ProviderUsed,
    string ModelUsed,
    string? ExtractedText);
