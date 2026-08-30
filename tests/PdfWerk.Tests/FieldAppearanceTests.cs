using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// A field's value has to be drawn, not merely stored.
/// </summary>
/// <remarks>
/// /V and /DV say what the value is; /NeedAppearances asks the viewer to render it. Chrome's
/// built-in viewer and pdf.js largely ignore that flag, so a field with a perfectly correct value
/// showed an empty box — which is indistinguishable from the default never having been applied.
///
/// These assertions therefore look for the value inside a drawn appearance stream, which is the
/// only evidence that anyone opening the file will actually see it.
/// </remarks>
public class FieldAppearanceTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfFormService Forms = new();

    private static byte[] SamplePdf() =>
        Composer.Create(new CreateFromTextRequest
        {
            Content = "Agreement.",
            Title = "Agreement",
            Format = TextFormat.Plain,
        }).Content;

    /// <summary>Every appearance stream in the document, inflated.</summary>
    private static string AppearanceStreams(byte[] pdf)
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
                builder.Append(Encoding.Latin1.GetString(output.ToArray())).Append('\n');
            }
            catch (InvalidDataException)
            {
                // Uncompressed streams are readable as they stand.
                builder.Append(text[start..end]).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static byte[] WithField(FormFieldType type, string? value, bool multiline = false) =>
        Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "field",
                    Type = type,
                    Rect = new FieldRect(1, 72, 200, 240, multiline ? 60 : 22),
                    Value = value,
                    Multiline = multiline,
                    Options = type is FormFieldType.Dropdown or FormFieldType.ListBox
                        ? ["Monthly", "Annual"]
                        : [],
                },
            ],
        }).Content;

    [Fact]
    public void A_text_field_draws_its_default_value()
    {
        var streams = AppearanceStreams(WithField(FormFieldType.Text, "Ada Lovelace"));

        Assert.Contains("(Ada Lovelace) Tj", streams, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FormFieldType.Dropdown)]
    [InlineData(FormFieldType.ListBox)]
    public void A_choice_field_draws_its_selected_option(FormFieldType type)
    {
        var streams = AppearanceStreams(WithField(type, "Annual"));

        Assert.Contains("(Annual) Tj", streams, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multiline_field_draws_every_line()
    {
        var streams = AppearanceStreams(WithField(FormFieldType.Text, "First line\nSecond line", multiline: true));

        Assert.Contains("(First line) Tj", streams, StringComparison.Ordinal);
        Assert.Contains("(Second line) Tj", streams, StringComparison.Ordinal);
        Assert.Contains("T*", streams, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_with_no_value_draws_no_text()
    {
        var streams = AppearanceStreams(WithField(FormFieldType.Text, null));

        // The border is still drawn; what must not appear is an empty string being shown, which
        // would leave a stray text object in every blank field on the page.
        Assert.DoesNotContain("() Tj", streams, StringComparison.Ordinal);
    }

    [Fact]
    public void Brackets_in_a_value_do_not_break_the_stream()
    {
        // An unescaped ")" ends the literal early and corrupts everything after it.
        var streams = AppearanceStreams(WithField(FormFieldType.Text, "Acme (UK) Ltd"));

        Assert.Contains(@"(Acme \(UK\) Ltd) Tj", streams, StringComparison.Ordinal);
    }

    [Fact]
    public void Filling_a_field_redraws_it()
    {
        var blank = WithField(FormFieldType.Text, null);

        var filled = Forms.FillFields(blank, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["field"] = "Grace Hopper" },
        }).Content;

        Assert.Contains("(Grace Hopper) Tj", AppearanceStreams(filled), StringComparison.Ordinal);
    }

    [Fact]
    public void A_filled_value_replaces_the_default_rather_than_joining_it()
    {
        var withDefault = WithField(FormFieldType.Text, "Ada Lovelace");

        var filled = Forms.FillFields(withDefault, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["field"] = "Grace Hopper" },
        }).Content;

        var streams = AppearanceStreams(filled);

        Assert.Contains("(Grace Hopper) Tj", streams, StringComparison.Ordinal);

        // The old appearance object may still be in the file as an orphan, but the widget must
        // not be pointing at it. Reading the value back is what the viewer will do.
        Assert.Equal("Grace Hopper", Forms.ReadFields(filled).Single().Value);
    }
}
