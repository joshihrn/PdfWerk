using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfWerk.Core;
using PdfWerk.Core.Models;

namespace PdfWerk.Pdf.Forms;

/// <summary>
/// Creates and deletes AcroForm fields at the PDF object level.
/// </summary>
/// <remarks>
/// PDFsharp can read and fill an existing form but has no API for authoring one, so the field
/// and widget dictionaries are assembled here against ISO 32000-1 §12.7 directly. Widgets are
/// "merged" — the field dictionary and its single widget annotation are the same object, which
/// is what the spec recommends for a field with one appearance on one page, and what every
/// viewer handles best. Radio groups are the exception: they need a parent plus one kid widget
/// per option.
/// </remarks>
internal static class AcroFormWriter
{
    // Field flag bit positions from ISO 32000-1 table 221, expressed as masks.
    private const int FlagReadOnly = 1 << 0;
    private const int FlagRequired = 1 << 1;
    private const int FlagMultiline = 1 << 12;
    private const int FlagRadio = 1 << 15;
    private const int FlagCombo = 1 << 17;
    private const int FlagMultiSelect = 1 << 21;

    /// <summary>Annotation flag: Print. Without it the field never appears on paper.</summary>
    private const int AnnotationPrint = 1 << 2;

    /// <summary>Returns the document's AcroForm, creating and registering one if absent.</summary>
    public static PdfDictionary EnsureAcroForm(PdfDocument document)
    {
        var catalog = document.Internals.Catalog;
        var acroForm = catalog.Elements.GetDictionary("/AcroForm");

        if (acroForm is null)
        {
            acroForm = new PdfDictionary(document);
            document.Internals.AddObject(acroForm);
            catalog.Elements.SetReference("/AcroForm", acroForm);
        }

        if (acroForm.Elements.GetArray("/Fields") is null)
            acroForm.Elements["/Fields"] = new PdfArray(document);

        EnsureDefaultResources(document, acroForm);
        return acroForm;
    }

    /// <summary>
    /// Guarantees /DR and /DA exist. A field whose /DA names a font missing from /DR renders
    /// as blank text in most viewers, so the two standard faces are always registered.
    /// </summary>
    private static void EnsureDefaultResources(PdfDocument document, PdfDictionary acroForm)
    {
        var resources = acroForm.Elements.GetDictionary("/DR");
        if (resources is null)
        {
            resources = new PdfDictionary(document);
            acroForm.Elements["/DR"] = resources;
        }

        var fonts = resources.Elements.GetDictionary("/Font");
        if (fonts is null)
        {
            fonts = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;
        }

        if (fonts.Elements.GetDictionary("/Helv") is null)
            fonts.Elements["/Helv"] = StandardFont(document, "/Helvetica", withEncoding: true);

        if (fonts.Elements.GetDictionary("/ZaDb") is null)
            fonts.Elements["/ZaDb"] = StandardFont(document, "/ZapfDingbats", withEncoding: false);

        if (!acroForm.Elements.ContainsKey("/DA"))
            acroForm.Elements["/DA"] = new PdfString("/Helv 0 Tf 0 g");

        // Ask viewers to build appearance streams for anything we did not draw ourselves.
        acroForm.Elements.SetBoolean("/NeedAppearances", true);
    }

    private static PdfDictionary StandardFont(PdfDocument document, string baseFont, bool withEncoding)
    {
        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", baseFont);

        // ZapfDingbats is a symbolic face and must keep its built-in encoding.
        if (withEncoding)
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");

        return font;
    }

    /// <summary>Names of every field currently defined, including descendants.</summary>
    public static HashSet<string> ExistingNames(PdfDictionary acroForm)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fields = acroForm.Elements.GetArray("/Fields");
        if (fields is null)
            return names;

