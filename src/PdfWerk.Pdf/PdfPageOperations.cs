using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Fonts;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>Divides a document into page ranges.</summary>
public sealed class PdfSplitter : IPdfSplitter
{
    public IReadOnlyList<SplitPart> Split(byte[] pdf, SplitRequest request, string sourceName)
    {
        using var source = PdfGuard.Open(pdf, PdfDocumentOpenMode.Import);

        var selected = PageRange.Resolve(request.Pages, source.PageCount);
        var stem = Path.GetFileNameWithoutExtension(sourceName);

        return request.Mode switch
        {
            SplitMode.Burst => Burst(source, selected, stem),
            SplitMode.Groups => Groups(source, request.Pages, stem),
            _ => [Build(source, selected, FileNames.Suffixed(sourceName, "pages"))],
        };
    }

    private static List<SplitPart> Burst(PdfDocument source, IReadOnlyList<int> pages, string stem)
    {
        var parts = new List<SplitPart>(pages.Count);

        foreach (var page in pages)
        {
            // Zero-padded so a burst of a 100-page document sorts correctly in a file listing.
            var width = pages.Count.ToString(CultureInfo.InvariantCulture).Length;
            var name = $"{stem}-p{page.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}.pdf";

            parts.Add(Build(source, [page], name));
        }

        return parts;
    }

    /// <summary>One output per comma-separated group, so "1-3,7-9" yields two documents.</summary>
    private static List<SplitPart> Groups(PdfDocument source, string? expression, string stem)
    {
        var groups = (expression ?? "all")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (groups.Length == 0)
            throw new PdfWerkException("Supply at least one page group, e.g. 1-3,7-9.");

        var parts = new List<SplitPart>(groups.Length);

        for (var i = 0; i < groups.Length; i++)
        {
            var pages = PageRange.Resolve(groups[i], source.PageCount);
            parts.Add(Build(source, pages, $"{stem}-{i + 1}.pdf"));
        }

        return parts;
    }

    private static SplitPart Build(PdfDocument source, IReadOnlyList<int> pages, string name)
    {
        using var output = new PdfDocument();

        output.Info.Title = source.Info.Title;
        output.Info.Creator = "PdfWerk";

        foreach (var page in pages)
            output.AddPage(source.Pages[page - 1]);

        return new SplitPart(FileNames.WithExtension(name, ".pdf"), PdfGuard.Save(output), pages);
    }
}

/// <summary>Turns pages by a quarter turn.</summary>
public sealed class PdfRotator : IPdfRotator
{
    public PdfArtifact Rotate(byte[] pdf, RotateRequest request)
    {
        var degrees = Normalise(request.Degrees);

        using var document = PdfGuard.Open(pdf);
        var pages = PageRange.Resolve(request.Pages, document.PageCount);

        foreach (var number in pages)
        {
            var page = document.Pages[number - 1];

            // /Rotate is inherited and cumulative in practice, so adding to the existing value
            // is what "rotate this page" means to a user looking at the rendered result.
            var current = page.Elements.GetInteger("/Rotate");
            var next = request.Absolute ? degrees : current + degrees;

            page.Elements.SetInteger("/Rotate", ((next % 360) + 360) % 360);
        }

        return new PdfArtifact(PdfGuard.Save(document), "rotated.pdf");
    }

    private static int Normalise(int degrees)
    {
        var wrapped = ((degrees % 360) + 360) % 360;

        if (wrapped % 90 != 0)
            throw new PdfWerkException($"Rotation must be a quarter turn (90, 180 or 270), got {degrees}.");

        return wrapped;
    }
}

/// <summary>
/// Stamps text across pages.
/// </summary>
/// <remarks>
/// Drawn as real page content rather than as an annotation, so it survives flattening, printing
/// and any viewer that ignores annotations — which is the point of a watermark.
/// </remarks>
public sealed class PdfWatermarker : IPdfWatermarker
{
    static PdfWatermarker() => FileSystemFontResolver.Install();

    public PdfArtifact Apply(byte[] pdf, WatermarkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new PdfWerkException("Supply the text to stamp.");

        if (request.Text.Length > 200)
            throw new PdfWerkException("Watermark text is limited to 200 characters.");

        if (request.Opacity is < 0 or > 1)
            throw new PdfWerkException("Opacity must be between 0 and 1.");

        var colour = ParseColour(request.Color);

        using var document = PdfGuard.Open(pdf);
        var pages = PageRange.Resolve(request.Pages, document.PageCount);

        foreach (var number in pages)
        {
            var page = document.Pages[number - 1];

            // Prepend draws beneath existing content; Append draws over it.
            var options = request.BehindContent
                ? XGraphicsPdfPageOptions.Prepend
                : XGraphicsPdfPageOptions.Append;

            using var gfx = XGraphics.FromPdfPage(page, options);

            var width = page.Width.Point;
            var height = page.Height.Point;

            var size = request.FontSize ?? FitToPage(gfx, request.Text, width, height, request.Position);
            var font = new XFont("Helvetica", size, XFontStyleEx.Bold);

            var brush = new XSolidBrush(XColor.FromArgb((int)Math.Round(request.Opacity * 255), colour.R, colour.G, colour.B));

            var centre = new XPoint(width / 2, height / 2);

            var angle = request.Position switch
            {
                WatermarkPosition.Diagonal => -Math.Atan2(height, width) * 180 / Math.PI,
                WatermarkPosition.Vertical => -90,
                _ => 0,
            };

            var state = gfx.Save();
            if (angle != 0)
                gfx.RotateAtTransform(angle, centre);

            gfx.DrawString(request.Text, font, brush, centre, XStringFormats.Center);
            gfx.Restore(state);
        }

        return new PdfArtifact(PdfGuard.Save(document), "watermarked.pdf");
    }

    /// <summary>Scales the text so it spans roughly three quarters of the page's long axis.</summary>
    private static double FitToPage(XGraphics gfx, string text, double width, double height, WatermarkPosition position)
    {
        var span = position switch
        {
            WatermarkPosition.Diagonal => Math.Sqrt((width * width) + (height * height)),
            WatermarkPosition.Vertical => height,
            _ => width,
        };

        var probe = new XFont("Helvetica", 100, XFontStyleEx.Bold);
        var measured = gfx.MeasureString(text, probe).Width;

        if (measured <= 0)
            return 48;

        return Math.Clamp(span * 0.75 / measured * 100, 8, 400);
    }

    private static (int R, int G, int B) ParseColour(string hex)
    {
        var value = (hex ?? string.Empty).TrimStart('#');

        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new PdfWerkException($"'{hex}' is not a valid #RRGGBB colour.");

        return (
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value[2..4], 16),
            Convert.ToInt32(value[4..], 16));
    }
}
