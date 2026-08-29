using PdfWerk.Core.Models;

namespace PdfWerk.Core.Abstractions;

/// <summary>Builds brand-new PDFs from text or Markdown.</summary>
public interface IPdfComposer
{
    PdfArtifact Create(CreateFromTextRequest request);
}

/// <summary>Rewrites text content inside an existing document.</summary>
public interface IPdfTextEditor
{
    /// <returns>The edited document and the number of replacements actually applied.</returns>
    (PdfArtifact Artifact, int ReplacementCount) ReplaceText(byte[] pdf, EditTextRequest request);
}

/// <summary>Reads and writes a document's AcroForm.</summary>
public interface IPdfFormService
{
    /// <summary>Adds and removes field definitions, returning the modified document.</summary>
    PdfArtifact EditFields(byte[] pdf, EditFormFieldsRequest request);

    /// <summary>Merges values into an existing form, optionally flattening it.</summary>
    PdfArtifact FillFields(byte[] pdf, FillFormRequest request);

    /// <summary>Lists the fields currently defined on the document.</summary>
    IReadOnlyList<ExistingFormField> ReadFields(byte[] pdf);
}

/// <summary>Concatenates documents.</summary>
public interface IPdfMerger
{
    PdfArtifact Merge(IReadOnlyList<(string FileName, byte[] Content)> documents, string outputFileName);
}

/// <summary>Pulls plain text out of a document, for summarization and search.</summary>
public interface IPdfTextExtractor
{
    /// <returns>One entry per page, in document order.</returns>
    IReadOnlyList<string> ExtractPages(byte[] pdf);
}

/// <summary>Reports structural metadata without modifying the document.</summary>
public interface IPdfInspector
{
    PdfInfo Inspect(byte[] pdf, string fileName);
}

/// <summary>
/// Converts word-processor documents to PDF. Implementations advertise availability so the
/// pipeline can fall back from LibreOffice to the in-process renderer at runtime.
/// </summary>
public interface IWordConverter
{
    /// <summary>Stable key used in logs and in the X-PdfWerk-Converter response header.</summary>
    string Name { get; }

    /// <summary>Lower runs first. LibreOffice is 0; the managed fallback is 100.</summary>
    int Priority { get; }

    /// <summary>Cheap, cached check — does this converter have what it needs on this machine?</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<PdfArtifact> ConvertAsync(byte[] source, string fileName, CancellationToken ct = default);
}

/// <summary>Divides a document into page ranges.</summary>
public interface IPdfSplitter
{
    IReadOnlyList<SplitPart> Split(byte[] pdf, SplitRequest request, string sourceName);
}

/// <summary>Turns pages by a quarter turn.</summary>
public interface IPdfRotator
{
    PdfArtifact Rotate(byte[] pdf, RotateRequest request);
}

/// <summary>Stamps text across pages.</summary>
public interface IPdfWatermarker
{
    PdfArtifact Apply(byte[] pdf, WatermarkRequest request);
}
