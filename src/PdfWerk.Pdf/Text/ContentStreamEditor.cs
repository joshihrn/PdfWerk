using System.Text;

namespace PdfWerk.Pdf.Text;

/// <summary>
/// Rewrites the strings behind a content stream's text-showing operators.
/// </summary>
/// <remarks>
/// <para>
/// Operators, positioning and resources are spliced through byte-for-byte, so an edited document
/// looks exactly like the original except for the words that changed — and, critically, the old
/// text is genuinely gone rather than merely covered up.
/// </para>
/// <para>
/// Strings are decoded through the showing font's <see cref="FontMap"/>, so this works for both
/// simple fonts, whose bytes are their characters, and the embedded subsets that address glyphs
/// by id. A replacement is only written when the font can express every character it needs; a
/// subset missing a glyph leaves that occurrence untouched and uncounted rather than producing
/// blanks on the page.
/// </para>
/// </remarks>
internal static class ContentStreamEditor
{
    /// <summary>One text-showing operand group, its byte span, and the font in effect.</summary>
    private sealed record TextGroup(int Start, int End, IReadOnlyList<Token> Strings, bool IsArray, FontMap Font);

    private sealed record Token(int Start, int End, TokenKind Kind, string Text);

    private enum TokenKind { LiteralString, HexString, ArrayOpen, ArrayClose, Operator, Name, Other }

    /// <summary>
    /// Applies the replacements to a decoded content stream.
    /// </summary>
    /// <param name="fonts">Font resource name (including the leading slash) to its map.</param>
    /// <returns>The rewritten bytes, and how many replacements landed.</returns>
    public static (byte[] Content, int Count) Apply(
        byte[] content,
        IReadOnlyList<(string Find, string Replace, bool MatchCase)> replacements,
        IReadOnlyDictionary<string, FontMap> fonts)
    {
        var tokens = Tokenize(content);
        var groups = GroupTextOperands(tokens, fonts);
        if (groups.Count == 0)
            return (content, 0);

        // Working text per group, decoded out of the font's encoding.
        var texts = groups
            .Select(g => g.Font.Decode(string.Concat(g.Strings.Select(s => s.Text))))
            .ToList();

        var original = texts.ToList();
        var count = 0;

        foreach (var (find, replace, matchCase) in replacements)
        {
            if (find.Length > 0)
                count += ApplyOne(groups, texts, find, replace, matchCase);
        }

        if (count == 0)
            return (content, 0);

        var edits = new List<(int Start, int End, byte[] Replacement)>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (string.Equals(texts[i], original[i], StringComparison.Ordinal))
                continue;

            var encoded = groups[i].Font.Encode(texts[i]);
            if (encoded is null)
                continue;   // guarded before the edit was accepted, so this should not happen

            // The span collapses to a single string: the original kerning offsets no longer
            // line up with the new text, and keeping them would space it wrongly.
            var literal = EscapeLiteral(encoded);
            var serialized = Encoding.Latin1.GetBytes(groups[i].IsArray ? $"[{literal}]" : literal);

            edits.Add((groups[i].Start, groups[i].End, serialized));
        }