        foreach (var item in fields.Elements)
        {
            var dict = Resolve(item);
            var name = dict?.Elements.GetString("/T");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Deletes the named fields and every widget they own.
    /// </summary>
    /// <returns>The names that were actually found and removed.</returns>
    public static IReadOnlyList<string> Remove(PdfDocument document, PdfDictionary acroForm, IReadOnlyList<string> names)
    {
        var fields = acroForm.Elements.GetArray("/Fields");
        if (fields is null || names.Count == 0)
            return [];

        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        var removed = new List<string>();
        var doomedWidgets = new List<PdfDictionary>();

        for (var i = fields.Elements.Count - 1; i >= 0; i--)
        {
            var item = fields.Elements[i];
            var dict = Resolve(item);
            var name = dict?.Elements.GetString("/T");

            if (dict is null || string.IsNullOrEmpty(name) || !wanted.Contains(name))
                continue;

            CollectWidgets(dict, doomedWidgets);
            fields.Elements.RemoveAt(i);
            removed.Add(name);
        }

        if (doomedWidgets.Count > 0)
            DetachWidgets(document, doomedWidgets);

        return removed;
    }

    /// <summary>A merged field is its own widget; a parent field keeps them under /Kids.</summary>
    private static void CollectWidgets(PdfDictionary field, List<PdfDictionary> into)
    {
        var kids = field.Elements.GetArray("/Kids");
        if (kids is null)
        {
            into.Add(field);
            return;
        }

        foreach (var kid in kids.Elements)
        {
            var dict = Resolve(kid);
            if (dict is not null)
                CollectWidgets(dict, into);
        }
    }

    /// <summary>Removes widget annotations from whichever page /Annots array holds them.</summary>
    private static void DetachWidgets(PdfDocument document, List<PdfDictionary> widgets)
    {
        var targets = new HashSet<PdfObjectID>();
        foreach (var widget in widgets)
        {
            if (widget.Reference is not null)
                targets.Add(widget.Reference.ObjectID);
        }

        foreach (var page in document.Pages)
        {
            var annots = page.Elements.GetArray("/Annots");
            if (annots is null)
                continue;

            for (var i = annots.Elements.Count - 1; i >= 0; i--)
            {
                var item = annots.Elements[i];

                var isTarget = item switch
                {
                    PdfReference reference => targets.Contains(reference.ObjectID),
                    // A direct widget can only be matched by identity.
                    PdfDictionary dict => widgets.Any(w => ReferenceEquals(w, dict)),
                    _ => false,
                };

                if (isTarget)
                    annots.Elements.RemoveAt(i);
            }
        }
    }

    /// <summary>Creates a field and attaches its widget(s) to the target page.</summary>
    public static void Add(PdfDocument document, PdfDictionary acroForm, FormFieldSpec spec)
    {
        spec.Validate();
        PdfGuardPage(document, spec.Rect.Page);

        var page = document.Pages[spec.Rect.Page - 1];

        var field = spec.Type == FormFieldType.RadioGroup
            ? BuildRadioGroup(document, page, spec)
            : BuildMergedField(document, page, spec);

        acroForm.Elements.GetArray("/Fields")!.Elements.Add(Ref(field));
    }

    private static void PdfGuardPage(PdfDocument document, int page)
    {
        if (page < 1 || page > document.PageCount)
            throw new PdfWerkException($"Page {page} is out of range; the document has {document.PageCount} page(s).");
    }

    /// <summary>Builds a field whose dictionary doubles as its single widget annotation.</summary>
    private static PdfDictionary BuildMergedField(PdfDocument document, PdfPage page, FormFieldSpec spec)
    {
        var field = new PdfDictionary(document);

        // Registered up front: AttachToPage below stores an indirect reference to this
        // dictionary, which PDFsharp can only produce for an object the document owns.
        document.Internals.AddObject(field);

        field.Elements.SetName("/Type", "/Annot");
        field.Elements.SetName("/Subtype", "/Widget");
        field.Elements["/T"] = Text(spec.Name);
        field.Elements.SetRectangle("/Rect", FieldGeometry.ToPdfRect(page, spec.Rect));
        field.Elements.SetInteger("/F", AnnotationPrint);

        if (!string.IsNullOrWhiteSpace(spec.ToolTip))
            field.Elements["/TU"] = Text(spec.ToolTip);

        ApplyAppearanceCharacteristics(document, field, page, spec);

        var flags = BaseFlags(spec);

        switch (spec.Type)
        {
            case FormFieldType.Text:
                field.Elements.SetName("/FT", "/Tx");
                field.Elements["/DA"] = new PdfString(TextDefaultAppearance(spec));
                if (spec.Multiline) flags |= FlagMultiline;
                if (spec.MaxLength is > 0) field.Elements.SetInteger("/MaxLen", spec.MaxLength.Value);
                if (!string.IsNullOrEmpty(spec.Value))
                {
                    field.Elements["/V"] = Text(spec.Value);
                    field.Elements["/DV"] = Text(spec.Value);
                }

                break;

            case FormFieldType.Checkbox:
                BuildCheckbox(document, field, spec);
                break;

            case FormFieldType.Dropdown:
            case FormFieldType.ListBox:
                field.Elements.SetName("/FT", "/Ch");
                field.Elements["/DA"] = new PdfString(TextDefaultAppearance(spec));
                field.Elements["/Opt"] = OptionArray(document, spec.Options);
                if (spec.Type == FormFieldType.Dropdown) flags |= FlagCombo;
                else flags |= FlagMultiSelect;

                if (!string.IsNullOrEmpty(spec.Value) && spec.Options.Contains(spec.Value, StringComparer.Ordinal))
                {
                    field.Elements["/V"] = Text(spec.Value);
                    field.Elements.SetInteger("/I", spec.Options.ToList().IndexOf(spec.Value));
                }

                break;

            case FormFieldType.Signature:
                field.Elements.SetName("/FT", "/Sig");
                break;

            default:
                throw new PdfWerkException($"Field type {spec.Type} cannot be created as a single widget.");
        }

        field.Elements.SetInteger("/Ff", flags);
        AttachToPage(document, page, field);
        return field;
    }

    private static void BuildCheckbox(PdfDocument document, PdfDictionary field, FormFieldSpec spec)
    {
        var isChecked = string.Equals(spec.Value, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(spec.Value, "on", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(spec.Value, "yes", StringComparison.OrdinalIgnoreCase);

        field.Elements.SetName("/FT", "/Btn");
        field.Elements["/DA"] = new PdfString($"/ZaDb 0 Tf {ColorOperator(spec.BorderColor ?? "#000000", stroke: false)}");
        field.Elements.SetName("/V", isChecked ? "/Yes" : "/Off");
        field.Elements.SetName("/DV", isChecked ? "/Yes" : "/Off");
        field.Elements.SetName("/AS", isChecked ? "/Yes" : "/Off");

        // ZapfDingbats '4' is a check mark; /MK /CA tells viewers which glyph the widget uses.
        var mk = field.Elements.GetDictionary("/MK") ?? new PdfDictionary(document);
        mk.Elements["/CA"] = new PdfString("4");
        field.Elements["/MK"] = mk;

        field.Elements["/AP"] = CheckAppearances(document, spec, glyph: "4");
    }

    private static PdfDictionary BuildRadioGroup(PdfDocument document, PdfPage page, FormFieldSpec spec)
    {
        var parent = new PdfDictionary(document);
        parent.Elements.SetName("/FT", "/Btn");
        parent.Elements["/T"] = Text(spec.Name);
        parent.Elements.SetInteger("/Ff", BaseFlags(spec) | FlagRadio);
        parent.Elements["/DA"] = new PdfString($"/ZaDb 0 Tf {ColorOperator(spec.BorderColor ?? "#000000", stroke: false)}");

        if (!string.IsNullOrWhiteSpace(spec.ToolTip))
            parent.Elements["/TU"] = Text(spec.ToolTip);

        var selected = spec.Value is not null && spec.Options.Contains(spec.Value, StringComparer.Ordinal)
            ? spec.Value
            : null;

        parent.Elements.SetName("/V", selected is null ? "/Off" : "/" + EscapeName(selected));
        document.Internals.AddObject(parent);

        var kids = new PdfArray(document);

        // Options are laid out left to right across the supplied rectangle, each button a
        // square the height of the rectangle. The designer draws the same layout, so what the
        // user positioned is what they get.
        var side = Math.Min(spec.Rect.Height, spec.Rect.Width / Math.Max(spec.Options.Count, 1));
        var step = spec.Options.Count > 1
            ? (spec.Rect.Width - side) / (spec.Options.Count - 1)
            : 0;

        for (var i = 0; i < spec.Options.Count; i++)
        {
            var option = spec.Options[i];
            var onState = "/" + EscapeName(option);

            var kidRect = new FieldRect(
                spec.Rect.Page,
                spec.Rect.X + (step * i),
                spec.Rect.Y,
                side,
                side);

            var kid = new PdfDictionary(document);
            kid.Elements.SetName("/Type", "/Annot");
            kid.Elements.SetName("/Subtype", "/Widget");
            kid.Elements.SetRectangle("/Rect", FieldGeometry.ToPdfRect(page, kidRect));
            kid.Elements.SetInteger("/F", AnnotationPrint);
            kid.Elements.SetName("/AS", option == selected ? onState : "/Off");
            kid.Elements.SetReference("/Parent", parent);

            ApplyAppearanceCharacteristics(document, kid, page, spec with { Rect = kidRect });

            // ZapfDingbats 'l' is a filled circle, the conventional radio marker.
            kid.Elements["/AP"] = CheckAppearances(document, spec with { Rect = kidRect }, glyph: "l", onState: onState);

            document.Internals.AddObject(kid);
            AttachToPage(document, page, kid);
            kids.Elements.Add(Ref(kid));
        }

        parent.Elements["/Kids"] = kids;
        return parent;
    }

    /// <summary>Builds the /AP /N dictionary holding the on and off appearance streams.</summary>
    private static PdfDictionary CheckAppearances(PdfDocument document, FormFieldSpec spec, string glyph, string onState = "/Yes")
    {
        var width = spec.Rect.Width;
        var height = spec.Rect.Height;

        var normal = new PdfDictionary(document);
        normal.Elements[onState] = AppearanceStream(document, width, height, spec, glyph);
        normal.Elements["/Off"] = AppearanceStream(document, width, height, spec, glyph: null);

        var ap = new PdfDictionary(document);
        ap.Elements["/N"] = normal;
        return ap;
    }

    /// <summary>
    /// Builds one form XObject. When /AP is present viewers ignore /MK for rendering, so the
    /// background and border have to be drawn here as well as declared there.
    /// </summary>
    private static PdfItem AppearanceStream(PdfDocument document, double width, double height, FormFieldSpec spec, string? glyph)
    {
        var content = new StringBuilder();
        content.Append("q\n");

        if (spec.BackgroundColor is not null)
            content.Append(CultureInfo.InvariantCulture, $"{ColorOperator(spec.BackgroundColor, stroke: false)} 0 0 {F(width)} {F(height)} re f\n");

        if (spec.BorderColor is not null)
            content.Append(CultureInfo.InvariantCulture, $"{ColorOperator(spec.BorderColor, stroke: true)} 0.5 w 0.25 0.25 {F(width - 0.5)} {F(height - 0.5)} re S\n");

        if (glyph is not null)
        {
            var size = Math.Min(width, height) * 0.72;
            var x = (width - (size * 0.78)) / 2;
            var y = (height - (size * 0.72)) / 2;

            content.Append("BT\n");
            content.Append(CultureInfo.InvariantCulture, $"/ZaDb {F(size)} Tf\n");
            content.Append(CultureInfo.InvariantCulture, $"{ColorOperator(spec.BorderColor ?? "#000000", stroke: false)}\n");
            content.Append(CultureInfo.InvariantCulture, $"{F(x)} {F(y)} Td\n");
            content.Append(CultureInfo.InvariantCulture, $"({glyph}) Tj\n");
            content.Append("ET\n");
        }

        content.Append("Q\n");

        var xobject = new PdfDictionary(document);
        xobject.Elements.SetName("/Type", "/XObject");
        xobject.Elements.SetName("/Subtype", "/Form");
        xobject.Elements["/BBox"] = new PdfArray(document,
            new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height));

        var fonts = new PdfDictionary(document);
        fonts.Elements["/ZaDb"] = StandardFont(document, "/ZapfDingbats", withEncoding: false);

        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;
        xobject.Elements["/Resources"] = resources;

        xobject.CreateStream(Encoding.ASCII.GetBytes(content.ToString()));
        document.Internals.AddObject(xobject);

        return Ref(xobject);
    }

    /// <summary>Declares border and background colours, plus the widget's own rotation.</summary>
    private static void ApplyAppearanceCharacteristics(PdfDocument document, PdfDictionary widget, PdfPage page, FormFieldSpec spec)
    {
        var mk = new PdfDictionary(document);

        if (spec.BorderColor is not null)
            mk.Elements["/BC"] = ColorArray(document, spec.BorderColor);

        if (spec.BackgroundColor is not null)
            mk.Elements["/BG"] = ColorArray(document, spec.BackgroundColor);

        var rotation = FieldGeometry.WidgetRotation(page);
        if (rotation != 0)
            mk.Elements.SetInteger("/R", rotation);

        if (mk.Elements.Count > 0)
            widget.Elements["/MK"] = mk;

        if (spec.BorderColor is not null)
        {
            var border = new PdfDictionary(document);
            border.Elements.SetReal("/W", 0.5);
            border.Elements.SetName("/S", "/S");    // solid
            widget.Elements["/BS"] = border;
        }
    }

    private static void AttachToPage(PdfDocument document, PdfPage page, PdfDictionary widget)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(document);
            page.Elements["/Annots"] = annots;
        }

        widget.Elements.SetReference("/P", page);
        annots.Elements.Add(Ref(widget));
    }

    private static int BaseFlags(FormFieldSpec spec)
    {
        var flags = 0;
        if (spec.ReadOnly) flags |= FlagReadOnly;
        if (spec.Required) flags |= FlagRequired;
        return flags;
    }

    private static string TextDefaultAppearance(FormFieldSpec spec) =>
        // A size of 0 means auto-fit, which is what the designer's "auto" option maps to.
        string.Create(CultureInfo.InvariantCulture, $"/Helv {spec.FontSize:0.##} Tf 0 g");

    private static PdfArray OptionArray(PdfDocument document, IReadOnlyList<string> options)
    {
        var array = new PdfArray(document);
        foreach (var option in options)
            array.Elements.Add(Text(option));

        return array;
    }

    private static PdfArray ColorArray(PdfDocument document, string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return new PdfArray(document, new PdfReal(r), new PdfReal(g), new PdfReal(b));
    }

    /// <summary>Emits an RGB colour-setting operator: "rg" fills, "RG" strokes.</summary>
    private static string ColorOperator(string hex, bool stroke)
    {
        var (r, g, b) = ParseHex(hex);
        return string.Create(CultureInfo.InvariantCulture, $"{r:0.###} {g:0.###} {b:0.###} {(stroke ? "RG" : "rg")}");
    }

    private static (double R, double G, double B) ParseHex(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new PdfWerkException($"'{hex}' is not a valid #RRGGBB colour.");

        return (
            Convert.ToInt32(value[..2], 16) / 255.0,
            Convert.ToInt32(value[2..4], 16) / 255.0,
            Convert.ToInt32(value[4..], 16) / 255.0);
    }

    /// <summary>
    /// Encodes a text string. Names and values that are plain ASCII stay readable in the file;
    /// anything else is written as UTF-16 so accented and non-Latin text survives.
    /// </summary>
    private static PdfString Text(string value) =>
        value.All(char.IsAscii)
            ? new PdfString(value, PdfStringEncoding.PDFDocEncoding)
            : new PdfString(value, PdfStringEncoding.Unicode);

    /// <summary>Escapes a value for use as a PDF name, per the #xx convention.</summary>
    private static string EscapeName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                sb.Append(ch);
            else
                sb.Append(CultureInfo.InvariantCulture, $"#{(int)ch:X2}");
        }

        return sb.Length == 0 ? "Option" : sb.ToString();
    }

    /// <summary>
    /// Indirect reference to an object already added to the document. PDFsharp returns null for
    /// objects it has not registered, which would be a programming error here rather than input.
    /// </summary>
    private static PdfReference Ref(PdfObject obj) =>
        PdfInternals.GetReference(obj)
        ?? throw new InvalidOperationException("Object was not added to the document before being referenced.");

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static PdfDictionary? Resolve(PdfItem item) => item switch
    {
        PdfReference reference => reference.Value as PdfDictionary,
        PdfDictionary dictionary => dictionary,
        _ => null,
    };
}
