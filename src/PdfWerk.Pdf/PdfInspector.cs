using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Forms;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Read-only introspection: what the document is, and what the designer needs in order to
/// overlay an editable field layer on top of it.
/// </summary>
public sealed class PdfInspector : IPdfInspector
{
    public PdfInfo Inspect(byte[] pdf, string fileName)
    {
        using var document = PdfGuard.Open(pdf, PdfDocumentOpenMode.Import);

        var fields = AcroFormIndex.Describe(document);
        var info = document.Info;

        var pages = new List<PageSize>(document.PageCount);
        for (var i = 0; i < document.PageCount; i++)
        {
            // Rendered dimensions, so the browser overlay lines up on rotated pages.
            var visual = FieldGeometry.Describe(document.Pages[i]);
            pages.Add(new PageSize(i + 1, Math.Round(visual.Width, 2), Math.Round(visual.Height, 2)));
        }

        return new PdfInfo(
            PageCount: document.PageCount,
            Title: NullIfEmpty(info.Title),
            Author: NullIfEmpty(info.Author),
            Subject: NullIfEmpty(info.Subject),
            Creator: NullIfEmpty(info.Creator),
            CreatedAt: ReadCreationDate(document),
            HasAcroForm: fields.Count > 0,
            IsEncrypted: document.SecuritySettings.IsEncrypted,
            ByteCount: pdf.Length,
            Fields: fields,
            Pages: pages);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Reads /CreationDate defensively: producers write malformed dates often enough that a
    /// bad value must not fail the whole inspection.
    /// </summary>
    private static DateTimeOffset? ReadCreationDate(PdfDocument document)
    {
        try
        {
            var created = document.Info.CreationDate;
            return created == default ? null : new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }
}
