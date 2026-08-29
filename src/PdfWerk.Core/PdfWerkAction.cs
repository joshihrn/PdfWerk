namespace PdfWerk.Core;

/// <summary>
/// Every billable / rate-limited operation PdfWerk exposes. The enum member name is the
/// canonical key used in configuration, Redis counters and quota response headers.
/// </summary>
public enum PdfWerkAction
{
    /// <summary>Render supplied text or Markdown into a new PDF.</summary>
    CreateFromText,

    /// <summary>Convert an uploaded .docx / .doc into a PDF.</summary>
    CreateFromWord,

    /// <summary>Search-and-replace text inside an existing PDF.</summary>
    EditText,

    /// <summary>Add or remove AcroForm fields on an existing PDF.</summary>
    EditFormFields,

    /// <summary>Merge a set of field name/value pairs into an existing AcroForm.</summary>
    FillForm,

    /// <summary>Concatenate several PDFs into one.</summary>
    Merge,

    /// <summary>Produce an AI summary of a PDF's text content.</summary>
    Summarize,

    /// <summary>Read-only introspection: page count, metadata, field list.</summary>
    Inspect,

    /// <summary>Split a document into one or more page ranges.</summary>
    Split,

    /// <summary>Rotate pages by a quarter turn.</summary>
    Rotate,

    /// <summary>Stamp text across each page.</summary>
    Watermark,

    /// <summary>Apply a password and permission flags.</summary>
    Protect,
}

/// <summary>Human-facing metadata for an action, surfaced on the landing page and in the API docs.</summary>
public sealed record ActionDescriptor(
    PdfWerkAction Action,
    string Slug,
    string Title,
    string Summary,
    bool RequiresAi);

/// <summary>Static catalogue of the actions, keyed by URL slug.</summary>
public static class ActionCatalog
{
    public static IReadOnlyList<ActionDescriptor> All { get; } =
    [
        new(PdfWerkAction.CreateFromText, "create/text", "Create PDF from text",
            "Turn plain text or Markdown into a clean, paginated PDF.", false),
        new(PdfWerkAction.CreateFromWord, "create/word", "Create PDF from Word",
            "Convert .docx or .doc documents to PDF with layout preserved.", false),
        new(PdfWerkAction.EditText, "edit/text", "Update text in a PDF",
            "Find and replace text content inside an existing PDF.", false),
        new(PdfWerkAction.EditFormFields, "forms/design", "Add or remove form fields",
            "Place, move and delete AcroForm fields on an existing PDF.", false),
        new(PdfWerkAction.FillForm, "forms/fill", "Merge values into a form",
            "Populate an existing AcroForm with values, optionally flattening the result.", false),
        new(PdfWerkAction.Merge, "merge", "Merge PDFs",
            "Combine several PDFs into a single document, in order.", false),
        new(PdfWerkAction.Summarize, "summarize", "Summarize a PDF",
            "Extract the text and produce a structured AI summary.", true),
        new(PdfWerkAction.Inspect, "inspect", "Inspect a PDF",
            "Report page count, metadata and the AcroForm field inventory.", false),
        new(PdfWerkAction.Split, "split", "Split a PDF",
            "Pull out page ranges, or burst a document into single pages.", false),
        new(PdfWerkAction.Rotate, "rotate", "Rotate pages",
            "Turn selected pages by 90, 180 or 270 degrees.", false),
        new(PdfWerkAction.Watermark, "watermark", "Watermark a PDF",
            "Stamp text across every page, above or beneath the content.", false),
        new(PdfWerkAction.Protect, "protect", "Password-protect a PDF",
            "Require a password to open, and restrict printing, copying or editing.", false),
    ];

    public static ActionDescriptor Get(PdfWerkAction action) =>
        All.First(a => a.Action == action);

    public static ActionDescriptor? BySlug(string slug) =>
        All.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
