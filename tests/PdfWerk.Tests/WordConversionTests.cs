using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;
using PdfWerk.Pdf.Word;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PdfWerk.Tests;

/// <summary>
/// Covers the managed .docx path and the converter selection around it. Fixtures are built with
/// OpenXML in-process so the suite needs no binary assets and no Word installation.
/// </summary>
public class WordConversionTests
{
    private static readonly PdfTextExtractor Extractor = new();

    private static OpenXmlWordConverter Managed => new();

    private static string TextOf(byte[] pdf) => string.Join("\n", Extractor.ExtractPages(pdf));

    // ---- fixtures --------------------------------------------------------

    private static byte[] BuildDocx(Action<W.Body> compose)
    {
        using var buffer = new MemoryStream();

        using (var package = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var body = new W.Body();
            compose(body);
            main.Document = new W.Document(body);
        }

        return buffer.ToArray();
    }

    private static W.Paragraph Para(string text, string? style = null, bool bold = false)
    {
        var run = bold
            ? new W.Run(new W.RunProperties(new W.Bold()), new W.Text(text))
            : new W.Run(new W.Text(text));

        return style is null
            ? new W.Paragraph(run)
            : new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }),
                run);
    }

    // ---- conversion ------------------------------------------------------

    [Fact]
    public async Task Converts_a_docx_into_a_readable_pdf()
    {
        var docx = BuildDocx(body =>
        {
            body.AppendChild(Para("Quarterly Report", "Heading1"));
            body.AppendChild(Para("Revenue grew this quarter.", bold: true));
            body.AppendChild(Para("Signed by the finance team."));
        });

        var artifact = await Managed.ConvertAsync(docx, "report.docx");

        Assert.Equal("report.pdf", artifact.FileName);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(artifact.Content, 0, 5), StringComparison.Ordinal);

        var text = TextOf(artifact.Content);
        Assert.Contains("Quarterly Report", text, StringComparison.Ordinal);
        Assert.Contains("Revenue grew", text, StringComparison.Ordinal);
        Assert.Contains("finance team", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Converts_tables()
    {
        var docx = BuildDocx(body =>
        {
            var table = new W.Table(
                new W.TableRow(
                    new W.TableCell(Para("Region")),
                    new W.TableCell(Para("Sales"))),
                new W.TableRow(
                    new W.TableCell(Para("EMEA")),
                    new W.TableCell(Para("120"))));

            body.AppendChild(table);
        });

        var text = TextOf((await Managed.ConvertAsync(docx, "table.docx")).Content);

        Assert.Contains("Region", text, StringComparison.Ordinal);
        Assert.Contains("EMEA", text, StringComparison.Ordinal);
        Assert.Contains("120", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Converts_multi_page_documents()
    {
        var docx = BuildDocx(body =>
        {
            body.AppendChild(Para("First page content."));
            body.AppendChild(new W.Paragraph(new W.Run(
                new W.Break { Type = W.BreakValues.Page },
                new W.Text("Second page content."))));
        });

        var pages = Extractor.ExtractPages((await Managed.ConvertAsync(docx, "multi.docx")).Content);

        Assert.Equal(2, pages.Count);
        Assert.Contains("First page", pages[0], StringComparison.Ordinal);
        Assert.Contains("Second page", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_paragraphs_become_blank_lines_not_dropped_content()
    {
        var docx = BuildDocx(body =>
        {
            body.AppendChild(Para("Above the gap."));
            body.AppendChild(new W.Paragraph());
            body.AppendChild(Para("Below the gap."));
        });

        var text = TextOf((await Managed.ConvertAsync(docx, "gap.docx")).Content);

        Assert.Contains("Above the gap", text, StringComparison.Ordinal);
        Assert.Contains("Below the gap", text, StringComparison.Ordinal);
    }

    // ---- rejection -------------------------------------------------------

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_docx()
    {
        var junk = System.Text.Encoding.ASCII.GetBytes(new string('x', 400));

        var ex = await Assert.ThrowsAsync<PdfWerkException>(() => Managed.ConvertAsync(junk, "fake.docx"));
        Assert.Contains("could not be read", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explains_that_legacy_doc_needs_libreoffice()
    {
        var anything = new byte[64];

        var ex = await Assert.ThrowsAsync<PdfWerkException>(() => Managed.ConvertAsync(anything, "old.doc"));
        Assert.Contains("LibreOffice", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".docx", ex.Message, StringComparison.Ordinal);
    }

    // ---- converter selection ---------------------------------------------

    /// <summary>Stands in for LibreOffice being absent, or present but broken.</summary>
    private sealed class StubConverter(string name, int priority, bool available, bool throws) : IWordConverter
    {
        public string Name => name;

        public int Priority => priority;

        public bool WasCalled { get; private set; }

        public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) => ValueTask.FromResult(available);

        public Task<PdfArtifact> ConvertAsync(byte[] source, string fileName, CancellationToken ct = default)
        {
            WasCalled = true;

            if (throws)
                throw new InvalidOperationException("simulated environment failure");

            return Task.FromResult(new PdfArtifact([1, 2, 3], "stub.pdf"));
        }
    }

    private static WordConversionPipeline Pipeline(params IWordConverter[] converters) =>
        new(converters, NullLogger<WordConversionPipeline>.Instance);

    [Fact]
    public async Task Pipeline_skips_a_converter_that_is_unavailable()
    {
        var absent = new StubConverter("libreoffice", 0, available: false, throws: false);
        var result = await Pipeline(absent, Managed).ConvertAsync(
            BuildDocx(b => b.AppendChild(Para("Hello."))), "doc.docx");

        Assert.False(absent.WasCalled);
        Assert.Equal("openxml", result.Converter);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task Pipeline_falls_back_when_the_preferred_converter_breaks()
    {
        var broken = new StubConverter("libreoffice", 0, available: true, throws: true);
        var result = await Pipeline(broken, Managed).ConvertAsync(
            BuildDocx(b => b.AppendChild(Para("Hello."))), "doc.docx");

        Assert.True(broken.WasCalled);
        Assert.Equal("openxml", result.Converter);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task Pipeline_prefers_the_lower_priority_converter()
    {
        var preferred = new StubConverter("libreoffice", 0, available: true, throws: false);
        var result = await Pipeline(Managed, preferred).ConvertAsync(
            BuildDocx(b => b.AppendChild(Para("Hello."))), "doc.docx");

        Assert.Equal("libreoffice", result.Converter);
    }

    [Fact]
    public async Task Pipeline_rejects_an_empty_upload()
    {
        await Assert.ThrowsAsync<PdfWerkException>(() => Pipeline(Managed).ConvertAsync([], "empty.docx"));
    }

    // ---- LibreOffice discovery -------------------------------------------

    [Fact]
    public async Task LibreOffice_reports_unavailable_when_disabled()
    {
        var converter = new LibreOfficeWordConverter(
            Options.Create(new LibreOfficeOptions { Enabled = false }),
            NullLogger<LibreOfficeWordConverter>.Instance);

        Assert.False(await converter.IsAvailableAsync());
    }

    [Fact]
    public async Task LibreOffice_reports_unavailable_for_a_bad_configured_path()
    {
        var converter = new LibreOfficeWordConverter(
            Options.Create(new LibreOfficeOptions { ExecutablePath = @"Z:\nope\soffice.exe" }),
            NullLogger<LibreOfficeWordConverter>.Instance);

        Assert.False(await converter.IsAvailableAsync());

        var ex = await Assert.ThrowsAsync<PdfWerkException>(() => converter.ConvertAsync([1, 2, 3], "x.docx"));
        Assert.Contains("not available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
