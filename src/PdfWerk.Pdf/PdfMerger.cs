using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Concatenates documents in the order supplied, carrying interactive form fields across
/// where the sources have them.
/// </summary>
public sealed class PdfMerger : IPdfMerger
{
    public PdfArtifact Merge(IReadOnlyList<(string FileName, byte[] Content)> documents, string outputFileName)
    {
        if (documents.Count == 0)
            throw new PdfWerkException("Supply at least one document to merge.");

        using var output = new PdfDocument();
        output.Info.Title = Path.GetFileNameWithoutExtension(outputFileName);
        output.Info.Creator = "PdfWerk";

        var mergedFields = new List<PdfItem>();
        var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);
        var sawAcroForm = false;

        foreach (var (fileName, content) in documents)
        {
            PdfDocument source;
            try
            {
                source = PdfGuard.Open(content, PdfDocumentOpenMode.Import);
            }
            catch (InvalidPdfException ex)
            {
                // Name the offending file: with a dozen inputs, "one of them is corrupt" is useless.
                throw new InvalidPdfException($"'{fileName}' could not be merged. {ex.Message}");
            }

            using (source)
            {
                if (source.PageCount == 0)
                    throw new InvalidPdfException($"'{fileName}' contains no pages.");

                foreach (var page in source.Pages)
                    output.AddPage(page);

                sawAcroForm |= CollectFields(source, mergedFields, seenFieldNames);
            }
        }

        if (sawAcroForm && mergedFields.Count > 0)
            AttachAcroForm(output, mergedFields);

        return new PdfArtifact(PdfGuard.Save(output), FileNames.WithExtension(outputFileName, ".pdf"));
    }

    /// <summary>
    /// Gathers the top-level field references from a source AcroForm.
    /// </summary>
    /// <remarks>
    /// Field names must be unique across a single AcroForm, so a name that already arrived from
    /// an earlier document is skipped rather than renamed — silently rewriting a caller's field
    /// names would break the fill step that almost always follows a merge.
    /// </remarks>
    private static bool CollectFields(PdfDocument source, List<PdfItem> into, HashSet<string> seen)
    {
        var acroForm = source.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        if (acroForm is null)
            return false;

        var fields = acroForm.Elements.GetArray("/Fields");
        if (fields is null || fields.Elements.Count == 0)
            return true;

        foreach (var item in fields.Elements)
        {
            var dict = ResolveDictionary(item);
            var name = dict?.Elements.GetString("/T");

            if (string.IsNullOrEmpty(name) || seen.Add(name))
                into.Add(item);
        }

        return true;
    }

    /// <summary>Rebuilds a catalog-level AcroForm on the merged document.</summary>
    private static void AttachAcroForm(PdfDocument output, List<PdfItem> fields)
    {
        var acroForm = new PdfDictionary(output);
        var array = new PdfArray(output);

        foreach (var field in fields)
            array.Elements.Add(field);

        acroForm.Elements["/Fields"] = array;

        // Without this, viewers render field values only after the user clicks into each widget.
        acroForm.Elements["/NeedAppearances"] = new PdfBoolean(true);

        output.Internals.Catalog.Elements["/AcroForm"] = acroForm;
    }

    private static PdfDictionary? ResolveDictionary(PdfItem item) => item switch
    {
        PdfReference reference => reference.Value as PdfDictionary,
        PdfDictionary dictionary => dictionary,
        _ => null,
    };
}
