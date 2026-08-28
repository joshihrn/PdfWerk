using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Fonts;
using PdfWerk.Pdf.Forms;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Reads, authors and fills AcroForms: the add/remove designer round-trip, value merging, and
/// flattening.
/// </summary>
public sealed class PdfFormService : IPdfFormService
{
    static PdfFormService() => FileSystemFontResolver.Install();

    public IReadOnlyList<ExistingFormField> ReadFields(byte[] pdf)
    {
        using var document = PdfGuard.Open(pdf);
        return AcroFormIndex.Describe(document);
    }

    public PdfArtifact EditFields(byte[] pdf, EditFormFieldsRequest request)
    {
        if (request.Add.Count == 0 && request.Remove.Count == 0)
            throw new PdfWerkException("Specify at least one field to add or remove.");

        using var document = PdfGuard.Open(pdf);
        var acroForm = AcroFormWriter.EnsureAcroForm(document);

        // Removals run first so that a replace is simply a remove followed by an add.
        var toRemove = request.Remove.ToList();

        if (request.Replace)
        {
            var existing = AcroFormWriter.ExistingNames(acroForm);
            toRemove.AddRange(request.Add.Select(a => a.Name).Where(existing.Contains));
        }

        if (toRemove.Count > 0)
            AcroFormWriter.Remove(document, acroForm, toRemove);

        var present = AcroFormWriter.ExistingNames(acroForm);
        var added = new HashSet<string>(StringComparer.Ordinal);

        foreach (var spec in request.Add)
        {
            if (!added.Add(spec.Name))
                throw new PdfWerkException($"Field '{spec.Name}' appears twice in the same request.");

            if (present.Contains(spec.Name))
                throw new PdfWerkException(
                    $"A field named '{spec.Name}' already exists. Set replace=true to overwrite it.");

            AcroFormWriter.Add(document, acroForm, spec);
        }

        return new PdfArtifact(PdfGuard.Save(document), "form.pdf");
    }

    public PdfArtifact FillFields(byte[] pdf, FillFormRequest request)
    {
        using var document = PdfGuard.Open(pdf);

        var index = AcroFormIndex.Build(document);
        if (index.Count == 0)
            throw new PdfWerkException("This PDF has no form fields to fill.");

        var byName = index.ToDictionary(f => f.Name, StringComparer.Ordinal);

        if (request.StrictFieldNames)
        {
            var unknown = request.Values.Keys.Where(k => !byName.ContainsKey(k)).ToList();
            if (unknown.Count > 0)
            {
                throw new PdfWerkException(
                    $"This form has no field(s) named: {string.Join(", ", unknown)}. " +
                    $"Available fields: {string.Join(", ", byName.Keys.Take(25))}.");
            }
        }

        foreach (var (name, value) in request.Values)
        {
            if (byName.TryGetValue(name, out var field))
                SetValue(field, value);
        }

        if (request.Flatten)
            Flatten(document, index);
        else
            RequestAppearanceRegeneration(document);

        return new PdfArtifact(PdfGuard.Save(document), "filled.pdf");
    }

    // ---- value writing ---------------------------------------------------

    private static void SetValue(IndexedField field, string value)
    {
        switch (field.Type)
        {
            case FormFieldType.Checkbox:
                SetCheckbox(field, value);
                break;

            case FormFieldType.RadioGroup:
                SetRadio(field, value);
                break;

            case FormFieldType.Signature:
                throw new PdfWerkException($"Field '{field.Name}' is a signature field and cannot be filled with a value.");

            default:
                field.Field.Elements["/V"] = PdfText(value);
                SyncChoiceIndex(field, value);
                break;
        }
    }

    /// <summary>
    /// Encodes a value as a PDF text string, keeping plain ASCII readable in the file and
    /// falling back to UTF-16 so accented and non-Latin input survives a round trip.
    /// </summary>
    private static PdfString PdfText(string value) =>
        value.All(char.IsAscii)
            ? new PdfString(value, PdfStringEncoding.PDFDocEncoding)
            : new PdfString(value, PdfStringEncoding.Unicode);

    private static void SetCheckbox(IndexedField field, string value)
    {
        var on = value.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1" or "checked";

        // The "on" state name is whatever the widget's /AP /N dictionary calls it — very often
        // /Yes, but by no means always, so it is read back rather than assumed.
        var stateName = on ? DiscoverOnState(field) : "/Off";

        field.Field.Elements.SetName("/V", stateName);

        foreach (var widget in field.Widgets)
            widget.Elements.SetName("/AS", stateName);
    }

