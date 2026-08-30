using MigraDoc.DocumentObjectModel;
using PdfSharp.Drawing;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Fonts;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Draws new text and shapes onto an existing page.
/// </summary>
/// <remarks>
/// The gap this fills: editing was find-and-replace only, so text could be changed or removed but
/// never introduced. There was no way to write into blank space — to sign a line, fill a gap in a
/// scanned form, or add a note in a margin.
///
/// This draws into the page's content stream rather than adding annotation objects. The result is
/// part of the document: it prints, it survives flattening, and it cannot be toggled off in a
/// viewer's comment panel. That also means it cannot be edited afterwards, which is the honest
/// trade for text that behaves like the rest of the page.
/// </remarks>
public sealed class PdfAnnotator : IPdfAnnotator
{
    static PdfAnnotator() => FileSystemFontResolver.Install();

    /// <summary>Generous, but bounded: this ends up in a content stream we have to hold in memory.</summary>
    private const int MaxItems = 200;

    private const int MaxTextLength = 2_000;

    public PdfArtifact Annotate(byte[] pdf, AnnotateRequest request)
    {
        if (request.Items.Count == 0)
            throw new PdfWerkException("Supply at least one thing to draw.");

        if (request.Items.Count > MaxItems)
            throw new PdfWerkException($"Up to {MaxItems} items can be drawn in one request.");

        using var document = PdfGuard.Open(pdf);

        foreach (var item in request.Items)
            Validate(item, document.PageCount);

        // Grouped by page so each page's content stream is opened once. Opening it per item
        // appends a fresh stream every time, which is valid but bloats the file quickly.
        foreach (var group in request.Items.GroupBy(i => i.Page))
        {
            var page = document.Pages[group.Key - 1];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            // The rest of the codebase places things from the top-left, and so does the
            // designer's canvas, so the same convention is used here rather than PDF's
            // bottom-left origin. XGraphics already works this way.
            foreach (var item in group)
                Draw(gfx, item);
        }

        return new PdfArtifact(PdfGuard.Save(document), "annotated.pdf");
    }

    private static void Validate(AnnotateItem item, int pageCount)
    {
        if (item.Page < 1 || item.Page > pageCount)
            throw new PdfWerkException($"Page {item.Page} is out of range; the document has {pageCount} page(s).");

        if (item.Width < 0 || item.Height < 0)
            throw new PdfWerkException("Width and height cannot be negative.");

        switch (item.Type)
        {
            case AnnotateItemType.Text when string.IsNullOrEmpty(item.Text):
                throw new PdfWerkException("A text item needs some text to draw.");

            case AnnotateItemType.Text when item.Text!.Length > MaxTextLength:
                throw new PdfWerkException($"Text is limited to {MaxTextLength:N0} characters per item.");

            case AnnotateItemType.Line when item.Width == 0 && item.Height == 0:
                throw new PdfWerkException("A line needs a width or a height.");

            case AnnotateItemType.Rectangle when item.Width == 0 || item.Height == 0:
                throw new PdfWerkException("A rectangle needs both a width and a height.");

            default:
                break;
        }

        if (item.FontSize is <= 0 or > 400)
            throw new PdfWerkException("Font size must be between 0 and 400 points.");

        if (item.Opacity is < 0 or > 1)
            throw new PdfWerkException("Opacity must be between 0 and 1.");
    }

    private static void Draw(XGraphics gfx, AnnotateItem item)
    {
        var colour = ParseColour(item.Color);
        var alpha = (int)Math.Round(item.Opacity * 255);
        var brush = new XSolidBrush(XColor.FromArgb(alpha, colour.R, colour.G, colour.B));

        switch (item.Type)
        {
            case AnnotateItemType.Rectangle:
            {
                var rect = new XRect(item.X, item.Y, item.Width, item.Height);

                if (item.Filled)
                    gfx.DrawRectangle(brush, rect);
                else
                    gfx.DrawRectangle(new XPen(brush.Color, item.LineWidth), rect);

                break;
            }

            case AnnotateItemType.Line:
                gfx.DrawLine(
                    new XPen(brush.Color, item.LineWidth),
                    new XPoint(item.X, item.Y),
                    new XPoint(item.X + item.Width, item.Y + item.Height));
                break;

            default:
                DrawText(gfx, item, brush);
                break;
        }
    }

    private static void DrawText(XGraphics gfx, AnnotateItem item, XBrush brush)
    {
        var style = (item.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular)
                    | (item.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);

        var font = new XFont(string.IsNullOrWhiteSpace(item.FontFamily) ? "Helvetica" : item.FontFamily,
            item.FontSize, style);

        // Without a width there is nothing to wrap against, so the text is drawn as one line from
        // the given point. That is the common case: a name on a signature line.
        if (item.Width <= 0)
        {
            gfx.DrawString(item.Text!, font, brush, new XPoint(item.X, item.Y + item.FontSize), XStringFormats.TopLeft);
            return;
        }

        var y = item.Y;
        var leading = item.FontSize * 1.2;

        // A height of zero means "as far as it takes"; anything else clips, so a note cannot
        // silently run down over the rest of the page.
        var limit = item.Height > 0 ? item.Y + item.Height : double.MaxValue;

        foreach (var line in Wrap(gfx, item.Text!, font, item.Width))
        {
            if (y + leading > limit)
                break;

            gfx.DrawString(line, font, brush, new XPoint(item.X, y + item.FontSize), XStringFormats.TopLeft);
            y += leading;
        }
    }

    /// <summary>
    /// Breaks text to a width, keeping explicit line breaks.
    /// </summary>
    /// <remarks>
    /// Measured with the real font rather than by character count, because a proportional face
    /// makes "lllll" and "WWWWW" very different widths. A single word longer than the box is
    /// emitted whole and overflows: breaking mid-word without a hyphenation dictionary produces
    /// worse output than a slightly wide line.
    /// </remarks>
    private static IEnumerable<string> Wrap(XGraphics gfx, string text, XFont font, double width)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            var line = new System.Text.StringBuilder();

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";

                if (line.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
                {
                    yield return line.ToString();
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }

    private static (int R, int G, int B) ParseColour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (0, 0, 0);

        var hex = value.TrimStart('#');

        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            throw new PdfWerkException($"'{value}' is not a colour. Use #RRGGBB.");
        }

        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
