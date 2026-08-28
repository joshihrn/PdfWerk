using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;

namespace PdfWerk.Tests;

/// <summary>
/// End-to-end checks over the PDF engine. Every test starts from a document the engine itself
/// produced, so a regression in composition shows up here rather than silently corrupting the
/// fixtures the other tests depend on.
/// </summary>
public class PdfEngineTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfFormService Forms = new();
    private static readonly PdfTextExtractor Extractor = new();
    private static readonly PdfMerger Merger = new();

    private static byte[] SamplePdf(string title = "Sample", string body = "Hello from PdfWerk.") =>
        Composer.Create(new CreateFromTextRequest
        {
            Content = body,
            Title = title,
            Format = TextFormat.Plain,
        }).Content;

    // ---- creation --------------------------------------------------------

    [Fact]
    public void Create_produces_a_readable_pdf()
    {
        var artifact = Composer.Create(new CreateFromTextRequest
        {
            Content = "# Quarterly Report\n\nRevenue grew **12%** this quarter.\n\n- North America\n- Europe\n",
            Title = "Quarterly Report",
            Author = "Finance Team",
            Format = TextFormat.Markdown,
        });

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(artifact.Content, 0, 5), StringComparison.Ordinal);
        Assert.Equal("quarterly-report.pdf", artifact.FileName);
        Assert.True(artifact.ByteCount > 500, $"Expected a real document, got {artifact.ByteCount} bytes.");

        var text = string.Join("\n", Extractor.ExtractPages(artifact.Content));
        Assert.Contains("Quarterly Report", text, StringComparison.Ordinal);
        Assert.Contains("Revenue grew", text, StringComparison.Ordinal);
        Assert.Contains("North America", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_renders_markdown_tables_and_code()
    {
        var artifact = Composer.Create(new CreateFromTextRequest
        {
            Content = "| Region | Sales |\n| --- | ---: |\n| EMEA | 120 |\n\n```\nvar x = 1;\n```\n",
            Format = TextFormat.Markdown,
        });

        var text = string.Join("\n", Extractor.ExtractPages(artifact.Content));
        Assert.Contains("EMEA", text, StringComparison.Ordinal);
        Assert.Contains("Region", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_empty_content()
    {
        var ex = Assert.Throws<PdfWerkException>(() =>
            Composer.Create(new CreateFromTextRequest { Content = "   " }));

        Assert.Equal(400, ex.StatusCode);
    }

    // ---- guards ----------------------------------------------------------

    [Fact]
    public void Reading_a_non_pdf_reports_a_client_error()
    {
        var junk = System.Text.Encoding.ASCII.GetBytes(new string('x', 500));

        var ex = Assert.Throws<InvalidPdfException>(() => Forms.ReadFields(junk));
        Assert.Equal(422, ex.StatusCode);
    }

    // ---- merge -----------------------------------------------------------

    [Fact]
    public void Merge_concatenates_page_counts()
    {
        var first = SamplePdf("First", "Document one.");
        var second = SamplePdf("Second", "Document two.");

        var merged = Merger.Merge([("a.pdf", first), ("b.pdf", second)], "combined.pdf");

        Assert.Equal("combined.pdf", merged.FileName);

        var pages = Extractor.ExtractPages(merged.Content);
        Assert.Equal(2, pages.Count);
        Assert.Contains("Document one", pages[0], StringComparison.Ordinal);
        Assert.Contains("Document two", pages[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_names_the_file_that_failed()
    {
        var good = SamplePdf();
        var bad = System.Text.Encoding.ASCII.GetBytes(new string('x', 500));

        var ex = Assert.Throws<InvalidPdfException>(() =>
            Merger.Merge([("good.pdf", good), ("broken.pdf", bad)], "out.pdf"));

        Assert.Contains("broken.pdf", ex.Message, StringComparison.Ordinal);
    }

    // ---- form authoring --------------------------------------------------

    private static readonly FormFieldSpec[] Fields =
    [
        new()
        {
            Name = "fullName",
            Type = FormFieldType.Text,
            Rect = new FieldRect(1, 72, 200, 220, 22),
            ToolTip = "Your full legal name",
            Required = true,
        },
        new()
        {
            Name = "agreed",
            Type = FormFieldType.Checkbox,
            Rect = new FieldRect(1, 72, 240, 16, 16),
        },
        new()
        {
            Name = "plan",
            Type = FormFieldType.Dropdown,
            Rect = new FieldRect(1, 72, 280, 160, 20),
            Options = ["Monthly", "Annual"],
        },
    ];

    [Fact]
    public void Add_fields_then_read_them_back()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var read = Forms.ReadFields(withFields.Content);

        Assert.Equal(3, read.Count);

        var name = read.Single(f => f.Name == "fullName");
        Assert.Equal(FormFieldType.Text, name.Type);
        Assert.NotNull(name.Rect);
        Assert.Equal(1, name.Rect!.Page);

        // The designer sends top-left coordinates; they must survive the round trip through
        // PDF user space to within rounding.
        Assert.Equal(72, name.Rect.X, 1);
        Assert.Equal(200, name.Rect.Y, 1);
        Assert.Equal(220, name.Rect.Width, 1);
        Assert.Equal(22, name.Rect.Height, 1);

        Assert.Equal(FormFieldType.Checkbox, read.Single(f => f.Name == "agreed").Type);

        var plan = read.Single(f => f.Name == "plan");
        Assert.Equal(FormFieldType.Dropdown, plan.Type);
        Assert.Equal(["Monthly", "Annual"], plan.Options);
    }

    [Fact]
    public void Adding_a_duplicate_name_is_rejected_unless_replacing()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var ex = Assert.Throws<PdfWerkException>(() =>
            Forms.EditFields(withFields.Content, new EditFormFieldsRequest { Add = [Fields[0]] }));
        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);

        var replaced = Forms.EditFields(withFields.Content,
            new EditFormFieldsRequest { Add = [Fields[0]], Replace = true });

        Assert.Equal(3, Forms.ReadFields(replaced.Content).Count);
    }

    [Fact]
    public void Remove_deletes_the_field_and_its_widget()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var trimmed = Forms.EditFields(withFields.Content,
            new EditFormFieldsRequest { Remove = ["agreed"] });

        var read = Forms.ReadFields(trimmed.Content);
        Assert.Equal(2, read.Count);
        Assert.DoesNotContain(read, f => f.Name == "agreed");
    }

    [Fact]
    public void Radio_groups_create_one_widget_per_option()
    {
        var spec = new FormFieldSpec
        {
            Name = "billing",
            Type = FormFieldType.RadioGroup,
            Rect = new FieldRect(1, 72, 320, 120, 16),
            Options = ["Card", "Invoice"],
        };

        var withRadio = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = [spec] });

        var field = Forms.ReadFields(withRadio.Content).Single(f => f.Name == "billing");
        Assert.Equal(FormFieldType.RadioGroup, field.Type);

        var filled = Forms.FillFields(withRadio.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["billing"] = "Invoice" },
        });

        Assert.Equal("Invoice", Forms.ReadFields(filled.Content).Single(f => f.Name == "billing").Value);
    }

    // ---- filling ---------------------------------------------------------

    [Fact]
    public void Fill_writes_values_that_read_back()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var filled = Forms.FillFields(withFields.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string>
            {
                ["fullName"] = "Ada Lovelace",
                ["agreed"] = "true",
                ["plan"] = "Annual",
            },
        });

        var read = Forms.ReadFields(filled.Content);
        Assert.Equal("Ada Lovelace", read.Single(f => f.Name == "fullName").Value);
        Assert.Equal("Annual", read.Single(f => f.Name == "plan").Value);
        Assert.Equal("Yes", read.Single(f => f.Name == "agreed").Value);
    }

    [Fact]
    public void Fill_rejects_unknown_field_names_in_strict_mode()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var ex = Assert.Throws<PdfWerkException>(() => Forms.FillFields(withFields.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["nope"] = "x" },
        }));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fill_ignores_unknown_names_when_not_strict()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var filled = Forms.FillFields(withFields.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["nope"] = "x", ["fullName"] = "Grace" },
            StrictFieldNames = false,
        });

        Assert.Equal("Grace", Forms.ReadFields(filled.Content).Single(f => f.Name == "fullName").Value);
    }

    [Fact]
    public void Flatten_removes_the_form_and_paints_the_value()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest { Add = Fields });

        var flattened = Forms.FillFields(withFields.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["fullName"] = "Ada Lovelace" },
            StrictFieldNames = false,
            Flatten = true,
        });

        Assert.Empty(Forms.ReadFields(flattened.Content));

        // The value must survive as page content, not just as a form value.
        var text = string.Join("\n", Extractor.ExtractPages(flattened.Content));
        Assert.Contains("Ada Lovelace", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Filling_a_document_with_no_form_is_a_client_error()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Forms.FillFields(SamplePdf(), new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["x"] = "y" },
        }));

        Assert.Contains("no form fields", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
