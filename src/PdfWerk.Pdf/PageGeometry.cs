using MigraDoc.DocumentObjectModel;

namespace PdfWerk.Pdf;

/// <summary>
/// Resolves a section's page dimensions.
/// </summary>
/// <remarks>
/// <para>
/// Setting <see cref="PageSetup.PageFormat"/> does not populate
/// <see cref="PageSetup.PageWidth"/> — the format is a name that MigraDoc resolves during
/// rendering, and until then the width reads as zero. Anything that needs the width while the
/// document is still being built has to work it out for itself.
/// </para>
/// <para>
/// This was not theoretical. Table columns were sized as
/// <c>(PageWidth - LeftMargin - RightMargin) / columns</c>, which with a zero width came out as
/// minus half the margins. Columns were laid out with negative widths, running leftwards off the
/// page, and a two-column table rendered as a narrow strip at the left edge with its second
/// column outside the paper entirely.
/// </para>
/// <para>
/// The sizes are the ISO and US definitions, in points at 72 per inch. They are stated here
/// rather than derived so the numbers can be checked against the standards by eye.
/// </para>
/// </remarks>
internal static class PageGeometry
{
    private static (double Width, double Height) PortraitSize(PageFormat format) => format switch
    {
        PageFormat.A0 => (2383.94, 3370.39),
        PageFormat.A1 => (1683.78, 2383.94),
        PageFormat.A2 => (1190.55, 1683.78),
        PageFormat.A3 => (841.89, 1190.55),
        PageFormat.A4 => (595.28, 841.89),
        PageFormat.A5 => (419.53, 595.28),
        PageFormat.A6 => (297.64, 419.53),
        PageFormat.B5 => (498.90, 708.66),
        PageFormat.Letter => (612.00, 792.00),
        PageFormat.Legal => (612.00, 1008.00),
        PageFormat.Ledger => (1224.00, 792.00),
        PageFormat.P11x17 => (792.00, 1224.00),
        _ => (595.28, 841.89),
    };

    /// <summary>
    /// Writes explicit page dimensions onto the setup, honouring its orientation.
    /// </summary>
    /// <remarks>
    /// Called once, immediately after the format and orientation are chosen, so every later
    /// reader of <see cref="PageSetup.PageWidth"/> gets a real number.
    /// </remarks>
    public static void ApplyExplicitSize(PageSetup setup)
    {
        var (width, height) = PortraitSize(setup.PageFormat);

        if (setup.Orientation == Orientation.Landscape)
            (width, height) = (height, width);

        setup.PageWidth = Unit.FromPoint(width);
        setup.PageHeight = Unit.FromPoint(height);
    }

    /// <summary>
    /// The printable width between the margins.
    /// </summary>
    /// <remarks>
    /// Falls back to the format's own width when the setup carries none, so a caller that never
    /// went through <see cref="ApplyExplicitSize"/> still gets a sane number rather than a
    /// negative one. A width of zero here is always a bug, never a legitimate page.
    /// </remarks>
    public static double ContentWidth(PageSetup setup)
    {
        var width = setup.PageWidth.Point;

        if (width <= 0)
        {
            var (fallback, _) = PortraitSize(setup.PageFormat);
            width = setup.Orientation == Orientation.Landscape
                ? PortraitSize(setup.PageFormat).Height
                : fallback;
        }

        var usable = width - setup.LeftMargin.Point - setup.RightMargin.Point;

        // Margins wider than the paper are a caller error, but returning a negative width would
        // put content off the page rather than merely make it cramped.
        return usable > 1 ? usable : width;
    }
}
