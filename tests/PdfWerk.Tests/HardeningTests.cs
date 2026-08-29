using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;

namespace PdfWerk.Tests;

/// <summary>
/// Adversarial input, of the kind a public endpoint actually receives.
/// </summary>
/// <remarks>
/// The bar here is not "produces a nice result" — it is that hostile or malformed input either
/// works correctly or fails as a clean 4xx, and never corrupts a document, hangs, or escapes into
/// an unhandled exception that the API would report as a 500.
/// </remarks>
public class HardeningTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfFormService Forms = new();
    private static readonly PdfTextEditor Editor = new();
    private static readonly PdfTextExtractor Extractor = new();
    private static readonly PdfInspector Inspector = new();
    private static readonly PdfMerger Merger = new();

    private static byte[] Document(string body = "Ordinary content for testing.") =>
        Composer.Create(new CreateFromTextRequest { Content = body, Format = TextFormat.Plain }).Content;

    private static string TextOf(byte[] pdf) => string.Join("\n", Extractor.ExtractPages(pdf));

    // ---- malformed input -------------------------------------------------

    [Fact]
    public void A_header_alone_does_not_make_a_pdf()
    {
        // Passes the magic-byte check, then falls apart. The reader must not leak its own
        // exception type through as a 500.
        var fake = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('\0', 400));

        Assert.Throws<InvalidPdfException>(() => Inspector.Inspect(fake, "fake.pdf"));
    }

    [Fact]
    public void A_truncated_document_is_rejected_cleanly()
    {
        var whole = Document();
        var half = whole[..(whole.Length / 2)];

        var ex = Record.Exception(() => Inspector.Inspect(half, "truncated.pdf"));

        // Either it recovers (PDF readers are forgiving) or it fails as a client error.
        // What it must never do is throw something unmapped.
        Assert.True(ex is null or InvalidPdfException, $"Unexpected: {ex?.GetType().Name}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public void Undersized_input_is_rejected(int size)
    {
        Assert.Throws<InvalidPdfException>(() => PdfProbe.RequirePdf(new byte[size]));
    }

    [Fact]
    public void Random_bytes_are_never_mistaken_for_a_document()
    {
        var random = new Random(1234);
        var noise = new byte[4096];
        random.NextBytes(noise);

        Assert.Throws<InvalidPdfException>(() => PdfProbe.RequirePdf(noise));
    }

    // ---- content stream escaping -----------------------------------------

    /// <summary>
    /// Replacement text is written straight into a PDF literal string, where '(', ')' and '\'
    /// are syntax. Unescaped, they would terminate the string early and corrupt every object
    /// after it — the classic injection bug for this kind of editor.
    /// </summary>
    [Theory]
    [InlineData("(unbalanced")]
    [InlineData("unbalanced)")]
    [InlineData("both (parens) here")]
    [InlineData(@"a backslash \ and more \\")]
    [InlineData(@"terminator \) attempt")]
    [InlineData("newline\nand\ttab")]
    public void Replacement_text_containing_pdf_syntax_does_not_corrupt_the_document(string hostile)
    {
        var pdf = Document("Replace TARGET please.");

        var (artifact, count) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "TARGET", Replace = hostile }],
            FailOnNoMatch = false,
        });

        // Whether or not the edit applied, the result must still parse as a PDF.
        var info = Inspector.Inspect(artifact.Content, "out.pdf");
        Assert.Equal(1, info.PageCount);

        if (count > 0)
        {
            // And the text must survive intact, not truncated at the first delimiter.
            var text = TextOf(artifact.Content);
            var expected = hostile.Split('\n', '\t')[0];
            Assert.Contains(expected[..Math.Min(expected.Length, 10)], text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_replacement_that_is_pure_delimiters_still_produces_a_valid_document()
    {
        var pdf = Document("Replace TARGET please.");

        var (artifact, _) = Editor.ReplaceText(pdf, new EditTextRequest
        {
            Replacements = [new TextReplacement { Find = "TARGET", Replace = @"))))\\\\((((" }],
            FailOnNoMatch = false,
        });

        Assert.Equal(1, Inspector.Inspect(artifact.Content, "out.pdf").PageCount);
    }

    // ---- field names -----------------------------------------------------

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("name with spaces")]
    [InlineData("naïve-ünïcode")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("field(with)parens")]
    [InlineData("back\\slash")]
    public void Unusual_field_names_round_trip_or_are_rejected(string name)
    {
        var spec = new FormFieldSpec
        {
            Name = name,
            Type = FormFieldType.Text,
            Rect = new FieldRect(1, 50, 100, 180, 20),
        };

        var ex = Record.Exception(() =>
        {
            var withField = Forms.EditFields(Document(), new EditFormFieldsRequest { Add = [spec] });
            var read = Forms.ReadFields(withField.Content);

            // If it was accepted, it must come back exactly as given — a silently mangled field
            // name would break every later fill call against it.
            Assert.Contains(read, f => f.Name == name);
        });

        Assert.True(ex is null or PdfWerkException, $"Unexpected: {ex?.GetType().Name}: {ex?.Message}");
    }

    [Fact]
    public void A_dotted_field_name_is_rejected_with_an_explanation()
    {
        // '.' separates hierarchy levels in an AcroForm, so accepting it would create a field
        // that cannot be addressed by the name the caller thinks it has.
        var spec = new FormFieldSpec
        {
            Name = "parent.child",
            Type = FormFieldType.Text,
            Rect = new FieldRect(1, 50, 100, 180, 20),
        };

        var ex = Assert.Throws<PdfWerkException>(() =>
            Forms.EditFields(Document(), new EditFormFieldsRequest { Add = [spec] }));

        Assert.Contains("hierarchy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_absurdly_long_field_name_does_not_break_the_writer()
    {
        var spec = new FormFieldSpec
        {
            Name = new string('x', 5000),
            Type = FormFieldType.Text,
            Rect = new FieldRect(1, 50, 100, 180, 20),
        };

        var ex = Record.Exception(() => Forms.EditFields(Document(), new EditFormFieldsRequest { Add = [spec] }));
        Assert.True(ex is null or PdfWerkException, $"Unexpected: {ex?.GetType().Name}");
    }

    // ---- geometry --------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, 0)]              // zero size
    [InlineData(-10, -10, 100, 20)]       // negative origin
    [InlineData(10, 10, -100, -20)]       // negative size
    [InlineData(1e9, 1e9, 100, 20)]       // far off the page
    public void Degenerate_rectangles_are_rejected_or_clamped(double x, double y, double w, double h)
    {
        var spec = new FormFieldSpec
        {
            Name = "field",
            Type = FormFieldType.Text,
            Rect = new FieldRect(1, x, y, w, h),
        };

        var ex = Record.Exception(() =>
        {
            var result = Forms.EditFields(Document(), new EditFormFieldsRequest { Add = [spec] });
            Inspector.Inspect(result.Content, "out.pdf");     // must still parse
        });

        Assert.True(ex is null or PdfWerkException, $"Unexpected: {ex?.GetType().Name}");
    }

    [Fact]
    public void A_field_on_a_page_that_does_not_exist_is_rejected()
    {
        var spec = new FormFieldSpec
        {
            Name = "field",
            Type = FormFieldType.Text,
            Rect = new FieldRect(99, 50, 100, 180, 20),
        };

        var ex = Assert.Throws<PdfWerkException>(() =>
            Forms.EditFields(Document(), new EditFormFieldsRequest { Add = [spec] }));

        Assert.Contains("out of range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- unicode ---------------------------------------------------------

    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("José Müller-Škoda")]
    [InlineData("日本語のテキスト")]
    [InlineData("العربية")]
    [InlineData("emoji 🎉 here")]
    public void Non_ascii_values_survive_a_fill_round_trip(string value)
    {
        var withField = Forms.EditFields(Document(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "name",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 50, 100, 300, 22),
                },
            ],
        });

        var filled = Forms.FillFields(withField.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["name"] = value },
        });

        // Values are stored as UTF-16 when they leave Latin-1, so they must come back byte-exact.
        Assert.Equal(value, Forms.ReadFields(filled.Content).Single(f => f.Name == "name").Value);
    }

    [Fact]
    public void Unicode_content_renders_without_throwing()
    {
        var artifact = Composer.Create(new CreateFromTextRequest
        {
            Content = "# Ünïcôde\n\nJosé, Škoda, naïve — em-dashes and “smart quotes”.",
            Format = TextFormat.Markdown,
        });

        Assert.True(artifact.ByteCount > 500);
    }

    // ---- resource exhaustion --------------------------------------------

    [Fact]
    public void A_very_large_body_of_text_still_completes()
    {
        // Roughly 400 KB of prose: well past anything a person would paste, and a reasonable
        // proxy for the largest input the tier guards would let through.
        var big = string.Join("\n\n", Enumerable.Range(1, 4000).Select(i => $"Paragraph {i} with some ordinary filler text in it."));

        var artifact = Composer.Create(new CreateFromTextRequest { Content = big, Format = TextFormat.Plain });

        Assert.True(Inspector.Inspect(artifact.Content, "big.pdf").PageCount > 10);
    }

    [Fact]
    public void Pathological_markdown_does_not_hang_the_parser()
    {
        // Unclosed fences, ragged tables and stray delimiters are exactly what a fuzzer finds,
        // and each has its own early-exit path in the writer.
        var nasty = string.Join('\n',
            "```",
            "unclosed code fence",
            "| broken | table",
            "| --- |",
            "| only | one | extra | cell |",
            "#### #### ####",
            "**unclosed bold",
            "*  ",
            "> ",
            "[link](not-a-url)",
            "[unclosed](",
            new string('#', 40),
            new string('*', 200));

        var artifact = Composer.Create(new CreateFromTextRequest { Content = nasty, Format = TextFormat.Markdown });

        Assert.True(artifact.ByteCount > 200);
    }

    [Fact]
    public void Merging_the_same_document_many_times_stays_consistent()
    {
        var one = Document("Repeated content.");
        var inputs = Enumerable.Range(0, 25).Select(i => ($"copy{i}.pdf", one)).ToList();

        var merged = Merger.Merge(inputs, "merged.pdf");

        Assert.Equal(25, Inspector.Inspect(merged.Content, "merged.pdf").PageCount);
    }

    // ---- fill edge cases -------------------------------------------------

    [Fact]
    public void Filling_a_checkbox_with_nonsense_leaves_it_unchecked_rather_than_throwing()
    {
        var withField = Forms.EditFields(Document(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "agreed",
                    Type = FormFieldType.Checkbox,
                    Rect = new FieldRect(1, 50, 100, 16, 16),
                },
            ],
        });

        var filled = Forms.FillFields(withField.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["agreed"] = "banana" },
        });

        Assert.Null(Forms.ReadFields(filled.Content).Single(f => f.Name == "agreed").Value);
    }

    [Fact]
    public void An_empty_value_clears_a_field_rather_than_failing()
    {
        var withField = Forms.EditFields(Document(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "name",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 50, 100, 200, 22),
                    Value = "preset",
                },
            ],
        });

        var filled = Forms.FillFields(withField.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["name"] = string.Empty },
        });

        var value = Forms.ReadFields(filled.Content).Single(f => f.Name == "name").Value;
        Assert.True(string.IsNullOrEmpty(value), $"Expected the field to be cleared, got '{value}'.");
    }

    [Fact]
    public void Flattening_a_form_twice_is_not_an_error_the_second_time()
    {
        var withField = Forms.EditFields(Document(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "name",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 50, 100, 200, 22),
                },
            ],
        });

        var flattened = Forms.FillFields(withField.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["name"] = "Ada" },
            Flatten = true,
        });

        // The form is gone, so a second attempt must be a clean client error, not a crash.
        var ex = Assert.Throws<PdfWerkException>(() => Forms.FillFields(flattened.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["name"] = "Grace" },
            StrictFieldNames = false,
        }));

        Assert.Equal(400, ex.StatusCode);
    }
}
