namespace PdfWerk.Pdf.Internal;

/// <summary>
/// Determines whether a file is encrypted, by looking at the file rather than asking PDFsharp.
/// </summary>
/// <remarks>
/// PdfDocument.SecuritySettings.IsEncrypted describes the security the document will be written
/// with, not the security it was read with. Once a file has been opened and decrypted, the
/// in-memory document has no pending encryption, so the property reads false — including for
/// files that plainly are encrypted. Using it made /v1/inspect report "not encrypted" for every
/// document it could open.
///
/// The trailer of an encrypted file references an encryption dictionary as "/Encrypt n g R".
/// Matching that shape, rather than the bare word, keeps the check specific enough that a chance
/// byte sequence inside a compressed stream will not trigger it.
/// </remarks>
internal static class EncryptionProbe
{
    private static readonly byte[] Marker = "/Encrypt"u8.ToArray();

    public static bool IsEncrypted(byte[] content)
    {
        for (var i = 0; i + Marker.Length < content.Length; i++)
        {
            if (!content.AsSpan(i, Marker.Length).SequenceEqual(Marker))
                continue;

            if (LooksLikeReference(content, i + Marker.Length))
                return true;
        }

        return false;
    }

    /// <summary>Matches the " n g R" that follows /Encrypt in a trailer.</summary>
    private static bool LooksLikeReference(byte[] content, int start)
    {
        var i = start;

        if (!SkipWhitespace(content, ref i) || !SkipDigits(content, ref i)) return false;
        if (!SkipWhitespace(content, ref i) || !SkipDigits(content, ref i)) return false;
        if (!SkipWhitespace(content, ref i)) return false;

        return i < content.Length && content[i] == (byte)'R';
    }

    private static bool SkipWhitespace(byte[] content, ref int i)
    {
        var start = i;
        while (i < content.Length && content[i] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
            i++;

        return i > start;
    }

    private static bool SkipDigits(byte[] content, ref int i)
    {
        var start = i;
        while (i < content.Length && content[i] is >= (byte)'0' and <= (byte)'9')
            i++;

        return i > start;
    }
}
