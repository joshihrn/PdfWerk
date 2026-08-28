using System.Text;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using UglyToad.PdfPig;
using PdfPigWord = UglyToad.PdfPig.Content.Word;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace PdfWerk.Pdf;

/// <summary>
/// Pulls plain text out of a document with PdfPig, which understands font encodings and glyph
/// positions well enough to recover reading order from real-world files.
/// </summary>
public sealed class PdfTextExtractor : IPdfTextExtractor
{
    /// <summary>
    /// Gap between two words, as a multiple of the current line height, above which a line break
    /// is assumed. Tuned so that multi-column layouts do not run together into one line.
    /// </summary>
    private const double LineBreakFactor = 0.6;

    public IReadOnlyList<string> ExtractPages(byte[] pdf)
    {
        Internal.PdfGuard.RequirePdf(pdf);

        try
        {
            using var document = PdfDocument.Open(pdf);
            var pages = new List<string>(document.NumberOfPages);

            foreach (var page in document.GetPages())
                pages.Add(ExtractPage(page));

            return pages;
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidPdfException("This PDF is password protected, so its text cannot be read.");
        }
        catch (Exception ex) when (ex is not PdfWerkException)
        {
            throw new InvalidPdfException($"The text of this PDF could not be extracted: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuilds a page's text from positioned words. The raw <c>page.Text</c> concatenates glyphs
    /// with no spacing information at all, which produces unusable input for a summarizer.
    /// </summary>
    private static string ExtractPage(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters).ToList();
        if (words.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        PdfPigWord? previous = null;

        foreach (var word in words)
        {
            if (previous is not null)
                sb.Append(IsNewLine(previous, word) ? '\n' : ' ');

            sb.Append(word.Text);
            previous = word;
        }

        return Normalise(sb.ToString());
    }

    private static bool IsNewLine(PdfPigWord previous, PdfPigWord current)
    {
        var previousBox = previous.BoundingBox;
        var currentBox = current.BoundingBox;

        // PDF user space has its origin at the bottom-left, so a later line sits lower.
        var verticalShift = Math.Abs(currentBox.Bottom - previousBox.Bottom);
        var lineHeight = Math.Max(previousBox.Height, 1);

        if (verticalShift > lineHeight * LineBreakFactor)
            return true;

        // Same line vertically, but the x position jumped backwards: a wrap or a new column.
        return currentBox.Left < previousBox.Left - lineHeight;
    }

    /// <summary>
    /// Collapses runs of horizontal whitespace and trims line ends, leaving newlines intact.
    /// </summary>
    /// <remarks>
    /// Producers frequently emit each space as its own positioned glyph, which the word extractor
    /// then reports as a standalone word — so naive joining yields "Ada   Lovelace". Paragraph
    /// breaks are genuine signal for a summarizer, so only horizontal runs are squeezed.
    /// </remarks>
    private static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        var atLineStart = true;

        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                pendingSpace = false;
                atLineStart = true;
                sb.Append('\n');
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                // Defer the space: it is only emitted if real content follows on this line.
                pendingSpace = !atLineStart;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            atLineStart = false;
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