    private static void SetRadio(IndexedField field, string value)
    {
        var target = "/" + value.Trim().TrimStart('/');
        var matched = false;

        foreach (var widget in field.Widgets)
        {
            var states = OnStates(widget).ToList();
            var hit = states.FirstOrDefault(s => string.Equals(s, target, StringComparison.OrdinalIgnoreCase));

            if (hit is not null)
            {
                widget.Elements.SetName("/AS", hit);
                matched = true;
            }
            else
            {
                widget.Elements.SetName("/AS", "/Off");
            }
        }

        if (!matched)
        {
            var available = field.Widgets.SelectMany(OnStates).Select(s => s.TrimStart('/')).Distinct();
            throw new PdfWerkException(
                $"'{value}' is not an option for radio group '{field.Name}'. Options: {string.Join(", ", available)}.");
        }

        field.Field.Elements.SetName("/V", target);
    }

    private static string DiscoverOnState(IndexedField field)
    {
        foreach (var widget in field.Widgets)
        {
            var state = OnStates(widget).FirstOrDefault();
            if (state is not null)
                return state;
        }

        return "/Yes";
    }

    /// <summary>Every appearance state of a widget other than /Off.</summary>
    private static IEnumerable<string> OnStates(PdfDictionary widget)
    {
        var normal = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (normal is null)
            yield break;

        foreach (var key in normal.Elements.Keys)
        {
            if (!string.Equals(key, "/Off", StringComparison.OrdinalIgnoreCase))
                yield return key;
        }
    }

    /// <summary>Keeps /I in step with /V so list boxes highlight the right row.</summary>
    private static void SyncChoiceIndex(IndexedField field, string value)
    {
        if (field.Type is not (FormFieldType.Dropdown or FormFieldType.ListBox))
            return;

        var options = field.Field.Elements.GetArray("/Opt");
        if (options is null)
            return;

        for (var i = 0; i < options.Elements.Count; i++)
        {
            var entry = options.Elements[i];
            var text = entry switch
            {
                PdfString s => s.Value,
                PdfArray pair when pair.Elements.Count > 0 => (pair.Elements[0] as PdfString)?.Value,
                _ => null,
            };

            if (string.Equals(text, value, StringComparison.Ordinal))
            {
                field.Field.Elements.SetInteger("/I", i);
                return;
            }
        }
    }

    /// <summary>
    /// Sets /NeedAppearances, which tells the viewer to build appearance streams for the values
    /// just written. Without it, filled text is present in the file but invisible on screen.
    /// </summary>
    private static void RequestAppearanceRegeneration(PdfDocument document)
    {
        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        acroForm?.Elements.SetBoolean("/NeedAppearances", true);
    }

    // ---- flattening ------------------------------------------------------

    /// <summary>
    /// Bakes field values into page content and strips the form.
    /// </summary>
    /// <remarks>
    /// A flattened document must render identically without a form-aware viewer, so values are
    /// drawn rather than relying on /NeedAppearances. Widgets are painted in unrotated page
    /// space; on a page carrying /Rotate the drawing is counter-rotated first so the text is
    /// upright once the viewer applies the page rotation.
    /// </remarks>
    private static void Flatten(PdfDocument document, IReadOnlyList<IndexedField> fields)
    {
        var widgetsByPage = new Dictionary<int, List<(IndexedField Field, PdfDictionary Widget)>>();
        var pageLookup = BuildWidgetPageLookup(document);

        foreach (var field in fields)
        {
            foreach (var widget in field.Widgets)
            {
                if (widget.Reference is null || !pageLookup.TryGetValue(widget.Reference.ObjectID, out var pageNumber))
                    continue;

                if (!widgetsByPage.TryGetValue(pageNumber, out var list))
                    widgetsByPage[pageNumber] = list = [];

                list.Add((field, widget));
            }
        }

        foreach (var (pageNumber, widgets) in widgetsByPage)
        {
            var page = document.Pages[pageNumber - 1];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var rotation = page.Elements.GetInteger("/Rotate") % 360;
            if (rotation != 0)
                gfx.RotateAtTransform(-rotation, new XPoint(page.Width.Point / 2, page.Height.Point / 2));

            foreach (var (field, widget) in widgets)
                DrawWidget(gfx, page, field, widget);
        }

        // Drop the interactive layer entirely.
        foreach (var page in document.Pages)
        {
            var annots = page.Elements.GetArray("/Annots");
            if (annots is null)
                continue;

            for (var i = annots.Elements.Count - 1; i >= 0; i--)
            {
                var dict = AcroFormWriter.Resolve(annots.Elements[i]);
                if (dict?.Elements.GetName("/Subtype") == "/Widget")
                    annots.Elements.RemoveAt(i);
            }

            if (annots.Elements.Count == 0)
                page.Elements.Remove("/Annots");
        }

        document.Internals.Catalog.Elements.Remove("/AcroForm");
    }

