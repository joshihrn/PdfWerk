using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// Writing into blank space, which find-and-replace could never do.
/// </summary>
public class AnnotateTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfAnnotator Annotator = new();
    private static readonly PdfTextExtractor Extractor = new();

    private static byte[] SamplePdf(string body = "Signature page.") =>
        Composer.Create(new CreateFromTextRequest
        {
            Content = body,
            Title = "Sample",
            Format = TextFormat.Plain,
        }).Content;

    private static string TextOf(byte[] pdf) => string.Join("\n", Extractor.ExtractPages(pdf));

    private static AnnotateItem Text(string text, double x = 72, double y = 400) => new()
    {
        Type = AnnotateItemType.Text,
        Page = 1,
        X = x,
        Y = y,
        Text = text,
    };

    [Fact]
    public void Text_can_be_added_where_there_was_none()
    {
        var original = SamplePdf();

        Assert.DoesNotContain("Ada Lovelace", TextOf(original), StringComparison.Ordinal);

        var annotated = Annotator.Annotate(original, new AnnotateRequest { Items = [Text("Ada Lovelace")] });

        Assert.Contains("Ada Lovelace", TextOf(annotated.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void The_original_content_survives()
    {
        var annotated = Annotator.Annotate(SamplePdf(), new AnnotateRequest { Items = [Text("Added")] });
        var text = TextOf(annotated.Content);

        // Drawn over the page, not instead of it.
        Assert.Contains("Signature page", text, StringComparison.Ordinal);
        Assert.Contains("Added", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_items_on_one_page_all_appear()
    {
        var annotated = Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items = [Text("First", y: 300), Text("Second", y: 340), Text("Third", y: 380)],
        });

        var text = TextOf(annotated.Content);

        foreach (var expected in new[] { "First", "Second", "Third" })
            Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_wraps_inside_a_width()
    {
        var annotated = Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items =
            [
                new AnnotateItem
                {
                    Page = 1,
                    X = 72,
                    Y = 300,
                    Width = 120,
                    FontSize = 10,
                    Text = "A sentence long enough that it cannot possibly fit on one short line.",
                },
            ],
        });

        // Wrapped output arrives as several lines; unwrapped would be one long one.
        var lines = TextOf(annotated.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(lines.Length > 2, "the text does not appear to have wrapped");
    }

    [Fact]
    public void A_height_clips_rather_than_running_down_the_page()
    {
        var many = string.Join(" ", Enumerable.Repeat("word", 400));

        var annotated = Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items = [new AnnotateItem { Page = 1, X = 72, Y = 300, Width = 200, Height = 30, FontSize = 10, Text = many }],
        });

        // Two lines fit in thirty points at ten point text; the rest must be dropped, not spilled
        // over the footer and off the bottom of the page.
        var occurrences = TextOf(annotated.Content).Split("word").Length - 1;

        Assert.InRange(occurrences, 1, 20);
    }

    [Fact]
    public void Shapes_can_be_drawn()
    {
        var annotated = Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items =
            [
                new AnnotateItem { Type = AnnotateItemType.Line, Page = 1, X = 72, Y = 420, Width = 200, Height = 0 },
                new AnnotateItem { Type = AnnotateItemType.Rectangle, Page = 1, X = 72, Y = 440, Width = 120, Height = 40 },
            ],
        });

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(annotated.Content, 0, 5), StringComparison.Ordinal);
    }

    // ---- refusals --------------------------------------------------------

    [Fact]
    public void A_page_that_does_not_exist_is_refused()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items = [Text("Nowhere") with { Page = 9 }],
        }));

        Assert.Contains("out of range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_text_is_refused_rather_than_drawn_as_nothing()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items = [Text("")],
        }));

        Assert.Contains("needs some text", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_request_says_so()
    {
        var ex = Assert.Throws<PdfWerkException>(() =>
            Annotator.Annotate(SamplePdf(), new AnnotateRequest { Items = [] }));

        Assert.Contains("at least one", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_colour_that_is_not_a_colour_is_refused()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Annotator.Annotate(SamplePdf(), new AnnotateRequest
        {
            Items = [Text("Coloured") with { Color = "burnt sienna" }],
        }));

        Assert.Contains("#RRGGBB", ex.Message, StringComparison.Ordinal);
    }
}
