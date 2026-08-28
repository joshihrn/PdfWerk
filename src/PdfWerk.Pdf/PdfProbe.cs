using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Cheap structural checks used to reject an upload before any real work is done on it.
/// </summary>
/// <remarks>
/// Page count is the guard that matters most: byte size alone is a poor proxy for cost, because
/// a small file can carry thousands of pages and turn a summarize or flatten request into a very
/// expensive one.
/// </remarks>
public static class PdfProbe
{
    /// <summary>Validates the header and returns the page count.</summary>
    public static int PageCount(byte[] pdf) => PdfGuard.CountPages(pdf);

    /// <summary>Throws a client error if the bytes are not a readable PDF.</summary>
    public static void RequirePdf(byte[] pdf) => PdfGuard.RequirePdf(pdf);
}
