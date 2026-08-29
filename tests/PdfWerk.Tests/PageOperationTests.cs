using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;

namespace PdfWerk.Tests;

/// <summary>Covers page range parsing, splitting, rotation and watermarking.</summary>
public class PageOperationTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfSplitter Splitter = new();
    private static readonly PdfRotator Rotator = new();
    private static readonly PdfWatermarker Watermarker = new();
    private static readonly PdfInspector Inspector = new();
    private static readonly PdfTextExtractor Extractor = new();

    private static readonly PdfMerger Merger = new();

    /// <summary>
    /// A document of <paramref name="pages"/> pages where page N contains only "MARKERN".
    /// </summary>
    /// <remarks>
    /// Built by merging single-page documents rather than by relying on text overflowing onto
    /// new pages: pagination depends on font metrics, so a layout change could silently alter
    /// the page count and quietly invalidate every assertion below.
    /// </remarks>
    private static byte[] MultiPage(int pages = 5)
    {
        var singles = Enumerable.Range(1, pages)
            .Select(i => ($"p{i}.pdf", Composer.Create(new CreateFromTextRequest
            {
                Content = $"MARKER{i}",
                Format = TextFormat.Plain,
                PageNumbers = false,
            }).Content))
            .ToList();

        return pages == 1 ? singles[0].Item2 : Merger.Merge(singles, "multi.pdf").Content;
    }

    // ---- page range parsing ----------------------------------------------

    [Theory]
    [InlineData("all", 5, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("", 5, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("odd", 5, new[] { 1, 3, 5 })]
    [InlineData("even", 5, new[] { 2, 4 })]
    [InlineData("first", 5, new[] { 1 })]
    [InlineData("last", 5, new[] { 5 })]
    [InlineData("2", 5, new[] { 2 })]
    [InlineData("2-4", 5, new[] { 2, 3, 4 })]
    [InlineData("1,3,5", 5, new[] { 1, 3, 5 })]
    [InlineData("3-", 5, new[] { 3, 4, 5 })]
    [InlineData("-3", 5, new[] { 1, 2, 3 })]
    [InlineData("1-2,4-5", 5, new[] { 1, 2, 4, 5 })]
    [InlineData("3,1,2", 5, new[] { 1, 2, 3 })]       // sorted
    [InlineData("1-3,2-4", 5, new[] { 1, 2, 3, 4 })]  // overlaps merged
    public void Page_ranges_resolve_as_written(string expression, int pageCount, int[] expected)
    {
        Assert.Equal(expected, PageRange.Resolve(expression, pageCount));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("4-2")]
    [InlineData("abc")]
    [InlineData("1-abc")]
    [InlineData("--")]
    public void Malformed_page_ranges_are_rejected_with_an_explanation(string expression)
    {
        var ex = Assert.Throws<PdfWerkException>(() => PageRange.Resolve(expression, 5));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    // ---- split -----------------------------------------------------------

    [Fact]
    public void Extract_returns_one_document_with_the_selected_pages()
    {
        var parts = Splitter.Split(MultiPage(), new SplitRequest { Pages = "2-3" }, "source.pdf");

        Assert.Single(parts);
        Assert.Equal([2, 3], parts[0].Pages);
        Assert.Equal(2, Inspector.Inspect(parts[0].Content, parts[0].Name).PageCount);
    }

    [Fact]
    public void Burst_returns_one_document_per_page()
    {
        var parts = Splitter.Split(MultiPage(), new SplitRequest { Pages = "all", Mode = SplitMode.Burst }, "source.pdf");

        Assert.Equal(5, parts.Count);
        Assert.All(parts, p => Assert.Equal(1, Inspector.Inspect(p.Content, p.Name).PageCount));

        // Names must sort correctly in a file listing.
        Assert.Equal(parts.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), parts.Select(p => p.Name));
    }

    [Fact]
    public void Groups_returns_one_document_per_comma_separated_group()
    {
        var parts = Splitter.Split(
            MultiPage(),
            new SplitRequest { Pages = "1-2,4-5", Mode = SplitMode.Groups },
            "source.pdf");

        Assert.Equal(2, parts.Count);
        Assert.Equal([1, 2], parts[0].Pages);
        Assert.Equal([4, 5], parts[1].Pages);
    }

    [Fact]
    public void Split_keeps_the_right_content_on_the_right_page()
    {
        var parts = Splitter.Split(MultiPage(), new SplitRequest { Pages = "all", Mode = SplitMode.Burst }, "source.pdf");

        // The whole point of a split is that page N of the source becomes the output; verify
        // by content rather than by page count alone.
        var first = string.Join(" ", Extractor.ExtractPages(parts[0].Content));
        Assert.Contains("MARKER1", first, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER5", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Splitting_past_the_end_is_rejected()
    {
        Assert.Throws<PdfWerkException>(() =>
            Splitter.Split(MultiPage(3), new SplitRequest { Pages = "1-9" }, "source.pdf"));
    }

    // ---- rotate ----------------------------------------------------------

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(-90)]
    public void Rotation_is_recorded_on_the_page(int degrees)
    {
        var rotated = Rotator.Rotate(MultiPage(2), new RotateRequest { Degrees = degrees, Pages = "all" });

        var info = Inspector.Inspect(rotated.Content, "rotated.pdf");
        var expected = ((degrees % 360) + 360) % 360;

        // A quarter turn swaps the reported page dimensions, which is what the designer overlay
        // and any renderer will see.
        var swapped = expected is 90 or 270;
        var page = info.Pages[0];

        Assert.True(
            swapped ? page.Width > page.Height : page.Width < page.Height,
            $"After {degrees}° the page reported {page.Width}x{page.Height}.");
    }

    [Fact]
    public void Rotation_accumulates_unless_absolute_is_requested()
    {
        var once = Rotator.Rotate(MultiPage(1), new RotateRequest { Degrees = 90 });
        var twice = Rotator.Rotate(once.Content, new RotateRequest { Degrees = 90 });

        // 90 + 90 = 180, so the page is portrait again rather than still landscape.
        var page = Inspector.Inspect(twice.Content, "r.pdf").Pages[0];
        Assert.True(page.Height > page.Width);

        var absolute = Rotator.Rotate(once.Content, new RotateRequest { Degrees = 90, Absolute = true });
        var absolutePage = Inspector.Inspect(absolute.Content, "r.pdf").Pages[0];
        Assert.True(absolutePage.Width > absolutePage.Height);
    }

    [Fact]
    public void Rotating_only_some_pages_leaves_the_others_alone()
    {
        var rotated = Rotator.Rotate(MultiPage(4), new RotateRequest { Degrees = 90, Pages = "2" });
        var pages = Inspector.Inspect(rotated.Content, "r.pdf").Pages;

        Assert.True(pages[0].Height > pages[0].Width, "page 1 should be untouched");
        Assert.True(pages[1].Width > pages[1].Height, "page 2 should be rotated");
        Assert.True(pages[2].Height > pages[2].Width, "page 3 should be untouched");
    }

    [Theory]
    [InlineData(45)]
    [InlineData(1)]
    [InlineData(100)]
    public void A_rotation_that_is_not_a_quarter_turn_is_rejected(int degrees)
    {
        var ex = Assert.Throws<PdfWerkException>(() =>
            Rotator.Rotate(MultiPage(1), new RotateRequest { Degrees = degrees }));

        Assert.Contains("quarter turn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- watermark -------------------------------------------------------

    [Fact]
    public void Watermark_text_lands_on_every_selected_page()
    {
        var stamped = Watermarker.Apply(MultiPage(3), new WatermarkRequest { Text = "CONFIDENTIAL" });

        var pages = Extractor.ExtractPages(stamped.Content);
        Assert.All(pages, page => Assert.Contains("CONFIDENTIAL", page, StringComparison.Ordinal));
    }

    [Fact]
    public void Watermarking_a_subset_leaves_other_pages_unmarked()
    {
        var stamped = Watermarker.Apply(MultiPage(3), new WatermarkRequest { Text = "DRAFT", Pages = "1" });

        var pages = Extractor.ExtractPages(stamped.Content);
        Assert.Contains("DRAFT", pages[0], StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_watermark_drawn_behind_content_still_produces_a_valid_document()
    {
        var stamped = Watermarker.Apply(MultiPage(2), new WatermarkRequest
        {
            Text = "SAMPLE",
            BehindContent = true,
            Opacity = 0.3,
        });

        Assert.Equal(2, Inspector.Inspect(stamped.Content, "w.pdf").PageCount);
        Assert.Contains("MARKER1", string.Join(" ", Extractor.ExtractPages(stamped.Content)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WatermarkPosition.Diagonal)]
    [InlineData(WatermarkPosition.Horizontal)]
    [InlineData(WatermarkPosition.Vertical)]
    public void Every_orientation_renders(WatermarkPosition position)
    {
        var stamped = Watermarker.Apply(MultiPage(1), new WatermarkRequest { Text = "COPY", Position = position });

        Assert.Equal(1, Inspector.Inspect(stamped.Content, "w.pdf").PageCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_watermark_text_is_rejected(string text)
    {
        Assert.Throws<PdfWerkException>(() => Watermarker.Apply(MultiPage(1), new WatermarkRequest { Text = text }));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void Opacity_outside_zero_to_one_is_rejected(double opacity)
    {
        Assert.Throws<PdfWerkException>(() =>
            Watermarker.Apply(MultiPage(1), new WatermarkRequest { Text = "X", Opacity = opacity }));
    }

    [Theory]
    [InlineData("not-a-colour")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void An_invalid_colour_is_rejected(string colour)
    {
        Assert.Throws<PdfWerkException>(() =>
            Watermarker.Apply(MultiPage(1), new WatermarkRequest { Text = "X", Color = colour }));
    }

    [Fact]
    public void Overlong_watermark_text_is_rejected()
    {
        Assert.Throws<PdfWerkException>(() =>
            Watermarker.Apply(MultiPage(1), new WatermarkRequest { Text = new string('x', 500) }));
    }
}
