using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// Guards the page dimensions a table's columns are measured against.
/// </summary>
/// <remarks>
/// Setting <c>PageFormat</c> does not populate <c>PageWidth</c>; MigraDoc resolves the format at
/// render time, so anything reading the width while the document is being built sees zero. Column
/// widths were computed as <c>(PageWidth - margins) / columns</c>, which came out negative, and a
/// two-column table rendered as a narrow strip at the left edge with its second column off the
/// paper altogether.
///
/// Text extraction cannot see this: the words are all present and in the right order, they are
/// merely drawn outside the page. So these read coordinates out of the content stream instead.
/// </remarks>
public class PageGeometryTests
{
    private static readonly PdfComposer Composer = new();

    private const string TableDocument =
        """
        # Service Agreement

        | Item | Amount |
        | --- | ---: |
        | Setup | 1,200 |
        | Monthly retainer | 350 |
        """;

    /// <summary>
    /// Inflates every content stream and returns them as one string of PDF operators.
    /// </summary>
    /// <remarks>
    /// The streams are zlib, and each is followed by an end-of-line before <c>endstream</c>.
    /// ZLibStream stops at the end of the compressed data and ignores that trailer, which is
    /// what makes reading them this way workable without a parser.
    /// </remarks>
    private static string Operators(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var builder = new StringBuilder();

        foreach (Match match in Regex.Matches(text, @"stream\r?\n"))
        {
            var start = match.Index + match.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;

            var body = Encoding.Latin1.GetBytes(text[start..end]);

            try
            {
                using var input = new MemoryStream(body);
                using var inflate = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflate.CopyTo(output);
                builder.Append(Encoding.Latin1.GetString(output.ToArray()));
                builder.Append('\n');
            }
            catch (InvalidDataException)
            {
                // Not a compressed content stream; nothing to read here.
            }
        }

        return builder.ToString();
    }

    /// <summary>Every rectangle drawn on the page, as (x, y, width, height).</summary>
    private static List<(double X, double Y, double Width, double Height)> Rectangles(string operators) =>
        [.. Regex.Matches(operators, @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+re")
            .Select(m => (
                double.Parse(m.Groups[1].Value),
                double.Parse(m.Groups[2].Value),
                double.Parse(m.Groups[3].Value),
                double.Parse(m.Groups[4].Value)))];

    private static byte[] Render(PagePreset page = PagePreset.A4,
                                 PageOrientation orientation = PageOrientation.Portrait) =>
        Composer.Create(new CreateFromTextRequest
        {
            Content = TableDocument,
            Format = TextFormat.Markdown,
            Title = "Service Agreement",
            Page = page,
            Orientation = orientation,
            MarginMm = 20,
        }).Content;

    [Fact]
    public void A_table_is_drawn_inside_the_page_not_off_its_left_edge()
    {
        var rectangles = Rectangles(Operators(Render()));

        Assert.NotEmpty(rectangles);

        // A cell whose left edge is at or beyond zero is off the paper. The shading rectangles
        // for the header row were being placed at x = -56.7 with a width of -57.2.
        Assert.All(rectangles, r => Assert.True(
            r.X >= 0 && r.X + r.Width >= 0,
            $"a rectangle was drawn off the left edge of the page at x={r.X:0.#} width={r.Width:0.#}"));
    }

    [Fact]
    public void Table_cells_have_a_positive_width()
    {
        var rectangles = Rectangles(Operators(Render()));

        // A negative width is how the fault presented: usable width came out as minus the
        // margins, so every column was sized at half of that.
        Assert.All(rectangles, r => Assert.True(
            r.Width >= 0,
            $"a rectangle was drawn with a negative width of {r.Width:0.#}"));
    }

    [Fact]
    public void A_two_column_table_spans_most_of_the_printable_width()
    {
        var rectangles = Rectangles(Operators(Render()));

        var rightmost = rectangles.Max(r => Math.Max(r.X, r.X + r.Width));

        // A4 is 595.28pt wide with 20mm margins, leaving about 482pt of printable width. The
        // broken output reached x = 57. Anything under half the page means the columns are being
        // sized against a page width of zero again.
        Assert.True(rightmost > 300,
            $"the table only reached x={rightmost:0.#}, so it is not using the printable width");
    }

    [Theory]
    [InlineData(PagePreset.A4, PageOrientation.Portrait)]
    [InlineData(PagePreset.A4, PageOrientation.Landscape)]
    [InlineData(PagePreset.Letter, PageOrientation.Portrait)]
    [InlineData(PagePreset.Legal, PageOrientation.Portrait)]
    [InlineData(PagePreset.A3, PageOrientation.Portrait)]
    [InlineData(PagePreset.A5, PageOrientation.Portrait)]
    public void Every_preset_and_orientation_produces_a_usable_width(
        PagePreset page, PageOrientation orientation)
    {
        var rectangles = Rectangles(Operators(Render(page, orientation)));

        Assert.All(rectangles, r => Assert.True(
            r.X >= 0 && r.Width >= 0,
            $"{page}/{orientation} drew a rectangle at x={r.X:0.#} width={r.Width:0.#}"));
    }

    [Fact]
    public void Landscape_is_wider_than_portrait()
    {
        var portrait = Rectangles(Operators(Render(PagePreset.A4, PageOrientation.Portrait)))
            .Max(r => r.X + r.Width);
        var landscape = Rectangles(Operators(Render(PagePreset.A4, PageOrientation.Landscape)))
            .Max(r => r.X + r.Width);

        // Catches the dimensions being applied without honouring orientation, which would leave
        // a landscape page laid out to portrait width.
        Assert.True(landscape > portrait,
            $"landscape reached {landscape:0.#} but portrait reached {portrait:0.#}");
    }
}
