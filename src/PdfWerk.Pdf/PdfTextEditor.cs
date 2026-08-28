using PdfSharp.Pdf.Advanced;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Fonts;
using PdfWerk.Pdf.Internal;
using PdfWerk.Pdf.Text;

namespace PdfWerk.Pdf;

/// <summary>
/// Find-and-replace over the text of an existing document.
/// </summary>
/// <remarks>
/// <para>
/// Editing happens in the content stream, decoding each showing font through its /ToUnicode CMap,
/// so the original words are removed from the file rather than painted over. A covered-up
/// replacement would still be extractable by copy-paste and by search — wrong for an edit, and a
/// disclosure risk for anyone using it to strip sensitive text.
/// </para>
/// <para>
/// Two cases cannot be edited and are reported honestly instead of approximated: a composite font
/// with no /ToUnicode map, whose glyph codes cannot be read as text at all, and an embedded
/// subset that lacks a glyph the replacement needs. Both leave the document unchanged.
/// </para>
/// </remarks>
public sealed class PdfTextEditor : IPdfTextEditor
{
    static PdfTextEditor() => FileSystemFontResolver.Install();

    public (PdfArtifact Artifact, int ReplacementCount) ReplaceText(byte[] pdf, EditTextRequest request)
    {
        if (request.Replacements.Count == 0)
            throw new PdfWerkException("Supply at least one replacement.");

        foreach (var replacement in request.Replacements)
        {
            if (string.IsNullOrEmpty(replacement.Find))
                throw new PdfWerkException("A replacement's 'find' text cannot be empty.");
        }

        var counts = new int[request.Replacements.Count];
        var content = Apply(pdf, request.Replacements, counts);
        var total = counts.Sum();

        if (total == 0 && request.FailOnNoMatch)
        {
            var terms = string.Join(", ", request.Replacements.Select(r => $"'{r.Find}'"));
            throw new PdfWerkException(
                $"No occurrences of {terms} could be replaced in this document. " +
                "The text may not be present, or it may be drawn with a font that does not " +
                "carry the character mapping needed to edit it — scanned pages are the usual " +
                "cause. Set failOnNoMatch=false to accept an unchanged document.");
        }

        return (new PdfArtifact(content, "edited.pdf"), total);
    }

    /// <summary>Runs the rewrite over every page, tracking per-instruction hit counts.</summary>
    private static byte[] Apply(byte[] pdf, IReadOnlyList<TextReplacement> replacements, int[] counts)
    {
        using var document = PdfGuard.Open(pdf);
        var changed = false;

        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            var pageNumber = pageIndex + 1;

            var applicable = Enumerable.Range(0, replacements.Count)
                .Where(i => replacements[i].Page is null || replacements[i].Page == pageNumber)
                .ToList();

            if (applicable.Count == 0)
                continue;

            var page = document.Pages[pageIndex];
            var fonts = FontMap.ForPage(page);

            // Each content stream is edited where it lives. CreateSingleContent() would let a
            // phrase split across streams be matched, but it hands back a detached copy whose
            // edits never reach the saved file, so the page's own streams are used instead.
            foreach (var content in page.Contents)
            {
                if (content.Stream is null)
                    continue;

                if (content.Stream.IsFiltered() && !content.Stream.TryUncompress())
                    continue;   // an unsupported filter, e.g. JBIG2

                var source = content.Stream.Value;
                if (source is null || source.Length == 0)
                    continue;

                // Each instruction runs on its own pass so its hits can be attributed to it.
                var working = source;
                var streamChanged = false;

                foreach (var index in applicable)
                {
                    var replacement = replacements[index];

                    var (updated, count) = ContentStreamEditor.Apply(
                        working,
                        [(replacement.Find, replacement.Replace, replacement.MatchCase)],
                        fonts);

                    if (count == 0)
                        continue;

                    counts[index] += count;
                    working = updated;
                    streamChanged = true;
                }

                if (!streamChanged)
                    continue;

                // The stream was uncompressed in place, so the old filter no longer describes it.
                content.Elements.Remove("/Filter");
                content.Elements.Remove("/DecodeParms");
                content.Stream.Value = working;
                content.Elements.SetInteger("/Length", working.Length);

                changed = true;
            }
        }

        return changed ? PdfGuard.Save(document) : pdf;
    }
}