        return edits.Count == 0 ? (content, 0) : (Splice(content, edits), count);
    }

    /// <summary>
    /// Applies one instruction across the page's text, rewriting the affected groups in place.
    /// </summary>
    /// <remarks>
    /// A phrase is very often split across several show-text operators, because producers
    /// position each word with its own Td rather than emitting space glyphs — "Acme Corporation"
    /// arrives as <c>(Acme) Tj … (Corporation) Tj</c>. Matching one operand at a time would
    /// therefore never find a multi-word phrase, so the search runs over the concatenation of
    /// every group with an implied separator at each boundary.
    /// </remarks>
    private static int ApplyOne(
        IReadOnlyList<TextGroup> groups,
        List<string> texts,
        string find,
        string replace,
        bool matchCase)
    {
        var (flat, owner) = Flatten(texts);

        var matches = new List<(int Start, int End)>();
        var cursor = 0;

        while (cursor < flat.Length)
        {
            var end = TryMatchAt(flat, cursor, find, matchCase);
            if (end < 0)
            {
                cursor++;
                continue;
            }

            matches.Add((cursor, end));
            cursor = end;
        }

        if (matches.Count == 0)
            return 0;

        var applied = 0;

        // Right to left, so indices into the unmodified flat text stay valid as edits land.
        for (var m = matches.Count - 1; m >= 0; m--)
        {
            var (start, end) = matches[m];

            var first = FirstOwned(owner, start, end);
            var last = LastOwned(owner, start, end);
            if (first is null || last is null)
                continue;

            var (firstGroup, firstOffset) = first.Value;
            var (lastGroup, lastOffset) = last.Value;

            // Encoding a span that changes font midway is not expressible as one string.
            if (!SameFont(groups, firstGroup, lastGroup))
                continue;

            var head = texts[firstGroup][..firstOffset];
            var tail = texts[lastGroup][(lastOffset + 1)..];

            var rewritten = firstGroup == lastGroup
                ? head + replace + tail
                : head + replace;

            // Reject before mutating: a subset font may not carry the replacement's glyphs.
            if (groups[firstGroup].Font.Encode(rewritten) is null)
                continue;

            texts[firstGroup] = rewritten;

            // The remainder of the match is consumed from the groups that follow.
            for (var g = firstGroup + 1; g < lastGroup; g++)
                texts[g] = string.Empty;

            if (lastGroup != firstGroup)
                texts[lastGroup] = tail;

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Concatenates the groups, recording which group and offset each character came from.
    /// Boundaries between groups become spaces owned by nobody.
    /// </summary>
    private static (string Flat, List<(int Group, int Offset)?> Owner) Flatten(List<string> texts)
    {
        var sb = new StringBuilder();
        var owner = new List<(int Group, int Offset)?>();

        for (var g = 0; g < texts.Count; g++)
        {
            if (g > 0)
            {
                sb.Append(' ');
                owner.Add(null);
            }

            for (var i = 0; i < texts[g].Length; i++)
            {
                sb.Append(texts[g][i]);
                owner.Add((g, i));
            }
        }

        return (sb.ToString(), owner);
    }

    /// <summary>
    /// Matches <paramref name="find"/> at a position, treating the implied separator between
    /// groups as optional so that both "Acme Corporation" and "AcmeCorporation" are found.
    /// </summary>
    /// <returns>The exclusive end index, or -1 when there is no match here.</returns>
    private static int TryMatchAt(string flat, int start, string find, bool matchCase)
    {
        var i = start;
        var j = 0;

        while (j < find.Length)
        {
            if (i >= flat.Length)
                return -1;

            if (Same(flat[i], find[j], matchCase))
            {
                i++;
                j++;
                continue;
            }

            // A separator the search text does not account for is skipped, but only mid-match:
            // allowing it at the start would let every match drift rightwards.
            if (flat[i] == ' ' && i > start)
            {
                i++;
                continue;
            }

            return -1;
        }

        return i;
    }

    private static bool Same(char a, char b, bool matchCase) =>
        matchCase ? a == b : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    private static (int Group, int Offset)? FirstOwned(List<(int Group, int Offset)?> owner, int start, int end)
    {
        for (var i = start; i < end && i < owner.Count; i++)
        {
            if (owner[i] is { } hit)
                return hit;
        }

        return null;
    }

    private static (int Group, int Offset)? LastOwned(List<(int Group, int Offset)?> owner, int start, int end)
    {
        for (var i = Math.Min(end, owner.Count) - 1; i >= start; i--)
        {
            if (owner[i] is { } hit)
                return hit;
        }

        return null;
    }

    private static bool SameFont(IReadOnlyList<TextGroup> groups, int first, int last)
    {
        for (var g = first + 1; g <= last; g++)
        {
            if (!ReferenceEquals(groups[g].Font, groups[first].Font))
                return false;
        }

        return true;
    }

    /// <summary>Rebuilds the stream with each edited span swapped in, leaving the rest untouched.</summary>
    private static byte[] Splice(byte[] content, List<(int Start, int End, byte[] Replacement)> edits)
    {
        edits.Sort((a, b) => a.Start.CompareTo(b.Start));

        using var output = new MemoryStream(content.Length);
        var cursor = 0;

        foreach (var (start, end, replacement) in edits)
        {
            if (start < cursor)
                continue;           // overlapping edit; should not happen, but never corrupt

            output.Write(content, cursor, start - cursor);
            output.Write(replacement, 0, replacement.Length);
            cursor = end;
        }

        output.Write(content, cursor, content.Length - cursor);
        return output.ToArray();
    }

    // ---- tokenizing ------------------------------------------------------

    private static List<Token> Tokenize(byte[] content)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < content.Length)
        {
            var c = (char)content[i];

            if (IsWhitespace(c))
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '%':
                    while (i < content.Length && content[i] is not ((byte)'\r' or (byte)'\n')) i++;
                    continue;

                case '(':
                    i = ReadLiteralString(content, i, tokens);
                    continue;

                case '/':
                    i = ReadName(content, i, tokens);
                    continue;

                case '<' when i + 1 < content.Length && content[i + 1] != (byte)'<':
                    i = ReadHexString(content, i, tokens);
                    continue;

                case '<':
                    tokens.Add(new Token(i, i + 2, TokenKind.Other, "<<"));
                    i += 2;
                    continue;

                case '>' when i + 1 < content.Length && content[i + 1] == (byte)'>':
                    tokens.Add(new Token(i, i + 2, TokenKind.Other, ">>"));
                    i += 2;
                    continue;

                case '[':
                    tokens.Add(new Token(i, i + 1, TokenKind.ArrayOpen, "["));
                    i++;
                    continue;

                case ']':
                    tokens.Add(new Token(i, i + 1, TokenKind.ArrayClose, "]"));
                    i++;
                    continue;
            }

            // A bare run of regular characters: an operator or a number.
            var start = i;
            while (i < content.Length && !IsDelimiter((char)content[i]))
                i++;

            if (i == start)
            {
                i++;                // an unexpected delimiter; step over it rather than spin
                continue;
            }

            var text = Encoding.Latin1.GetString(content, start, i - start);
            var kind = char.IsAsciiLetter(text[0]) || text[0] is '\'' or '"' ? TokenKind.Operator : TokenKind.Other;
            tokens.Add(new Token(start, i, kind, text));

            // Inline image data is raw binary and must not be tokenized.
            if (kind == TokenKind.Operator && text == "ID")
                i = SkipInlineImage(content, i);
        }

        return tokens;
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\r' or '\n' or '\t' or '\f' or '\0';

    private static bool IsDelimiter(char c) =>
        IsWhitespace(c) || c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    private static int ReadName(byte[] content, int start, List<Token> tokens)
    {
        var i = start + 1;
        while (i < content.Length && !IsDelimiter((char)content[i]))
            i++;

        tokens.Add(new Token(start, i, TokenKind.Name, Encoding.Latin1.GetString(content, start, i - start)));
        return i;
    }

    private static int ReadLiteralString(byte[] content, int start, List<Token> tokens)
    {
        var sb = new StringBuilder();
        var depth = 0;
        var i = start;

        while (i < content.Length)
        {
            var c = (char)content[i];

            if (c == '\\' && i + 1 < content.Length)
            {
                var (decoded, consumed) = DecodeEscape(content, i);
                if (decoded.HasValue)
                    sb.Append(decoded.Value);
                i += consumed;
                continue;
            }

            if (c == '(')
            {
                depth++;
                if (depth > 1) sb.Append(c);
                i++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    i++;
                    break;
                }

                sb.Append(c);
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        tokens.Add(new Token(start, i, TokenKind.LiteralString, sb.ToString()));
        return i;
    }

    /// <summary>Decodes one backslash escape, returning the character and bytes consumed.</summary>
    private static (char? Value, int Consumed) DecodeEscape(byte[] content, int i)
    {
        var next = (char)content[i + 1];

        switch (next)
        {
            case 'n': return ('\n', 2);
            case 'r': return ('\r', 2);
            case 't': return ('\t', 2);
            case 'b': return ('\b', 2);
            case 'f': return ('\f', 2);
            case '(': return ('(', 2);
            case ')': return (')', 2);
            case '\\': return ('\\', 2);

            // A backslash before an end-of-line is a line continuation: it yields nothing.
            case '\r':
                return (null, i + 2 < content.Length && content[i + 2] == (byte)'\n' ? 3 : 2);
            case '\n':
                return (null, 2);
        }

        if (next is >= '0' and <= '7')
        {
            var value = 0;
            var digits = 0;
            while (digits < 3 && i + 1 + digits < content.Length)
            {
                var d = (char)content[i + 1 + digits];
                if (d is < '0' or > '7')
                    break;

                value = (value * 8) + (d - '0');
                digits++;
            }

            return ((char)(value & 0xFF), 1 + digits);
        }

        // An unrecognised escape stands for the character itself.
        return (next, 2);
    }

    private static int ReadHexString(byte[] content, int start, List<Token> tokens)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        var digits = new StringBuilder(2);

        while (i < content.Length && content[i] != (byte)'>')
        {
            var c = (char)content[i];
            if (Uri.IsHexDigit(c))
            {
                digits.Append(c);
                if (digits.Length == 2)
                {
                    sb.Append((char)Convert.ToInt32(digits.ToString(), 16));
                    digits.Clear();
                }
            }

            i++;
        }

        // An odd trailing digit is padded with zero, per ISO 32000-1 §7.3.4.3.
        if (digits.Length == 1)
            sb.Append((char)Convert.ToInt32(digits.ToString() + "0", 16));

        if (i < content.Length)
            i++;

        tokens.Add(new Token(start, i, TokenKind.HexString, sb.ToString()));
        return i;
    }

    private static int SkipInlineImage(byte[] content, int afterId)
    {
        // Data runs until an EI delimited by whitespace on both sides.
        for (var i = afterId + 1; i + 2 < content.Length; i++)
        {
            if (content[i] != (byte)'E' || content[i + 1] != (byte)'I')
                continue;

            var beforeOk = IsWhitespace((char)content[i - 1]);
            var afterOk = i + 2 >= content.Length || IsDelimiter((char)content[i + 2]);

            if (beforeOk && afterOk)
                return i + 2;
        }

        return content.Length;
    }

    // ---- grouping --------------------------------------------------------

    /// <summary>
    /// Finds the operand spans belonging to Tj, TJ, ' and " operators, tracking the font
    /// selected by the most recent Tf so each group knows how to decode itself.
    /// </summary>
    private static List<TextGroup> GroupTextOperands(List<Token> tokens, IReadOnlyDictionary<string, FontMap> fonts)
    {
        var groups = new List<TextGroup>();
        var current = FontMap.Identity;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.Operator)
                continue;

            switch (tokens[i].Text)
            {
                case "Tf":
                {
                    // Operands are "/Name size Tf".
                    var name = i >= 2 && tokens[i - 2].Kind == TokenKind.Name ? tokens[i - 2].Text : null;
                    current = name is not null && fonts.TryGetValue(name, out var map) ? map : FontMap.Identity;
                    break;
                }

                case "Tj":
                case "'":
                case "\"":
                {
                    var operand = PreviousString(tokens, i);
                    if (operand is not null)
                        groups.Add(new TextGroup(operand.Start, operand.End, [operand], IsArray: false, current));

                    break;
                }

                case "TJ":
                {
                    var close = PreviousIndexOf(tokens, i, TokenKind.ArrayClose);
                    if (close < 0)
                        break;

                    var open = MatchingOpen(tokens, close);
                    if (open < 0)
                        break;

                    var strings = tokens
                        .Skip(open + 1)
                        .Take(close - open - 1)
                        .Where(t => t.Kind is TokenKind.LiteralString or TokenKind.HexString)
                        .ToList();

                    if (strings.Count > 0)
                        groups.Add(new TextGroup(tokens[open].Start, tokens[close].End, strings, IsArray: true, current));

                    break;
                }
            }
        }

        return groups;
    }

    private static Token? PreviousString(List<Token> tokens, int from)
    {
        for (var i = from - 1; i >= 0 && i >= from - 4; i--)
        {
            if (tokens[i].Kind is TokenKind.LiteralString or TokenKind.HexString)
                return tokens[i];
        }

        return null;
    }

    private static int PreviousIndexOf(List<Token> tokens, int from, TokenKind kind)
    {
        for (var i = from - 1; i >= 0 && i >= from - 3; i--)
        {
            if (tokens[i].Kind == kind)
                return i;
        }

        return -1;
    }

    private static int MatchingOpen(List<Token> tokens, int close)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            if (tokens[i].Kind == TokenKind.ArrayClose) depth++;
            else if (tokens[i].Kind == TokenKind.ArrayOpen && --depth == 0) return i;
        }

        return -1;
    }

    // ---- string helpers --------------------------------------------------

    private static int CountOccurrences(string haystack, string needle, StringComparison comparison)
    {
        var count = 0;
        var index = 0;

        while (index <= haystack.Length - needle.Length &&
               (index = haystack.IndexOf(needle, index, comparison)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ReplaceAll(string haystack, string needle, string replacement, StringComparison comparison)
    {
        var sb = new StringBuilder(haystack.Length);
        var cursor = 0;

        while (cursor <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, cursor, comparison);
            if (index < 0)
                break;

            sb.Append(haystack, cursor, index - cursor).Append(replacement);
            cursor = index + needle.Length;
        }

        return sb.Append(haystack, cursor, haystack.Length - cursor).ToString();
    }

    /// <summary>Serialises raw operand bytes as a PDF literal string.</summary>
    private static string EscapeLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('(');

        foreach (var c in value)
        {
            switch (c)
            {
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    // Anything outside printable ASCII is written as a three-digit octal escape,
                    // which keeps two-byte glyph codes safe from stream parsers.
                    if (c < 32 || c > 126)
                        sb.Append('\\').Append(Convert.ToString(c & 0xFF, 8).PadLeft(3, '0'));
                    else
                        sb.Append(c);

                    break;
            }
        }

        return sb.Append(')').ToString();
    }
}
