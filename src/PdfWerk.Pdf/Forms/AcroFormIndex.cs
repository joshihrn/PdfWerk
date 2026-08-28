using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfWerk.Core.Models;

namespace PdfWerk.Pdf.Forms;

/// <summary>One addressable field, flattened out of the AcroForm's tree structure.</summary>
/// <param name="Name">Fully qualified name: ancestor /T values joined with '.', per ISO 32000-1 §12.7.3.2.</param>
/// <param name="Field">The dictionary that owns /V — the one a fill operation writes to.</param>
/// <param name="Widgets">Every widget annotation that renders this field.</param>
internal sealed record IndexedField(
    string Name,
    PdfDictionary Field,
    IReadOnlyList<PdfDictionary> Widgets,
    FormFieldType Type,
    int Flags);

/// <summary>
/// Walks an AcroForm into a flat, name-addressable list.
/// </summary>
/// <remarks>
/// Real-world forms nest fields arbitrarily and let children inherit /FT and /Ff from ancestors,
/// so neither the type nor the flags of a field can be read from its own dictionary alone. The
/// walk therefore carries inherited attributes down as it descends.
/// </remarks>
internal static class AcroFormIndex
{
    private const int FlagRadio = 1 << 15;
    private const int FlagPushButton = 1 << 16;
    private const int FlagCombo = 1 << 17;

    public static List<IndexedField> Build(PdfDocument document)
    {
        var results = new List<IndexedField>();

        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        var fields = acroForm?.Elements.GetArray("/Fields");
        if (fields is null)
            return results;

        foreach (var item in fields.Elements)
        {
            var dict = AcroFormWriter.Resolve(item);
            if (dict is not null)
                Walk(dict, prefix: null, inheritedType: null, inheritedFlags: 0, results, depth: 0);
        }

        return results;
    }

    private static void Walk(
        PdfDictionary node,
        string? prefix,
        string? inheritedType,
        int inheritedFlags,
        List<IndexedField> results,
        int depth)
    {
        // Malformed files can contain reference cycles; bound the descent rather than hang.
        if (depth > 32)
            return;

        var partial = node.Elements.GetString("/T");
        var name = string.IsNullOrEmpty(partial)
            ? prefix
            : string.IsNullOrEmpty(prefix) ? partial : $"{prefix}.{partial}";

        var fieldType = node.Elements.GetName("/FT") is { Length: > 0 } ft ? ft : inheritedType;

        var flags = node.Elements.ContainsKey("/Ff")
            ? node.Elements.GetInteger("/Ff")
            : inheritedFlags;

        var kids = node.Elements.GetArray("/Kids");

        // Kids that carry their own /T are separate fields; kids without one are just extra
        // widgets for this same field, e.g. a field repeated on every page.
        var childFields = new List<PdfDictionary>();
        var widgets = new List<PdfDictionary>();

        if (kids is not null)
        {
            foreach (var kid in kids.Elements)
            {
                var dict = AcroFormWriter.Resolve(kid);
                if (dict is null)
                    continue;

                if (string.IsNullOrEmpty(dict.Elements.GetString("/T")))
                    widgets.Add(dict);
                else
                    childFields.Add(dict);
            }
        }
        else if (IsWidget(node))
        {
            // Merged field: the dictionary is both the field and its only widget.
            widgets.Add(node);
        }

        if (fieldType is not null && !string.IsNullOrEmpty(name) && childFields.Count == 0)
        {
            results.Add(new IndexedField(
                name,
                node,
                widgets,
                MapType(fieldType, flags),
                flags));
        }

        foreach (var child in childFields)
            Walk(child, name, fieldType, flags, results, depth + 1);
    }

    private static bool IsWidget(PdfDictionary node) =>
        node.Elements.GetName("/Subtype") == "/Widget" || node.Elements.ContainsKey("/Rect");

