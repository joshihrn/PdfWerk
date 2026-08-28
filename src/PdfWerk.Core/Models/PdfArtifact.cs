namespace PdfWerk.Core.Models;

/// <summary>
/// The output of any operation that produces a document. Held as a byte array rather than a
/// stream because every producer (PDFsharp, LibreOffice) materialises the whole file anyway,
/// and the API layer needs the length up-front to set Content-Length for the download path.
/// </summary>
public sealed record PdfArtifact(byte[] Content, string FileName, string ContentType = "application/pdf")
{
    public int ByteCount => Content.Length;
}

/// <summary>How the caller wants the result delivered.</summary>
public enum DeliveryMode
{
    /// <summary>Content-Disposition: attachment — the browser saves a file.</summary>
    Download,

    /// <summary>Content-Disposition: inline — render in place, e.g. in an iframe or object tag.</summary>
    Stream,

    /// <summary>JSON envelope with the document base64-encoded, for callers that want metadata alongside.</summary>
    Json,
}

/// <summary>Metadata reported by the inspect action.</summary>
public sealed record PdfInfo(
    int PageCount,
    string? Title,
    string? Author,
    string? Subject,
    string? Creator,
    DateTimeOffset? CreatedAt,
    bool HasAcroForm,
    bool IsEncrypted,
    long ByteCount,
    IReadOnlyList<ExistingFormField> Fields,
    IReadOnlyList<PageSize> Pages);

/// <summary>Page dimensions in points, as the designer needs them to scale its overlay.</summary>
public sealed record PageSize(int Page, double Width, double Height);
