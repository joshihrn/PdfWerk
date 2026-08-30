using System.Text;
using System.Text.RegularExpressions;
using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// Covers default values and fields that appear in more than one place.
/// </summary>
/// <remarks>
/// Assertions go against the saved bytes, because both features live in structure the reader does
/// not surface: <c>ExistingFormField</c> reports the current value but not the default, and it
/// reports a shared field once regardless of how many boxes it actually has. A test written
/// against the reader would pass whether or not either feature worked.
/// </remarks>
public class FormDefaultsAndSharedNamesTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfFormService Forms = new();

    private static byte[] SamplePdf() =>
        Composer.Create(new CreateFromTextRequest
        {
            // Long enough to overflow onto a second page. A form feed does not paginate
            // here, and on a one-page document every cross-page rectangle is out of range.
            Content = string.Join("\n\n", Enumerable.Repeat("Clause of the contract.", 90)),
            Title = "Contract",
            Format = TextFormat.Plain,
        }).Content;

    private static string Raw(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int Count(byte[] pdf, string pattern) =>
        Regex.Matches(Raw(pdf), pattern).Count;

    // ---- default values --------------------------------------------------

    [Theory]
    [InlineData(FormFieldType.Text, "Ada Lovelace")]
    [InlineData(FormFieldType.Dropdown, "Annual")]
    [InlineData(FormFieldType.ListBox, "Annual")]
    public void A_value_is_written_as_both_the_value_and_the_default(FormFieldType type, string value)
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "plan",
                    Type = type,
                    Rect = new FieldRect(1, 72, 200, 180, 22),
                    Value = value,
                    Options = type == FormFieldType.Text ? [] : ["Monthly", "Annual"],
                },
            ],
        });

        // /DV as well as /V. Without the default, a viewer's "reset form" empties the field
        // instead of returning it to what the document shipped with.
        Assert.True(Count(result.Content, @"/DV") >= 1, "the field carries no /DV");
        Assert.Contains(value, Raw(result.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void A_checked_checkbox_defaults_to_checked()
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "agreed",
                    Type = FormFieldType.Checkbox,
                    Rect = new FieldRect(1, 72, 240, 16, 16),
                    Value = "true",
                },
            ],
        });

        Assert.Matches(@"/DV\s*/Yes", Raw(result.Content));
    }

    [Fact]
    public void A_field_with_no_value_carries_no_text_default()
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "notes",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 72, 200, 180, 22),
                },
            ],
        });

        // An empty string is not a default. Writing /DV () would make "reset" put an empty
        // value in rather than leaving the field genuinely unset.
        Assert.DoesNotContain("/DV", Raw(result.Content), StringComparison.Ordinal);
    }

    // ---- shared names ----------------------------------------------------

    [Fact]
    public void A_name_used_twice_becomes_one_field_with_two_widgets()
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 72, 200, 220, 22),
                    Value = "Ada Lovelace",
                },
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(2, 72, 400, 220, 22),
                },
            ],
        });

        var fields = Forms.ReadFields(result.Content);

        // One field, not two: typing in either box fills both, which is the point.
        Assert.Single(fields);
        Assert.Equal("signerName", fields[0].Name);

        var raw = Raw(result.Content);

        // The name is written once, on the parent, and the boxes hang off it as kids.
        Assert.Single(Regex.Matches(raw, @"/T\s*\(signerName\)"));
        // Anchored to the field parent rather than a bare "/Kids": the page tree has one too.
        Assert.Contains("/Parent", raw, StringComparison.Ordinal);

        // Two widget annotations, one per page.
        Assert.Equal(2, Regex.Matches(raw, @"/Subtype\s*/Widget").Count);
    }

    [Fact]
    public void A_shared_field_keeps_its_value_on_the_parent()
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 72, 200, 220, 22),
                    Value = "Ada Lovelace",
                },
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(2, 72, 400, 220, 22),
                },
            ],
        });

        var field = Forms.ReadFields(result.Content).Single();

        Assert.Equal("Ada Lovelace", field.Value);
    }

    [Fact]
    public void Boxes_sharing_a_name_must_agree_on_their_type()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Forms.EditFields(SamplePdf(),
            new EditFormFieldsRequest
            {
                Add =
                [
                    new FormFieldSpec
                    {
                        Name = "agreed",
                        Type = FormFieldType.Text,
                        Rect = new FieldRect(1, 72, 200, 180, 22),
                    },
                    new FormFieldSpec
                    {
                        Name = "agreed",
                        Type = FormFieldType.Checkbox,
                        Rect = new FieldRect(1, 72, 240, 16, 16),
                    },
                ],
            }));

        Assert.Contains("same type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_repeated_radio_group_is_refused_with_an_explanation()
    {
        var ex = Assert.Throws<PdfWerkException>(() => Forms.EditFields(SamplePdf(),
            new EditFormFieldsRequest
            {
                Add =
                [
                    new FormFieldSpec
                    {
                        Name = "plan",
                        Type = FormFieldType.RadioGroup,
                        Rect = new FieldRect(1, 72, 200, 180, 22),
                        Options = ["Monthly", "Annual"],
                    },
                    new FormFieldSpec
                    {
                        Name = "plan",
                        Type = FormFieldType.RadioGroup,
                        Rect = new FieldRect(2, 72, 200, 180, 22),
                        Options = ["Monthly", "Annual"],
                    },
                ],
            }));

        // A radio group is already a parent with kids; nesting one inside another is not a
        // structure viewers handle, so the message says what to do instead.
        Assert.Contains("one rectangle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_single_use_of_a_name_still_produces_a_plain_field()
    {
        var result = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "fullName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 72, 200, 220, 22),
                },
            ],
        });

        // The common case must not grow a parent and a kid for no reason: one widget, and
        // it carries the name itself rather than pointing at a parent that holds it.
        Assert.Single(Regex.Matches(Raw(result.Content), @"/Subtype\s*/Widget"));
        Assert.Single(Regex.Matches(Raw(result.Content), @"/T\s*\(fullName\)"));
        Assert.Single(Forms.ReadFields(result.Content));
    }

    [Fact]
    public void A_shared_field_can_still_be_filled_and_flattened()
    {
        var withFields = Forms.EditFields(SamplePdf(), new EditFormFieldsRequest
        {
            Add =
            [
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(1, 72, 200, 220, 22),
                },
                new FormFieldSpec
                {
                    Name = "signerName",
                    Type = FormFieldType.Text,
                    Rect = new FieldRect(2, 72, 400, 220, 22),
                },
            ],
        });

        var filled = Forms.FillFields(withFields.Content, new FillFormRequest
        {
            Values = new Dictionary<string, string> { ["signerName"] = "Grace Hopper" },
        });

        Assert.Equal("Grace Hopper", Forms.ReadFields(filled.Content).Single().Value);
    }
}
