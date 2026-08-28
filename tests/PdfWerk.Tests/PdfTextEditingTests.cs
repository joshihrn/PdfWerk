using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;

namespace PdfWerk.Tests;

/// <summary>
/// Covers find-and-replace. The fixtures come from the composer, which embeds subset fonts, so
/// these exercise whichever strategy the editor selects rather than assuming one of them.
/// </summary>
public class PdfTextEditingTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfTextEditor Editor = new();
    private static readonly PdfTextExtractor Extractor = new();
    private static readonly PdfInspector Inspector = new();

    private static byte[] Document(string body) =>
        Composer.Create(new CreateFromTextRequest
        {
            Content = body,
            Format = TextFormat.Plain,
            PageNumbers = false,
        }).Content;

    private static string TextOf(byte[] pdf) => string.Join("\n", Extractor.ExtractPages(pdf));

    [Fact]
    public void Replace_swaps_the_text_and_reports_a_count()
    {
        var pdf = Document("Invoice for Acme Corporation.\n\nPlease remit to Acme Corporation.");

        var (artifact, count) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "Acme Corporation", Replace = "Globex Inc" }],
        });

        Assert.True(count >= 1, $"Expected at least one replacement, got {count}.");

        var text = TextOf(artifact.Content);
        Assert.Contains("Globex Inc", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme Corporation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_is_case_sensitive_by_default()
    {
        var pdf = Document("Status: PENDING and pending.");

        var (artifact, _) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "PENDING", Replace = "APPROVED" }],
        });

        var text = TextOf(artifact.Content);
        Assert.Contains("APPROVED", text, StringComparison.Ordinal);
        Assert.Contains("pending", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_can_ignore_case()
    {
        var pdf = Document("Status: PENDING and pending.");

        var (artifact, count) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "pending", Replace = "done", MatchCase = false }],
        });

        Assert.True(count >= 2, $"Expected both occurrences to match, got {count}.");
        Assert.DoesNotContain("pending", TextOf(artifact.Content), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replace_reports_when_nothing_matched()
    {
        var pdf = Document("Nothing to see here.");

        var ex = Assert.Throws<PdfWerkException>(() => Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "absent phrase", Replace = "x" }],
        }));

        Assert.Contains("absent phrase", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_can_tolerate_no_match()
    {
        var pdf = Document("Nothing to see here.");

        var (artifact, count) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "absent phrase", Replace = "x" }],
            FailOnNoMatch = false,
        });

        Assert.Equal(0, count);
        Assert.Contains("Nothing to see here", TextOf(artifact.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_rejects_an_empty_search()
    {
        var pdf = Document("Some content.");

        Assert.Throws<PdfWerkException>(() => Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "", Replace = "x" }],
        }));
    }

    [Fact]
    public void Edited_document_is_still_a_valid_pdf()
    {
        var pdf = Document("Contract with Acme Corporation.");

        var (artifact, _) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "Acme", Replace = "Globex" }],
        });

        var info = Inspector.Inspect(artifact.Content, "edited.pdf");
        Assert.Equal(1, info.PageCount);
        Assert.False(info.IsEncrypted);
    }

    // ---- inspection ------------------------------------------------------

    [Fact]
    public void Inspect_reports_pages_metadata_and_fields()
    {
        var pdf = Composer.Create(new CreateFromTextRequest
        {
            Content = "Body text.",
            Title = "Service Agreement",
            Author = "Legal",
            Format = TextFormat.Plain,
        }).Content;

        var forms = new PdfFormService();
        var withField = forms.EditFields(pdf, new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "signature",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 60, 400, 200, 24),
                },
            ],
        });

        var info = Inspector.Inspect(withField.Content, "agreement.pdf");

        Assert.Equal(1, info.PageCount);
        Assert.Equal("Service Agreement", info.Title);
        Assert.Equal("Legal", info.Author);
        Assert.True(info.HasAcroForm);
        Assert.Single(info.Fields);
        Assert.Equal("signature", info.Fields[0].Name);

        // A4 portrait, reported in points for the designer overlay.
        Assert.Single(info.Pages);
        Assert.Equal(595, info.Pages[0].Width, 0);
        Assert.Equal(842, info.Pages[0].Height, 0);
    }
}
