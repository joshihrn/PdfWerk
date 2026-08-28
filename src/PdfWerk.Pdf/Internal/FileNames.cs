using System.Text;

namespace PdfWerk.Pdf.Internal;

/// <summary>
/// Builds safe download names. Everything here ends up in a Content-Disposition header, so the
/// output is restricted to a conservative character set rather than merely stripping the
/// characters the local filesystem happens to dislike.
/// </summary>
internal static class FileNames
{
    private const int MaxStemLength = 80;

    /// <summary>Derives "quarterly-report.pdf" from a title, falling back to <paramref name="fallback"/>.</summary>
    public static string FromTitle(string? title, string fallback, string extension = ".pdf")
    {
        var stem = Slug(title);
        if (stem.Length == 0)
            stem = Slug(fallback);
        if (stem.Length == 0)
            stem = "document";

        return stem + extension;
    }

    /// <summary>Replaces a file's extension, keeping the stem safe.</summary>
    public static string WithExtension(string fileName, string extension)
    {
        var stem = Slug(Path.GetFileNameWithoutExtension(fileName));
        if (stem.Length == 0)
            stem = "document";

        return stem + extension;
    }

    /// <summary>Marks a derived file, e.g. "contract.pdf" + "filled" becomes "contract-filled.pdf".</summary>
    public static string Suffixed(string fileName, string suffix)
    {
        var stem = Slug(Path.GetFileNameWithoutExtension(fileName));
        if (stem.Length == 0)
            stem = "document";

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
            ext = ".pdf";

        return $"{stem}-{Slug(suffix)}{ext}";
    }

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else if (ch is ' ' or '-' or '_' or '.' or '/' or '\\')
            {
                // Collapse any run of separators into a single dash.
                if (!lastWasDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            // Everything else — quotes, control characters, non-ASCII — is dropped outright.

            if (sb.Length >= MaxStemLength)
                break;
        }

        return sb.ToString().Trim('-');
    }
}
