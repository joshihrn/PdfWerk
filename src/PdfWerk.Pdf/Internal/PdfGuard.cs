using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfWerk.Core;

namespace PdfWerk.Pdf.Internal;

/// <summary>
/// The single entry point for turning caller-supplied bytes into a PdfDocument.
/// </summary>
/// <remarks>
/// Every operation in this assembly opens untrusted input, so the failure modes are normalised
/// in one place: a malformed or encrypted file must surface as a 4xx <see cref="PdfWerkException"/>
/// and never as an unhandled reader exception that the API would report as a 500.
/// </remarks>
internal static class PdfGuard
{
    /// <summary>A PDF must begin with %PDF- (allowing for the junk some producers prepend).</summary>
    private static readonly byte[] Magic = "%PDF-"u8.ToArray();

    /// <summary>Opens a document for reading or modification, translating reader failures.</summary>
    public static PdfDocument Open(byte[] content, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Modify)
    {
        RequirePdf(content);

        try
        {
            // The stream must outlive the document: PDFsharp reads lazily from it.
            var stream = new MemoryStream(content, writable: false);
            return PdfReader.Open(stream, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidPdfException(
                ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                    ? "This PDF is password protected. Remove the password before uploading it."
                    : $"This file could not be read as a PDF: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException)
        {
            // PDFsharp surfaces structural damage as assorted BCL exceptions rather than its own type.
            throw new InvalidPdfException("This PDF appears to be corrupt and could not be parsed.");
        }
    }

    /// <summary>Cheap shape check before any parsing work is attempted.</summary>
    public static void RequirePdf(byte[] content)
    {
        if (content is null || content.Length == 0)
            throw new InvalidPdfException("The uploaded file was empty.");

        if (content.Length < 32)
            throw new InvalidPdfException("The uploaded file is too small to be a valid PDF.");

        // Some producers emit leading bytes before the header, so scan a short prefix.
        var window = Math.Min(1024, content.Length - Magic.Length);
        for (var i = 0; i <= window; i++)
        {
            if (content.AsSpan(i, Magic.Length).SequenceEqual(Magic))
                return;
        }

        throw new InvalidPdfException("The uploaded file is not a PDF (no %PDF- header found).");
    }

    /// <summary>Serialises a document, leaving the caller's input untouched.</summary>
    public static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>
    /// Reads the page count only. Import mode skips building the writable object graph, which
    /// keeps the guard cheap enough to run before a size check rejects the upload anyway.
    /// </summary>
    public static int CountPages(byte[] content)
    {
        using var document = Open(content, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>Rejects a page reference that falls outside the document.</summary>
    public static void RequirePageInRange(int page, int pageCount)
    {
        if (page < 1 || page > pageCount)
            throw new PdfWerkException($"Page {page} is out of range; the document has {pageCount} page(s).");
    }
}
