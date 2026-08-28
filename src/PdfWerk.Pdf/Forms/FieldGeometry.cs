using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfWerk.Core.Models;

namespace PdfWerk.Pdf.Forms;

/// <summary>
/// Converts between the designer's coordinate space and PDF user space.
/// </summary>
/// <remarks>
/// The browser designer works in <em>visual</em> coordinates: origin at the top-left of the page
/// as rendered, y growing downward. PDF user space puts the origin at the bottom-left of the
/// MediaBox with y growing upward, and a page may additionally carry a /Rotate that the viewer
/// applies before the user ever sees it. Getting a field to land where the user dropped it means
/// undoing all three differences, so the mapping lives here rather than being open-coded.
/// </remarks>
internal static class FieldGeometry
{
    /// <summary>The page as the user sees it, after rotation.</summary>
    internal readonly record struct VisualPage(double Width, double Height, int Rotation);

    /// <summary>Reports the rendered size of a page, which is what the designer overlays.</summary>
    public static VisualPage Describe(PdfPage page)
    {
        var box = page.MediaBox;
        var width = box.Width;
        var height = box.Height;
        var rotation = Normalise(page.Elements.GetInteger("/Rotate"));

        // A quarter turn swaps the rendered dimensions.
        return rotation is 90 or 270
            ? new VisualPage(height, width, rotation)
            : new VisualPage(width, height, rotation);
    }

    /// <summary>
    /// Maps a designer rectangle onto the page's /Rect, in PDF user space.
    /// </summary>
    public static PdfRectangle ToPdfRect(PdfPage page, FieldRect rect)
    {
        var box = page.MediaBox;
        var rotation = Normalise(page.Elements.GetInteger("/Rotate"));

        // Unrotated page extents, which is the space /Rect is expressed in.
        var pageWidth = box.Width;
        var pageHeight = box.Height;

        double u1, u2, v1, v2;

        switch (rotation)
        {
            case 90:
                u1 = rect.Y;
                u2 = rect.Y + rect.Height;
                v1 = rect.X;
                v2 = rect.X + rect.Width;
                break;

            case 180:
                u1 = pageWidth - (rect.X + rect.Width);
                u2 = pageWidth - rect.X;
                v1 = rect.Y;
                v2 = rect.Y + rect.Height;
                break;

            case 270:
                u1 = pageWidth - (rect.Y + rect.Height);
                u2 = pageWidth - rect.Y;
                v1 = pageHeight - (rect.X + rect.Width);
                v2 = pageHeight - rect.X;
                break;

            default:
                u1 = rect.X;
                u2 = rect.X + rect.Width;
                v1 = pageHeight - (rect.Y + rect.Height);
                v2 = pageHeight - rect.Y;
                break;
        }

        // The MediaBox need not start at the origin, so shift by its lower-left corner.
        var x1 = box.X1 + u1;
        var x2 = box.X1 + u2;
        var y1 = box.Y1 + v1;
        var y2 = box.Y1 + v2;

        // Clamp into the page: a widget outside the MediaBox is invisible and confusing.
        x1 = Math.Clamp(x1, box.X1, box.X2);
        x2 = Math.Clamp(x2, box.X1, box.X2);
        y1 = Math.Clamp(y1, box.Y1, box.Y2);
        y2 = Math.Clamp(y2, box.Y1, box.Y2);

        return new PdfRectangle(new XRect(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Abs(x2 - x1),
            Math.Abs(y2 - y1)));
    }

    /// <summary>Maps a widget's /Rect back into designer coordinates, for loading an existing form.</summary>
    public static FieldRect ToFieldRect(PdfPage page, PdfRectangle widget, int pageNumber)
    {
        var box = page.MediaBox;
        var rotation = Normalise(page.Elements.GetInteger("/Rotate"));

        var pageWidth = box.Width;
        var pageHeight = box.Height;

        var u1 = Math.Min(widget.X1, widget.X2) - box.X1;
        var u2 = Math.Max(widget.X1, widget.X2) - box.X1;
        var v1 = Math.Min(widget.Y1, widget.Y2) - box.Y1;
        var v2 = Math.Max(widget.Y1, widget.Y2) - box.Y1;

        double x, y, w, h;

        switch (rotation)
        {
            case 90:
                x = v1;
                y = u1;
                w = v2 - v1;
                h = u2 - u1;
                break;

            case 180:
                x = pageWidth - u2;
                y = v1;
                w = u2 - u1;
                h = v2 - v1;
                break;

            case 270:
                x = pageHeight - v2;
                y = pageWidth - u2;
                w = v2 - v1;
                h = u2 - u1;
                break;

            default:
                x = u1;
                y = pageHeight - v2;
                w = u2 - u1;
                h = v2 - v1;
                break;
        }

        return new FieldRect(pageNumber, Math.Round(x, 2), Math.Round(y, 2), Math.Round(w, 2), Math.Round(h, 2));
    }

    /// <summary>
    /// Rotation to apply to the widget's own contents via /MK /R, so that field text reads
    /// upright on a rotated page. /MK /R is counter-clockwise, and cancels the page's /Rotate.
    /// </summary>
    public static int WidgetRotation(PdfPage page)
    {
        var rotation = Normalise(page.Elements.GetInteger("/Rotate"));
        return rotation == 0 ? 0 : 360 - rotation;
    }

    private static int Normalise(int rotate)
    {
        var r = rotate % 360;
        if (r < 0)
            r += 360;

        // /Rotate is defined only in quarter turns; anything else is rounded to the nearest.
        return (int)(Math.Round(r / 90.0) * 90) % 360;
    }
}
