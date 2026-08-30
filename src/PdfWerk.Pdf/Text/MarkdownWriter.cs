using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MdTextFormat = MigraDoc.DocumentObjectModel.TextFormat;

namespace PdfWerk.Pdf.Text;

/// <summary>
/// Renders a practical subset of Markdown into a MigraDoc section: ATX headings, bullet and
/// ordered lists, fenced code, block quotes, horizontal rules, pipe tables, and inline
/// bold / italic / code / links.
/// </summary>
/// <remarks>
/// This is deliberately not a CommonMark implementation. It covers what people actually paste
/// into a "make me a PDF" box, and anything it does not recognise degrades to a plain
/// paragraph rather than throwing — a malformed table should still produce a document.
/// </remarks>
internal sealed class MarkdownWriter(Section section, double baseFontSize)
{
    private readonly Section _section = section;
    private readonly double _base = baseFontSize;

    public void Write(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n')
                            .Split('\n');

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (IsFence(line)) { i = WriteFencedCode(lines, i); continue; }
            if (IsRule(line)) { WriteRule(); i++; continue; }
            if (TryHeadingLevel(line, out var level)) { WriteHeading(line.TrimStart()[(level + 1)..].Trim(), level); i++; continue; }
            if (IsTableStart(lines, i)) { i = WriteTable(lines, i); continue; }
            if (IsQuote(line)) { i = WriteQuote(lines, i); continue; }
            if (IsBullet(line) || IsOrdered(line)) { i = WriteList(lines, i); continue; }

            i = WriteParagraph(lines, i);
        }
    }

    // ---- block detection -------------------------------------------------

    private static bool IsFence(string line) =>
        line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
        line.TrimStart().StartsWith("~~~", StringComparison.Ordinal);

    private static bool IsRule(string line)
    {
        var t = line.Trim();
        if (t.Length < 3) return false;
        return t.All(c => c == '-') || t.All(c => c == '*') || t.All(c => c == '_');
    }

    private static bool TryHeadingLevel(string line, out int level)
    {
        level = 0;
        var t = line.TrimStart();
        while (level < t.Length && t[level] == '#') level++;

        // "#Heading" without a space is not a heading, and seven hashes is past the limit.
        return level is >= 1 and <= 6 && level < t.Length && t[level] == ' ';
    }

    private static bool IsQuote(string line) => line.TrimStart().StartsWith('>');

    private static bool IsBullet(string line)
    {
        var t = line.TrimStart();
        return t.Length >= 2 && (t[0] == '-' || t[0] == '*' || t[0] == '+') && t[1] == ' ';
    }

    private static bool IsOrdered(string line)
    {
        var t = line.TrimStart();
        var digits = t.TakeWhile(char.IsDigit).Count();
        return digits > 0 && digits + 1 < t.Length && (t[digits] == '.' || t[digits] == ')') && t[digits + 1] == ' ';
    }

    private static bool IsTableStart(string[] lines, int i)
    {
        if (!lines[i].Contains('|', StringComparison.Ordinal)) return false;
        if (i + 1 >= lines.Length) return false;

        // The following line must be the delimiter row, e.g. | --- | :---: |
        var sep = lines[i + 1].Trim();
        if (!sep.Contains('-', StringComparison.Ordinal)) return false;

        return sep.Trim('|').Split('|')
                  .All(c => c.Trim().Length > 0 && c.Trim().All(ch => ch == '-' || ch == ':' || ch == ' '));
    }

    // ---- block writers ---------------------------------------------------

    private void WriteHeading(string text, int level)
    {
        var p = _section.AddParagraph();
        var scale = level switch { 1 => 1.85, 2 => 1.5, 3 => 1.25, 4 => 1.1, 5 => 1.0, _ => 0.95 };

        p.Format.Font.Size = Unit.FromPoint(_base * scale);
        p.Format.Font.Bold = true;
        p.Format.SpaceBefore = Unit.FromPoint(level == 1 ? 14 : 12);
        p.Format.SpaceAfter = Unit.FromPoint(6);
        p.Format.KeepWithNext = true;

        if (level <= 2)
        {
            p.Format.Borders.Bottom.Width = level == 1 ? 1.0 : 0.5;
            p.Format.Borders.Bottom.Color = Colors.LightGray;
            p.Format.Borders.Distance = Unit.FromPoint(3);
        }

        WriteInline(p, text);
    }

    private void WriteRule()
    {
        var p = _section.AddParagraph();
        p.Format.SpaceBefore = Unit.FromPoint(8);
        p.Format.SpaceAfter = Unit.FromPoint(8);
        p.Format.Borders.Bottom.Width = 0.75;
        p.Format.Borders.Bottom.Color = Colors.Gray;
    }

    private int WriteParagraph(string[] lines, int start)
    {
        var buffer = new List<string>();
        var i = start;

        // Consume until a blank line or the start of a different block type.
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
        {
            if (i > start && (IsFence(lines[i]) || IsRule(lines[i]) || TryHeadingLevel(lines[i], out _) ||
                              IsBullet(lines[i]) || IsOrdered(lines[i]) || IsQuote(lines[i])))
                break;

            buffer.Add(lines[i].Trim());
            i++;
        }

        var p = _section.AddParagraph();
        p.Format.SpaceAfter = Unit.FromPoint(8);
        p.Format.Alignment = ParagraphAlignment.Justify;
        WriteInline(p, string.Join(' ', buffer));
        return i;
    }

    private int WriteList(string[] lines, int start)
    {
        var ordered = IsOrdered(lines[start]);
        var i = start;
        var first = true;

        while (i < lines.Length && (IsBullet(lines[i]) || IsOrdered(lines[i])))
        {
            // A switch between bullet and numbered ends this list and starts another.
            if (IsOrdered(lines[i]) != ordered) break;

            var t = lines[i].TrimStart();
            var content = ordered
                ? t[(t.TakeWhile(char.IsDigit).Count() + 2)..]
                : t[2..];

            // Two spaces of leading indent nests one level, which is as deep as we go.
            var nested = lines[i].Length - t.Length >= 2;

            var p = _section.AddParagraph();
            p.Format.ListInfo = new ListInfo
            {
                ListType = (ordered, nested) switch
                {
                    (true, false) => ListType.NumberList1,
                    (true, true) => ListType.NumberList2,
                    (false, false) => ListType.BulletList1,
                    (false, true) => ListType.BulletList2,
                },
                ContinuePreviousList = !first,
            };
            p.Format.LeftIndent = Unit.FromPoint(nested ? 32 : 16);
            p.Format.SpaceAfter = Unit.FromPoint(3);

            WriteInline(p, content.Trim());
            first = false;
            i++;
        }

        // Restore the gap a normal paragraph would have left after the block.
        if (_section.Elements.LastObject is Paragraph last)
            last.Format.SpaceAfter = Unit.FromPoint(8);

        return i;
    }

    private int WriteQuote(string[] lines, int start)
    {
        var buffer = new List<string>();
        var i = start;

        while (i < lines.Length && IsQuote(lines[i]))
        {
            var t = lines[i].TrimStart()[1..];
            buffer.Add(t.StartsWith(' ') ? t[1..] : t);
            i++;
        }

        var p = _section.AddParagraph();
        p.Format.LeftIndent = Unit.FromPoint(16);
        p.Format.SpaceBefore = Unit.FromPoint(6);
        p.Format.SpaceAfter = Unit.FromPoint(10);
        p.Format.Borders.Left.Width = 2.5;
        p.Format.Borders.Left.Color = Colors.LightSlateGray;
        p.Format.Borders.Distance = Unit.FromPoint(8);
        p.Format.Font.Color = Colors.DimGray;
        p.Format.Font.Italic = true;

        WriteInline(p, string.Join(' ', buffer));
        return i;
    }

    private int WriteFencedCode(string[] lines, int start)
    {
        var i = start + 1;
        var buffer = new List<string>();

        while (i < lines.Length && !IsFence(lines[i]))
        {
            buffer.Add(lines[i]);
            i++;
        }

        WriteCodeBlock(buffer);
        return i < lines.Length ? i + 1 : i;   // step over the closing fence when present
    }

    private void WriteCodeBlock(IReadOnlyList<string> code)
    {
        var p = _section.AddParagraph();
        p.Format.Font.Name = "Courier New";
        p.Format.Font.Size = Unit.FromPoint(_base * 0.85);
        p.Format.LeftIndent = Unit.FromPoint(8);
        p.Format.RightIndent = Unit.FromPoint(8);
        p.Format.SpaceBefore = Unit.FromPoint(6);
        p.Format.SpaceAfter = Unit.FromPoint(10);
        p.Format.Shading.Color = new Color(244, 245, 247);
        p.Format.Borders.Width = 0.4;
        p.Format.Borders.Color = Colors.Gainsboro;
        p.Format.Borders.Distance = Unit.FromPoint(5);

        for (var n = 0; n < code.Count; n++)
        {
            // Code is verbatim: no inline parsing, and runs of spaces must survive layout.
            p.AddText(code[n].Replace(" ", "\u00A0", StringComparison.Ordinal));
            if (n < code.Count - 1)
                p.AddLineBreak();
        }
    }

    private int WriteTable(string[] lines, int start)
    {
        var header = SplitRow(lines[start]);
        var alignments = SplitRow(lines[start + 1]).Select(ParseAlignment).ToList();

        var body = new List<string[]>();
        var i = start + 2;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|', StringComparison.Ordinal))
        {
            body.Add(SplitRow(lines[i]));
            i++;
        }

        var columns = Math.Max(header.Length, body.Count == 0 ? 0 : body.Max(r => r.Length));
        if (columns == 0)
            return start + 1;

        var table = _section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Silver;
        table.Rows.LeftIndent = 0;

        // MigraDoc needs explicit column widths, so divide the printable width evenly.
        var usable = PageGeometry.ContentWidth(_section.PageSetup);
        for (var c = 0; c < columns; c++)
            table.AddColumn(Unit.FromPoint(usable / columns));

        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;      // repeats the header when the table spans pages
        headerRow.Shading.Color = new Color(238, 242, 247);
        headerRow.Format.Font.Bold = true;
        FillRow(headerRow, header, columns, alignments);

        foreach (var cells in body)
            FillRow(table.AddRow(), cells, columns, alignments);

        // AddTable leaves no trailing space of its own.
        var spacer = _section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromPoint(10);
        spacer.Format.Font.Size = Unit.FromPoint(1);

        return i;
    }

    private void FillRow(Row row, string[] cells, int columns, IReadOnlyList<ParagraphAlignment> alignments)
    {
        row.VerticalAlignment = VerticalAlignment.Center;

        for (var c = 0; c < columns; c++)
        {
            var cell = row.Cells[c];
            cell.Format.Font.Size = Unit.FromPoint(_base * 0.95);
            cell.Format.SpaceBefore = Unit.FromPoint(3);
            cell.Format.SpaceAfter = Unit.FromPoint(3);
            cell.Format.LeftIndent = Unit.FromPoint(4);
            cell.Format.RightIndent = Unit.FromPoint(4);

            var p = cell.AddParagraph();
            p.Format.Alignment = c < alignments.Count ? alignments[c] : ParagraphAlignment.Left;
            if (c < cells.Length)
                WriteInline(p, cells[c]);
        }
    }

    private static string[] SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToArray();
    }

    private static ParagraphAlignment ParseAlignment(string spec)
    {
        var t = spec.Trim();
        var left = t.StartsWith(':');
        var right = t.EndsWith(':');
        return (left, right) switch
        {
            (true, true) => ParagraphAlignment.Center,
            (false, true) => ParagraphAlignment.Right,
            _ => ParagraphAlignment.Left,
        };
    }

    // ---- inline formatting -----------------------------------------------

    /// <summary>
    /// Walks the span applying bold / italic / code / links. Each delimiter is matched against
    /// the nearest closing marker; an unmatched marker is emitted literally so that prose like
    /// "5 * 3" survives intact.
    /// </summary>
    private void WriteInline(Paragraph p, string text)
    {
        var literal = new System.Text.StringBuilder();
        var i = 0;

        void Flush()
        {
            if (literal.Length == 0) return;
            p.AddText(literal.ToString());
            literal.Clear();
        }

        while (i < text.Length)
        {
            // An escaped delimiter renders as itself: \* is a literal asterisk.
            if (text[i] == '\\' && i + 1 < text.Length && "*_`[]\\".Contains(text[i + 1], StringComparison.Ordinal))
            {
                literal.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (TryDelimited(text, i, "**", out var boldEnd))
            {
                Flush();
                p.AddFormattedText(text[(i + 2)..boldEnd], MdTextFormat.Bold);
                i = boldEnd + 2;
                continue;
            }

            if (TryDelimited(text, i, "__", out var bold2End))
            {
                Flush();
                p.AddFormattedText(text[(i + 2)..bold2End], MdTextFormat.Bold);
                i = bold2End + 2;
                continue;
            }

            if (TryDelimited(text, i, "`", out var codeEnd))
            {
                Flush();
                // FormattedText carries no shading of its own, so inline code is set apart
                // by face and colour rather than by a background fill.
                var code = p.AddFormattedText(text[(i + 1)..codeEnd]);
                code.Font.Name = "Courier New";
                code.Font.Size = Unit.FromPoint(_base * 0.9);
                code.Font.Color = new Color(190, 24, 93);
                i = codeEnd + 1;
                continue;
            }

            if ((text[i] == '*' || text[i] == '_') && TryDelimited(text, i, text[i].ToString(), out var italEnd))
            {
                Flush();
                p.AddFormattedText(text[(i + 1)..italEnd], MdTextFormat.Italic);
                i = italEnd + 1;
                continue;
            }

            if (text[i] == '[' && TryLink(text, i, out var label, out var url, out var linkEnd))
            {
                Flush();
                var link = p.AddHyperlink(url, HyperlinkType.Web);
                var ft = link.AddFormattedText(label);
                ft.Font.Color = new Color(29, 78, 216);
                ft.Font.Underline = Underline.Single;
                i = linkEnd;
                continue;
            }

            literal.Append(text[i]);
            i++;
        }

        Flush();
    }

    private static bool TryDelimited(string text, int start, string marker, out int closeIndex)
    {
        closeIndex = -1;
        if (!text.AsSpan(start).StartsWith(marker, StringComparison.Ordinal))
            return false;

        var searchFrom = start + marker.Length;
        if (searchFrom >= text.Length)
            return false;

        var close = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);
        if (close < 0 || close == searchFrom)   // an empty span means the markers are literal
            return false;

        closeIndex = close;
        return true;
    }

    private static bool TryLink(string text, int start, out string label, out string url, out int end)
    {
        label = url = string.Empty;
        end = start;

        var close = text.IndexOf(']', start);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
            return false;

        var urlEnd = text.IndexOf(')', close + 2);
        if (urlEnd < 0)
            return false;

        label = text[(start + 1)..close];
        url = text[(close + 2)..urlEnd].Trim();

        // Only http(s) links become clickable; anything else falls back to literal text.
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        end = urlEnd + 1;
        return true;
    }
}
