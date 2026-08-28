namespace PdfWerk.Core.Models;

/// <summary>The AcroForm widget types PdfWerk can create and manipulate.</summary>
public enum FormFieldType
{
    Text,
    Checkbox,
    RadioGroup,
    Dropdown,
    ListBox,
    Signature,
}

/// <summary>
/// A field's placement on the page, in PDF user-space points with the origin at the
/// TOP-LEFT of the page. The drag-and-drop designer works in this space so that the
/// browser's CSS coordinates map across with only a scale factor.
/// </summary>
/// <param name="Page">1-based page number.</param>
public sealed record FieldRect(int Page, double X, double Y, double Width, double Height)
{
    public void Validate()
    {
        if (Page < 1)
            throw new PdfWerkException($"Page must be 1 or greater, got {Page}.");
        if (Width <= 0 || Height <= 0)
            throw new PdfWerkException($"Field on page {Page} must have positive width and height.");
        if (X < 0 || Y < 0)
            throw new PdfWerkException($"Field on page {Page} must sit at non-negative coordinates.");
    }
}

/// <summary>A field to add to a document's AcroForm.</summary>
public sealed record FormFieldSpec
{
    public required string Name { get; init; }

    public FormFieldType Type { get; init; } = FormFieldType.Text;

    public required FieldRect Rect { get; init; }

    /// <summary>Initial value. For checkboxes, "true"/"false"; for choice fields, one of <see cref="Options"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Tooltip / accessible name shown by readers.</summary>
    public string? ToolTip { get; init; }

    public bool Required { get; init; }

    public bool ReadOnly { get; init; }

    public bool Multiline { get; init; }

    /// <summary>Maximum characters for a text field; null means unbounded.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Choices for <see cref="FormFieldType.Dropdown"/>, <see cref="FormFieldType.ListBox"/> and radio groups.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    public double FontSize { get; init; } = 10;

    /// <summary>Border colour as #RRGGBB; null draws no border.</summary>
    public string? BorderColor { get; init; } = "#64748B";

    /// <summary>Background colour as #RRGGBB; null leaves the widget transparent.</summary>
    public string? BackgroundColor { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new PdfWerkException("Every form field needs a name.");
        if (Name.Contains('.', StringComparison.Ordinal))
            throw new PdfWerkException($"Field name '{Name}' may not contain '.' — that character separates AcroForm hierarchy levels.");
        Rect.Validate();

        var needsOptions = Type is FormFieldType.Dropdown or FormFieldType.ListBox or FormFieldType.RadioGroup;
        if (needsOptions && Options.Count == 0)
            throw new PdfWerkException($"Field '{Name}' is a {Type} and needs at least one option.");
    }
}

/// <summary>A field discovered on an existing document, returned by inspect and by the designer's load step.</summary>
public sealed record ExistingFormField(
    string Name,
    FormFieldType Type,
    FieldRect? Rect,
    string? Value,
    bool ReadOnly,
    IReadOnlyList<string> Options);