    private static Dictionary<PdfObjectID, int> BuildWidgetPageLookup(PdfDocument document)
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

    private static void DrawWidget(XGraphics gfx, PdfPage page, IndexedField field, PdfDictionary widget)
    {
        var rect = widget.Elements.GetRectangle("/Rect");
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var box = page.MediaBox;

        // XGraphics uses a top-left origin over the unrotated page, which is the same space
        // /Rect is expressed in once the MediaBox offset is removed.
        var target = new XRect(
            Math.Min(rect.X1, rect.X2) - box.X1,
            box.Y2 - Math.Max(rect.Y1, rect.Y2),
            rect.Width,
            rect.Height);

        switch (field.Type)
        {
            case FormFieldType.Checkbox:
            case FormFieldType.RadioGroup:
                DrawTick(gfx, target, IsWidgetOn(widget));
                break;

            case FormFieldType.Signature:
                break;

            default:
                DrawText(gfx, target, ValueOf(field), FontSizeOf(field, widget, target));
                break;
        }
    }

    private static bool IsWidgetOn(PdfDictionary widget)
    {
        var state = widget.Elements.GetName("/AS");
        return !string.IsNullOrEmpty(state) && !string.Equals(state, "/Off", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValueOf(IndexedField field) =>
        field.Field.Elements.GetValue("/V") switch
        {
            PdfString s => s.Value,
            PdfName n => n.Value.TrimStart('/') == "Off" ? string.Empty : n.Value.TrimStart('/'),
            PdfInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty,
        };

    /// <summary>
    /// Reads the point size out of the field's /DA string. A size of 0 means auto-fit, which is
    /// approximated by sizing to the widget height.
    /// </summary>
    private static double FontSizeOf(IndexedField field, PdfDictionary widget, XRect target)
    {
        var da = widget.Elements.GetString("/DA");
        if (string.IsNullOrEmpty(da))
            da = field.Field.Elements.GetString("/DA");

        var size = 0.0;

        if (!string.IsNullOrEmpty(da))
        {
            var tokens = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tf = Array.IndexOf(tokens, "Tf");
            if (tf > 0)
                double.TryParse(tokens[tf - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out size);
        }

        if (size <= 0)
            size = Math.Clamp(target.Height * 0.62, 6, 14);

        return size;
    }

    private static void DrawText(XGraphics gfx, XRect target, string value, double fontSize)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var font = new XFont("Helvetica", fontSize, XFontStyleEx.Regular);

        // Inset matches the padding viewers apply inside a field box.
        var inner = new XRect(target.X + 2, target.Y + 1, Math.Max(target.Width - 4, 1), Math.Max(target.Height - 2, 1));

        var state = gfx.Save();
        gfx.IntersectClip(inner);

        var isMultiline = value.Contains('\n', StringComparison.Ordinal);

        if (isMultiline)
        {
            var y = inner.Y;
            var lineHeight = fontSize * 1.2;

            foreach (var line in value.Split('\n'))
            {
                if (y > inner.Bottom)
                    break;

                gfx.DrawString(line, font, XBrushes.Black,
                    new XRect(inner.X, y, inner.Width, lineHeight), XStringFormats.CenterLeft);
                y += lineHeight;
            }
        }
        else
        {
            gfx.DrawString(value, font, XBrushes.Black, inner, XStringFormats.CenterLeft);
        }

        gfx.Restore(state);
    }

    private static void DrawTick(XGraphics gfx, XRect target, bool isOn)
    {
        if (!isOn)
            return;

        var pen = new XPen(XColors.Black, Math.Max(target.Height * 0.09, 0.8));

        // A two-stroke check mark, inset so it sits inside the widget's border.
        var x = target.X + (target.Width * 0.22);
        var y = target.Y + (target.Height * 0.52);
        var midX = target.X + (target.Width * 0.42);
        var midY = target.Y + (target.Height * 0.74);
        var endX = target.X + (target.Width * 0.78);
        var endY = target.Y + (target.Height * 0.26);

        gfx.DrawLine(pen, x, y, midX, midY);
        gfx.DrawLine(pen, midX, midY, endX, endY);
    }
}