    private static FormFieldType MapType(string fieldType, int flags) => fieldType switch
    {
        "/Tx" => FormFieldType.Text,
        "/Sig" => FormFieldType.Signature,
        "/Btn" when (flags & FlagRadio) != 0 => FormFieldType.RadioGroup,
        "/Btn" when (flags & FlagPushButton) != 0 => FormFieldType.Checkbox,
        "/Btn" => FormFieldType.Checkbox,
        "/Ch" when (flags & FlagCombo) != 0 => FormFieldType.Dropdown,
        "/Ch" => FormFieldType.ListBox,
        _ => FormFieldType.Text,
    };

    /// <summary>Converts the index into the public shape returned by inspect and the designer.</summary>
    public static IReadOnlyList<ExistingFormField> Describe(PdfDocument document)
    {
        var pageIndex = BuildPageLookup(document);

        return Build(document)
            .Select(f => new ExistingFormField(
                f.Name,
                f.Type,
                LocateRect(f, pageIndex, document),
                ReadValue(f),
                (f.Flags & 1) != 0,
                ReadOptions(f.Field)))
            .ToList();
    }

    /// <summary>Maps each widget's object id back to the page it lives on.</summary>
    private static Dictionary<PdfObjectID, int> BuildPageLookup(PdfDocument document)
    {
        var lookup = new Dictionary<PdfObjectID, int>();

        for (var i = 0; i < document.PageCount; i++)
        {
            var annots = document.Pages[i].Elements.GetArray("/Annots");
            if (annots is null)
                continue;

            foreach (var item in annots.Elements)
            {
                if (item is PdfReference reference)
                    lookup[reference.ObjectID] = i + 1;
            }
        }

        return lookup;
    }

    private static FieldRect? LocateRect(IndexedField field, Dictionary<PdfObjectID, int> pageLookup, PdfDocument document)
    {
        // A field with several widgets is reported at its first appearance.
        foreach (var widget in field.Widgets)
        {
            var rect = widget.Elements.GetRectangle("/Rect");
            if (rect.Width == 0 && rect.Height == 0)
                continue;

            var pageNumber = widget.Reference is not null && pageLookup.TryGetValue(widget.Reference.ObjectID, out var found)
                ? found
                : PageFromParentLink(widget, document);

            if (pageNumber is null)
                continue;

            return FieldGeometry.ToFieldRect(document.Pages[pageNumber.Value - 1], rect, pageNumber.Value);
        }

        return null;
    }

    /// <summary>Falls back to the widget's /P back-pointer when the page /Annots scan missed it.</summary>
    private static int? PageFromParentLink(PdfDictionary widget, PdfDocument document)
    {
        var pageRef = widget.Elements.GetReference("/P");
        if (pageRef is null)
            return null;

        for (var i = 0; i < document.PageCount; i++)
        {
            if (document.Pages[i].Reference?.ObjectID == pageRef.ObjectID)
                return i + 1;
        }

        return null;
    }

    private static string? ReadValue(IndexedField field)
    {
        var value = field.Field.Elements.GetValue("/V");

        return value switch
        {
            PdfString s => s.Value,
            PdfName n => n.Value.TrimStart('/') is "Off" or "" ? null : n.Value.TrimStart('/'),
            PdfInteger i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PdfArray a when a.Elements.Count > 0 => (a.Elements[0] as PdfString)?.Value,
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadOptions(PdfDictionary field)
    {
        var options = field.Elements.GetArray("/Opt");
        if (options is null)
            return [];

        var results = new List<string>(options.Elements.Count);

        foreach (var item in options.Elements)
        {
            switch (item)
            {
                case PdfString s:
                    results.Add(s.Value);
                    break;

                // An /Opt entry may be [exportValue displayValue]; the display value is shown.
                case PdfArray pair when pair.Elements.Count > 0:
                    var display = pair.Elements.Count > 1 ? pair.Elements[1] : pair.Elements[0];
                    if (display is PdfString ps)
                        results.Add(ps.Value);
                    break;
            }
        }

        return results;
    }
}
