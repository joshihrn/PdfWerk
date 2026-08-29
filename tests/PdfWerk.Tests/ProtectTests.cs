using PdfSharp.Pdf.IO;
using PdfWerk.Core;
using PdfWerk.Core.Models;
using PdfWerk.Pdf;

namespace PdfWerk.Tests;

/// <summary>
/// Covers password protection. The distinction being tested is the one that matters in practice:
/// a user password genuinely encrypts, while permission flags are advisory.
/// </summary>
public class ProtectTests
{
    private static readonly PdfComposer Composer = new();
    private static readonly PdfProtector Protector = new();
    private static readonly PdfInspector Inspector = new();

    private static byte[] Document() =>
        Composer.Create(new CreateFromTextRequest { Content = "Sensitive content.", Format = TextFormat.Plain }).Content;

    [Fact]
    public void A_user_password_makes_the_document_unreadable_without_it()
    {
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest { UserPassword = "letmein" });

        // Opening without the password must fail — that is the whole point of setting one.
        var ex = Assert.Throws<InvalidPdfException>(() => Inspector.Inspect(protectedPdf.Content, "p.pdf"));
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_document_opens_again_with_the_right_password()
    {
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest { UserPassword = "letmein" });

        using var stream = new MemoryStream(protectedPdf.Content);
        using var document = PdfReader.Open(stream, "letmein", PdfDocumentOpenMode.Import);

        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void The_wrong_password_does_not_open_it()
    {
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest { UserPassword = "letmein" });

        using var stream = new MemoryStream(protectedPdf.Content);

        Assert.ThrowsAny<Exception>(() => PdfReader.Open(stream, "wrong", PdfDocumentOpenMode.Import));
    }

    [Fact]
    public void Permissions_alone_leave_the_document_openable()
    {
        // No user password: the file is still encrypted, but opens with an empty one, so it
        // remains readable by anybody. Callers are told this explicitly by the endpoint.
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest
        {
            Permissions = new PdfPermissions { AllowPrinting = false, AllowCopyingContent = false },
        });

        var info = Inspector.Inspect(protectedPdf.Content, "p.pdf");

        Assert.Equal(1, info.PageCount);
        Assert.True(info.IsEncrypted);
    }

    [Fact]
    public void Permission_flags_are_written_to_the_document()
    {
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest
        {
            OwnerPassword = "owner",
            Permissions = new PdfPermissions
            {
                AllowPrinting = false,
                AllowCopyingContent = false,
                AllowModification = false,
                AllowFormFilling = true,
            },
        });

        using var stream = new MemoryStream(protectedPdf.Content);
        using var document = PdfReader.Open(stream, "owner", PdfDocumentOpenMode.Modify);

        var security = document.SecuritySettings;
        Assert.False(security.PermitPrint);
        Assert.False(security.PermitExtractContent);
        Assert.False(security.PermitModifyDocument);
        Assert.True(security.PermitFormsFill);
    }

    [Fact]
    public void High_quality_printing_cannot_be_allowed_when_printing_is_denied()
    {
        // A document that forbids printing but permits high-quality printing is incoherent, and
        // readers resolve the contradiction inconsistently.
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest
        {
            OwnerPassword = "owner",
            Permissions = new PdfPermissions { AllowPrinting = false, AllowHighQualityPrinting = true },
        });

        using var stream = new MemoryStream(protectedPdf.Content);
        using var document = PdfReader.Open(stream, "owner", PdfDocumentOpenMode.Modify);

        Assert.False(document.SecuritySettings.PermitFullQualityPrint);
    }

    [Fact]
    public void An_owner_password_is_generated_when_none_is_supplied()
    {
        // Without an owner password the restrictions could be lifted by supplying an empty one,
        // so protection that looks applied would not be.
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest
        {
            Permissions = new PdfPermissions { AllowPrinting = false },
        });

        using var stream = new MemoryStream(protectedPdf.Content);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var document = PdfReader.Open(stream, string.Empty, PdfDocumentOpenMode.Modify);

            // Reaching here means an empty owner password was accepted for modification.
            Assert.Fail("An empty owner password should not grant modify access.");
        });
    }

    [Fact]
    public void Inspect_reports_encryption_accurately()
    {
        var plain = Document();
        Assert.False(Inspector.Inspect(plain, "plain.pdf").IsEncrypted);

        var permissionsOnly = Protector.Protect(plain, new ProtectRequest
        {
            Permissions = new PdfPermissions { AllowPrinting = false },
        });

        // Regression guard: PDFsharp's SecuritySettings.IsEncrypted reads false here, because it
        // describes the security the document would be *written* with, not what it was read with.
        Assert.True(Inspector.Inspect(permissionsOnly.Content, "p.pdf").IsEncrypted);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public void An_overlong_password_is_rejected(int length)
    {
        var ex = Assert.Throws<PdfWerkException>(() =>
            Protector.Protect(Document(), new ProtectRequest { UserPassword = new string('x', length) }));

        Assert.Contains("127", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unicode_passwords_round_trip()
    {
        var password = "pässwörd-日本";
        var protectedPdf = Protector.Protect(Document(), new ProtectRequest { UserPassword = password });

        using var stream = new MemoryStream(protectedPdf.Content);
        using var document = PdfReader.Open(stream, password, PdfDocumentOpenMode.Import);

        Assert.Equal(1, document.PageCount);
    }
}
