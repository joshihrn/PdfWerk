using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfWerk.Pdf.Text;

/// <summary>
/// Translates between the byte codes a content stream shows and the characters they display.
/// </summary>
/// <remarks>
/// Modern producers embed subset fonts addressed by glyph id, so the bytes inside a Tj operand
/// bear no relation to the text on the page. The /ToUnicode CMap that accompanies such a font is
/// the only reliable bridge, and it is what text extraction and copy-paste use too. Building the
/// reverse direction as well is what makes editing possible: a replacement can only be written
/// back if every one of its characters exists in the embedded subset.
/// </remarks>
internal sealed class FontMap
{
    private readonly Dictionary<int, string> _toUnicode;
    private readonly Dictionary<string, int> _fromUnicode;

    /// <summary>1 for simple fonts, 2 for composite (Type0 / Identity-H) fonts.</summary>
    public int BytesPerCode { get; }

    /// <summary>True when no /ToUnicode was present and codes are assumed to be Latin-1.</summary>
    public bool IsIdentity { get; }

    private FontMap(Dictionary<int, string> toUnicode, int bytesPerCode, bool isIdentity)
    {
        _toUnicode = toUnicode;
        BytesPerCode = bytesPerCode;
        IsIdentity = isIdentity;

        _fromUnicode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, text) in toUnicode)
        {
            // A glyph may be reachable by more than one code; the lowest is the safest choice.
            if (!_fromUnicode.TryGetValue(text, out var existing) || code < existing)
                _fromUnicode[text] = code;
        }
    }

    /// <summary>The pass-through map for simple fonts whose codes are already the characters.</summary>
    public static FontMap Identity { get; } = new([], 1, isIdentity: true);

    /// <summary>Decodes an operand string into the text it displays.</summary>
    public string Decode(string raw)
    {
        if (IsIdentity)
            return raw;

        var sb = new StringBuilder(raw.Length);

        for (var i = 0; i + BytesPerCode - 1 < raw.Length; i += BytesPerCode)
        {
            var code = BytesPerCode == 2
                ? ((raw[i] & 0xFF) << 8) | (raw[i + 1] & 0xFF)
                : raw[i] & 0xFF;

            // An unmapped code is preserved as a private-use marker so that the round trip
            // through replacement does not silently drop glyphs we cannot name.
            sb.Append(_toUnicode.TryGetValue(code, out var text) ? text : Unmapped(code));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Re-encodes text into operand bytes, or returns null if the font cannot express it.
    /// </summary>
    public string? Encode(string text)
    {
        if (IsIdentity)
            return text.All(c => c <= 0xFF) ? text : null;

        var sb = new StringBuilder(text.Length * BytesPerCode);

        for (var i = 0; i < text.Length; i++)
        {
            int code;

            if (TryReadUnmapped(text, i, out var original, out var consumed))
            {
                code = original;
                i += consumed - 1;
            }
            else if (_fromUnicode.TryGetValue(text[i].ToString(), out var mapped))
            {
                code = mapped;
            }
            else
            {
                // The replacement needs a glyph the subset does not contain.
                return null;
            }

            if (BytesPerCode == 2)
            {
                sb.Append((char)((code >> 8) & 0xFF));
                sb.Append((char)(code & 0xFF));
            }
            else
            {
                sb.Append((char)(code & 0xFF));
            }
        }

        return sb.ToString();
    }

    // Unmapped codes ride through the edit as U+E000 + code, inside the private use area.
    private static string Unmapped(int code) => ((char)(0xE000 + (code & 0x1FFF))).ToString();

    private static bool TryReadUnmapped(string text, int index, out int code, out int consumed)
    {
        var c = text[index];
        if (c >= (char)0xE000 && c - 0xE000 <= 0x1FFF)
        {
            code = c - 0xE000;
            consumed = 1;
            return true;
        }

        code = 0;
        consumed = 0;
        return false;
    }

    /// <summary>Builds a map per font resource name for one page.</summary>
    public static Dictionary<string, FontMap> ForPage(PdfPage page)
    {
        var maps = new Dictionary<string, FontMap>(StringComparer.Ordinal);

        var fonts = page.Resources?.Elements.GetDictionary("/Font");
        if (fonts is null)
            return maps;

        foreach (var key in fonts.Elements.Keys)
        {
            var font = Resolve(fonts.Elements[key]);
            if (font is null)
                continue;

            maps[key] = Build(font);
        }

        return maps;
    }

    private static FontMap Build(PdfDictionary font)
    {
        var isComposite = font.Elements.GetName("/Subtype") == "/Type0";
        var bytesPerCode = isComposite ? 2 : 1;

        var toUnicodeStream = Resolve(font.Elements["/ToUnicode"]);
        if (toUnicodeStream?.Stream is null)
        {
            // Without a CMap a simple font's codes are its characters; a composite font
            // without one cannot be decoded at all, so it is left alone.
            return isComposite ? new FontMap([], 2, isIdentity: false) : Identity;
        }

        var stream = toUnicodeStream.Stream;
        if (stream.IsFiltered())
            stream.TryUncompress();

        var cmap = Encoding.Latin1.GetString(stream.Value ?? []);
        return new FontMap(CMapParser.Parse(cmap), bytesPerCode, isIdentity: false);
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference reference => reference.Value as PdfDictionary,
        PdfDictionary dictionary => dictionary,
        _ => null,
    };
}

/// <summary>
/// Reads the bfchar and bfrange sections of a /ToUnicode CMap.
/// </summary>
/// <remarks>
/// A CMap is a small PostScript program, but the mapping sections have a fixed shape and are all
/// that matters here, so this reads them directly rather than interpreting the language.
/// </remarks>
internal static class CMapParser
{
    public static Dictionary<int, string> Parse(string cmap)
    {
        var map = new Dictionary<int, string>();

        ParseChars(cmap, map);
        ParseRanges(cmap, map);

        return map;
    }

    /// <summary>beginbfchar: pairs of &lt;src&gt; &lt;dst&gt;.</summary>
    private static void ParseChars(string cmap, Dictionary<int, string> map)
    {
        foreach (var section in Sections(cmap, "beginbfchar", "endbfchar"))
        {
            var tokens = HexTokens(section);
            for (var i = 0; i + 1 < tokens.Count; i += 2)
            {
                var code = ToInt(tokens[i]);
                var text = ToText(tokens[i + 1]);
                if (code >= 0 && text.Length > 0)
                    map[code] = text;
            }
        }
    }

    /// <summary>beginbfrange: triples of &lt;lo&gt; &lt;hi&gt; &lt;dstStart&gt;, or an array form.</summary>
    private static void ParseRanges(string cmap, Dictionary<int, string> map)
    {
        foreach (var section in Sections(cmap, "beginbfrange", "endbfrange"))
        {
            var i = 0;
            while (i < section.Length)
            {
                var lo = NextHex(section, ref i);
                if (lo is null) break;

                var hi = NextHex(section, ref i);
                if (hi is null) break;

                SkipWhitespace(section, ref i);

                if (i < section.Length && section[i] == '[')
                {
                    // Array form: one destination per code in the range.
                    i++;
                    var code = ToInt(lo);
                    while (i < section.Length && section[i] != ']')
                    {
                        var item = NextHex(section, ref i);
                        if (item is null) break;

                        var text = ToText(item);
                        if (text.Length > 0)
                            map[code] = text;

                        code++;
                        SkipWhitespace(section, ref i);
                    }

                    if (i < section.Length) i++;     // step over ']'
                    continue;
                }

                var dst = NextHex(section, ref i);
                if (dst is null) break;

                var start = ToInt(lo);
                var end = ToInt(hi);
                var baseText = ToText(dst);

                if (start < 0 || end < start || baseText.Length == 0)
                    continue;

                // Ranges are bounded: a malformed CMap should not allocate unboundedly.
                end = Math.Min(end, start + 65535);

                for (var code = start; code <= end; code++)
                {
                    // Successive codes map to successive characters from the base value.
                    var shifted = baseText[^1] + (code - start);
                    map[code] = string.Concat(baseText[..^1], (char)shifted);
                }
            }
        }
    }

    private static IEnumerable<string> Sections(string text, string open, string close)
    {
        var cursor = 0;

        while (true)
        {
            var start = text.IndexOf(open, cursor, StringComparison.Ordinal);
            if (start < 0) yield break;

            var end = text.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0) yield break;

            yield return text[(start + open.Length)..end];
            cursor = end + close.Length;
        }
    }

    private static List<string> HexTokens(string section)
    {
        var tokens = new List<string>();
        var i = 0;

        while (true)
        {
            var token = NextHex(section, ref i);
            if (token is null) break;
            tokens.Add(token);
        }

        return tokens;
    }

    private static string? NextHex(string text, ref int i)
    {
        while (i < text.Length && text[i] != '<')
        {
            // Stop at an array boundary so the caller can handle the array form.
            if (text[i] == '[' || text[i] == ']')
                return null;

            i++;
        }

        if (i >= text.Length) return null;

        var end = text.IndexOf('>', i);
        if (end < 0) return null;

        var token = text[(i + 1)..end];
        i = end + 1;
        return token;
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
    }

    private static int ToInt(string hex) =>
        int.TryParse(hex.Trim(), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : -1;

    /// <summary>A destination is UTF-16BE, so it may decode to a surrogate pair or a ligature.</summary>
    private static string ToText(string hex)
    {
        var trimmed = hex.Trim();
        if (trimmed.Length % 4 != 0 || trimmed.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(trimmed.Length / 4);

        for (var i = 0; i + 3 < trimmed.Length; i += 4)
        {
            if (!int.TryParse(trimmed.AsSpan(i, 4), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var unit))
                return string.Empty;

            sb.Append((char)unit);
        }

        return sb.ToString();
    }
}
