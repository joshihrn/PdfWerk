using System.Security.Cryptography;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf;

/// <summary>
/// Applies a password and permission flags.
/// </summary>
/// <remarks>
/// <para>
/// Worth being clear about what PDF permissions are and are not. The <em>user password</em> is
/// real: without it the content is encrypted and unreadable. The permission flags are not — they
/// are requests that conforming readers honour voluntarily, and any tool holding the owner
/// password, or simply choosing to ignore them, can disregard them entirely.
/// </para>
/// <para>
/// So permissions deter casual copying and printing; they do not protect a secret. The API says
/// as much in its response rather than letting a caller assume otherwise.
/// </para>
/// </remarks>
public sealed class PdfProtector : IPdfProtector
{
    public PdfArtifact Protect(byte[] pdf, ProtectRequest request)
    {
        var user = request.UserPassword ?? string.Empty;

        if (user.Length > 127)
            throw new PdfWerkException("Passwords are limited to 127 characters.");

        // An owner password is what makes the permission flags stick. Without one, any reader can
        // lift the restrictions by supplying an empty owner password — so one is generated rather
        // than leaving the caller with protection that is not there.
        var owner = string.IsNullOrEmpty(request.OwnerPassword)
            ? GenerateOwnerPassword()
            : request.OwnerPassword;

        if (owner.Length > 127)
            throw new PdfWerkException("Passwords are limited to 127 characters.");

        using var document = PdfGuard.Open(pdf);
        var security = document.SecuritySettings;

        if (user.Length > 0)
            security.UserPassword = user;

        security.OwnerPassword = owner;

        var permissions = request.Permissions;
        security.PermitPrint = permissions.AllowPrinting;
        security.PermitFullQualityPrint = permissions.AllowHighQualityPrinting && permissions.AllowPrinting;
        security.PermitModifyDocument = permissions.AllowModification;
        security.PermitExtractContent = permissions.AllowCopyingContent;
        security.PermitAnnotations = permissions.AllowAnnotations;
        security.PermitFormsFill = permissions.AllowFormFilling;
        security.PermitAssembleDocument = permissions.AllowAssembly;

        return new PdfArtifact(PdfGuard.Save(document), "protected.pdf");
    }

    /// <summary>A random owner password, for when the caller supplied none.</summary>
    private static string GenerateOwnerPassword()
    {
        // Never returned to the caller: its only job is to stop the permission flags being
        // removed by a reader that guesses the empty string.
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes);
    }
}
