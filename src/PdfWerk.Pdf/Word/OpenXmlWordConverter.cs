using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Fonts;
using PdfWerk.Pdf.Internal;
using MigraOrientation = MigraDoc.DocumentObjectModel.Orientation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PdfWerk.Pdf.Word;

/// <summary>
/// Converts .docx in-process by reading the OpenXML package and re-rendering it with MigraDoc.
/// </summary>
/// <remarks>
/// <para>
/// The fallback for hosts without LibreOffice, and the only path that works in a minimal
/// container. It re-implements enough of the Word model to carry ordinary business documents
/// across: headings, runs with character formatting, lists, tables, images, hyperlinks and page
/// setup.
/// </para>
/// <para>
/// It is explicitly not a Word layout engine. Floating objects, columns, footnotes, text boxes,
/// fields and content controls are not reproduced, and anything it does not recognise is skipped
/// rather than approximated. Callers are told which converter ran, so a document that needs
/// full fidelity can be retried on a host that has LibreOffice.
/// </para>
/// </remarks>
public sealed class OpenXmlWordConverter : IWordConverter
{
    static OpenXmlWordConverter() => FileSystemFontResolver.Install();

    /// <summary>Twips per point: Word stores most measurements in twentieths of a point.</summary>
    private const double TwipsPerPoint = 20.0;

    public string Name => "openxml";

    /// <summary>Runs only when LibreOffice is absent.</summary>
    public int Priority => 100;

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default) => ValueTask.FromResult(true);

    public Task<PdfArtifact> ConvertAsync(byte[] source, string fileName, CancellationToken ct = default)
    {
        if (Path.GetExtension(fileName).Equals(".doc", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfWerkException(
                "Legacy .doc files can only be converted on a server with LibreOffice installed. " +
                "Save the document as .docx and try again.");
        }

        using var stream = new MemoryStream(source, writable: false);

        WordprocessingDocument package;
        try
        {
            package = WordprocessingDocument.Open(stream, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentException)
        {
            throw new PdfWerkException("This file could not be read as a Word document.");
        }

        using (package)
        {
            var main = package.MainDocumentPart
                       ?? throw new PdfWerkException("This Word document has no readable content.");

            var body = main.Document?.Body
                       ?? throw new PdfWerkException("This Word document has no readable content.");

            var document = new Document();
            document.Info.Title = Path.GetFileNameWithoutExtension(fileName);
            document.Info.Comment = "Converted by PdfWerk — https://pdfwerk.com";

            var normal = document.Styles["Normal"]!;
            normal.Font.Name = "Calibri";
            normal.Font.Size = Unit.FromPoint(11);
            normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
            normal.ParagraphFormat.LineSpacing = 1.15;

            var section = document.AddSection();
            ApplyPageSetup(section, body);

            WriteBody(section, main, body, ct);

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            using var output = new MemoryStream();
            renderer.PdfDocument.Save(output, closeStream: false);

            return Task.FromResult(new PdfArtifact(output.ToArray(), FileNames.WithExtension(fileName, ".pdf")));
        }
    }

    // ---- page setup ------------------------------------------------------

    private static void ApplyPageSetup(Section section, W.Body body)
    {
        var setup = section.PageSetup;

        // Sensible A4 defaults, overridden by whatever the document actually declares.
        setup.PageFormat = PageFormat.A4;

        // Explicit dimensions up front, because a .docx need not declare a page size and the
        // format name on its own leaves PageWidth reading as zero.
        PageGeometry.ApplyExplicitSize(setup);

        var margin = Unit.FromMillimeter(20);
        setup.LeftMargin = setup.RightMargin = setup.TopMargin = setup.BottomMargin = margin;

        var properties = body.Elements<W.SectionProperties>().LastOrDefault();
        if (properties is null)
            return;

        var size = properties.GetFirstChild<W.PageSize>();
        if (size is not null)
        {
            if (size.Width is { HasValue: true } width)
                setup.PageWidth = Unit.FromPoint(width.Value / TwipsPerPoint);

            if (size.Height is { HasValue: true } height)
                setup.PageHeight = Unit.FromPoint(height.Value / TwipsPerPoint);

            setup.Orientation = size.Orient?.Value == W.PageOrientationValues.Landscape
                ? MigraOrientation.Landscape
                : MigraOrientation.Portrait;
        }

        var margins = properties.GetFirstChild<W.PageMargin>();
        if (margins is null)
            return;

        if (margins.Left is { HasValue: true } left)
            setup.LeftMargin = Unit.FromPoint(left.Value / TwipsPerPoint);

        if (margins.Right is { HasValue: true } right)
            setup.RightMargin = Unit.FromPoint(right.Value / TwipsPerPoint);

        if (margins.Top is { HasValue: true } top)
            setup.TopMargin = Unit.FromPoint(top.Value / TwipsPerPoint);

        if (margins.Bottom is { HasValue: true } bottom)
            setup.BottomMargin = Unit.FromPoint(bottom.Value / TwipsPerPoint);
    }

    // ---- body ------------------------------------------------------------

    private static void WriteBody(Section section, MainDocumentPart main, W.Body body, CancellationToken ct)
    {
        foreach (var element in body.ChildElements)
        {
            ct.ThrowIfCancellationRequested();

            switch (element)
            {
                case W.Paragraph paragraph:
                    WriteParagraph(section, main, paragraph);
                    break;

                case W.Table table:
                    WriteTable(section, main, table);
                    break;

                // SectionProperties is handled by ApplyPageSetup; anything else is skipped.
            }
        }
    }

    private static void WriteParagraph(Section section, MainDocumentPart main, W.Paragraph source)
    {
        var styleId = source.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        var headingLevel = HeadingLevel(styleId);

        var paragraph = section.AddParagraph();
        paragraph.Format.SpaceAfter = Unit.FromPoint(8);

        if (headingLevel > 0)
        {
            var scale = headingLevel switch { 1 => 1.8, 2 => 1.5, 3 => 1.25, 4 => 1.12, _ => 1.0 };
            paragraph.Format.Font.Size = Unit.FromPoint(11 * scale);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.SpaceBefore = Unit.FromPoint(12);
            paragraph.Format.KeepWithNext = true;
        }
        else if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            paragraph.Format.Font.Size = Unit.FromPoint(24);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.SpaceAfter = Unit.FromPoint(16);
        }

        ApplyParagraphProperties(paragraph, source.ParagraphProperties, styleId);

        // A page break inside a run has to take effect before the rest of that run's text, so
        // the cursor can retire the current paragraph and continue into a fresh one.
        var cursor = new ParagraphCursor(paragraph, () =>
        {
            var next = section.AddParagraph();
            next.Format = paragraph.Format.Clone();
            return next;
        });

        foreach (var child in source.ChildElements)
        {
            switch (child)
            {
                case W.Run run:
                    WriteRun(cursor, main, run);
                    break;

                case W.Hyperlink link:
                    WriteHyperlink(cursor, main, link);
                    break;
            }
        }

        // MigraDoc drops a paragraph with no content, which would lose the intended blank line.
        if (!cursor.Wrote)
            cursor.Current.AddText(" ");
    }

    /// <summary>
    /// Tracks the paragraph being filled, so a mid-run page break can start a new one.
    /// </summary>
    /// <remarks>
    /// Word models a page break as a run child, not as a paragraph boundary, and a run may carry
    /// text on both sides of it. Appending a break to the section instead would place it after
    /// the paragraph already being written, stranding that text on the wrong page.
    /// </remarks>
    private sealed class ParagraphCursor(Paragraph first, Func<Paragraph>? successor)
    {
        public Paragraph Current { get; private set; } = first;

        /// <summary>True once real content has been added to the current paragraph.</summary>
        public bool Wrote { get; private set; }

        public void MarkWritten() => Wrote = true;

        public void PageBreak()
        {
            // Inside a table cell there is no section to break, so it degrades to a line break.
            if (successor is null)
            {
                Current.AddLineBreak();
                Wrote = true;
                return;
            }

            if (!Wrote)
            {
                // Nothing written yet: break before this paragraph rather than leaving it empty.
                Current.Format.PageBreakBefore = true;
                return;
            }

            Current = successor();
            Current.Format.PageBreakBefore = true;
            Wrote = false;
        }
    }

    private static void ApplyParagraphProperties(Paragraph paragraph, W.ParagraphProperties? properties, string styleId)
    {
        if (properties is null)
            return;

        paragraph.Format.Alignment = properties.Justification?.Val?.Value switch
        {
            var v when v == W.JustificationValues.Center => ParagraphAlignment.Center,
            var v when v == W.JustificationValues.Right => ParagraphAlignment.Right,
            var v when v == W.JustificationValues.Both => ParagraphAlignment.Justify,
            _ => ParagraphAlignment.Left,
        };

        var indent = properties.Indentation;
        if (indent?.Left is { HasValue: true } left)
            paragraph.Format.LeftIndent = Unit.FromPoint(left.Value is { } text && double.TryParse(text, out var twips) ? twips / TwipsPerPoint : 0);

        // Word expresses lists through a numbering definition; reproducing the exact glyph and
        // restart behaviour needs the numbering part, so the level alone is used to pick a
        // bullet or number list of the right depth.
        var numbering = properties.NumberingProperties;
        if (numbering is null && !styleId.Equals("ListParagraph", StringComparison.OrdinalIgnoreCase))
            return;

        var level = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
        var ordered = numbering?.NumberingId?.Val?.Value is > 0 && IsOrdered(numbering);

        paragraph.Format.ListInfo = new ListInfo
        {
            ListType = (ordered, level > 0) switch
            {
                (true, false) => ListType.NumberList1,
                (true, true) => ListType.NumberList2,
                (false, false) => ListType.BulletList1,
                (false, true) => ListType.BulletList2,
            },
            ContinuePreviousList = true,
        };

        paragraph.Format.LeftIndent = Unit.FromPoint(16 + (level * 16));
        paragraph.Format.SpaceAfter = Unit.FromPoint(3);
    }

    /// <summary>
    /// Without walking the numbering part there is no certain answer, so a numbering id greater
    /// than one is treated as ordered — Word conventionally assigns the first definition to the
    /// default bullet list.
    /// </summary>
    private static bool IsOrdered(W.NumberingProperties numbering) =>
        numbering.NumberingId?.Val?.Value > 1;

    private static int HeadingLevel(string styleId)
    {
        if (!styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            return 0;

        var suffix = styleId[7..];
        return int.TryParse(suffix, out var level) && level is >= 1 and <= 6 ? level : 0;
    }

    // ---- runs ------------------------------------------------------------

    private static void WriteRun(ParagraphCursor cursor, MainDocumentPart main, W.Run run)
    {
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case W.Text text:
                {
                    var value = text.Text;
                    if (value.Length == 0)
                        break;

                    var formatted = cursor.Current.AddFormattedText(value);
                    ApplyRunProperties(formatted, run.RunProperties);
                    cursor.MarkWritten();
                    break;
                }

                case W.Break brk:
                    if (brk.Type?.Value == W.BreakValues.Page)
                    {
                        cursor.PageBreak();
                    }
                    else
                    {
                        cursor.Current.AddLineBreak();
                        cursor.MarkWritten();
                    }

                    break;

                case W.TabChar:
                    cursor.Current.AddTab();
                    cursor.MarkWritten();
                    break;

                case W.Drawing drawing:
                    if (WriteImage(cursor.Current, main, drawing))
                        cursor.MarkWritten();

                    break;
            }
        }
    }

    private static void ApplyRunProperties(FormattedText text, W.RunProperties? properties)
    {
        if (properties is null)
            return;

        // In OpenXML a toggle element that is present with no explicit val means "on".
        if (IsOn(properties.Bold)) text.Bold = true;
        if (IsOn(properties.Italic)) text.Italic = true;

        if (properties.Underline?.Val is not null && properties.Underline.Val.Value != W.UnderlineValues.None)
            text.Underline = Underline.Single;

        if (properties.Strike is not null)
            text.Font.Subscript = false;    // MigraDoc has no strike-through; ignored deliberately

        // Sizes are stored in half-points.
        if (properties.FontSize?.Val?.Value is { } halfPoints && double.TryParse(halfPoints, out var value) && value > 0)
            text.Font.Size = Unit.FromPoint(value / 2);

        if (properties.Color?.Val?.Value is { } hex && hex.Length == 6 && !hex.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            {
                text.Font.Color = new Color(
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
            }
        }

        if (properties.RunFonts?.Ascii?.Value is { Length: > 0 } family)
            text.Font.Name = family;
    }

    private static bool IsOn(OpenXmlLeafElement? toggle) => toggle switch
    {
        null => false,
        W.Bold bold => bold.Val?.Value ?? true,
        W.Italic italic => italic.Val?.Value ?? true,
        _ => true,
    };

    private static void WriteHyperlink(ParagraphCursor cursor, MainDocumentPart main, W.Hyperlink link)
    {
        var text = string.Concat(link.Descendants<W.Text>().Select(t => t.Text));
        if (text.Length == 0)
            return;

        var target = ResolveHyperlink(main, link);

        if (target is null)
        {
            cursor.Current.AddText(text);
            cursor.MarkWritten();
            return;
        }

        var hyperlink = cursor.Current.AddHyperlink(target, HyperlinkType.Web);
        var formatted = hyperlink.AddFormattedText(text);
        formatted.Font.Color = new Color(29, 78, 216);
        formatted.Font.Underline = Underline.Single;
        cursor.MarkWritten();
    }

    private static string? ResolveHyperlink(MainDocumentPart main, W.Hyperlink link)
    {
        var id = link.Id?.Value;
        if (string.IsNullOrEmpty(id))
            return null;

        try
        {
            var relationship = main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id);
            var uri = relationship?.Uri;

            // Only absolute http(s) targets become clickable; a bookmark has no meaning here.
            return uri is { IsAbsoluteUri: true } && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : null;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool WriteImage(Paragraph paragraph, MainDocumentPart main, W.Drawing drawing)
    {
        var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
        var embed = blip?.Embed?.Value;
        if (string.IsNullOrEmpty(embed))
            return false;

        try
        {
            if (main.GetPartById(embed) is not ImagePart part)
                return false;

            using var stream = part.GetStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            if (buffer.Length == 0)
                return false;

            // MigraDoc reads in-memory images through a base64 pseudo-path, which avoids
            // spilling the caller's embedded images onto disk.
            var image = paragraph.AddImage("base64:" + Convert.ToBase64String(buffer.ToArray()));

            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
            if (extent?.Cx is { } cx && cx > 0)
            {
                // Drawing sizes are in EMUs: 914400 per inch, so 12700 per point.
                image.Width = Unit.FromPoint(cx / 12700.0);
                image.LockAspectRatio = true;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        {
            // A broken or unsupported image should not fail the whole conversion.
            return false;
        }
    }

    // ---- tables ----------------------------------------------------------

    /// <summary>
    /// Where one source cell ends up in the rendered grid.
    /// </summary>
    /// <remarks>
    /// Separated from the drawing so it can be tested directly. The failure this exists to
    /// prevent — a merged cell leaving empty boxes behind — is invisible to text extraction,
    /// because an empty box contains no text to extract. Asserting on the layout catches it;
    /// asserting on the rendered PDF cannot.
    /// </remarks>
    internal sealed record CellPlacement(int Row, int Column, int ColumnSpan, W.TableCell Source)
    {
        /// <summary>Extra rows this cell grows into, from the vertical merges that follow it.</summary>
        public int ExtraRows { get; set; }
    }

    /// <summary>Reads a cell's horizontal span.</summary>
    private static int SpanOf(W.TableCell cell) =>
        cell.TableCellProperties?.GridSpan?.Val?.Value is { } span && span > 0 ? span : 1;

    /// <summary>Whether a cell begins a vertical merge, continues one, or neither.</summary>
    private static (bool Restart, bool Continue) VerticalMergeOf(W.TableCell cell)
    {
        var merge = cell.TableCellProperties?.VerticalMerge;
        if (merge is null)
            return (false, false);

        // An omitted val means "continue" — the same toggle convention as bold and italic.
        var restart = merge.Val?.Value == W.MergedCellValues.Restart;
        return (restart, !restart);
    }

    /// <summary>
    /// Places every cell on the grid, resolving horizontal and vertical merges.
    /// </summary>
    /// <remarks>
    /// A merged cell occupies several grid columns but appears once in the row, so a row with
    /// merges has fewer <c>w:tc</c> elements than the table has columns. Placing cells by their
    /// index within the row rather than their position on the grid is what shifted every later
    /// cell leftwards; treating a vertical merge's continuation cells as ordinary ones is what
    /// drew an empty bordered box beneath the merged cell for every row it spanned.
    /// </remarks>
    internal static IReadOnlyList<CellPlacement> LayOut(W.Table table, out int columnCount)
    {
        var rows = table.Elements<W.TableRow>().ToList();

        // The declared grid is authoritative. Without one the width has to be summed from the
        // spans, because a row of three cells where one spans two columns is four columns wide.
        var declared = table.Elements<W.TableGrid>().FirstOrDefault()?
            .Elements<W.GridColumn>().Count() ?? 0;
        var widest = rows.Count == 0 ? 0 : rows.Max(r => r.Elements<W.TableCell>().Sum(SpanOf));

        columnCount = Math.Max(declared, widest);
        if (columnCount == 0)
            return [];

        var placements = new List<CellPlacement>();
        var openMerge = new CellPlacement?[columnCount];

        for (var r = 0; r < rows.Count; r++)
        {
            var column = 0;

            foreach (var sourceCell in rows[r].Elements<W.TableCell>())
            {
                if (column >= columnCount)
                    break;

                var span = Math.Min(SpanOf(sourceCell), columnCount - column);
                var (restart, continues) = VerticalMergeOf(sourceCell);

                if (continues && openMerge[column] is { } origin)
                {
                    // Absorbed by the cell above. Any content here is Word's own placeholder
                    // and is not meant to be shown a second time.
                    origin.ExtraRows++;
                    column += span;
                    continue;
                }

                var placement = new CellPlacement(r, column, span, sourceCell);
                placements.Add(placement);

                openMerge[column] = restart ? placement : null;
                column += span;
            }
        }

        return placements;
    }

    private static void WriteTable(Section section, MainDocumentPart main, W.Table source)
    {
        var rowCount = source.Elements<W.TableRow>().Count();
        if (rowCount == 0)
            return;

        var placements = LayOut(source, out var columnCount);
        if (columnCount == 0)
            return;

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Silver;
        table.Rows.LeftIndent = 0;

        var usable = PageGeometry.ContentWidth(section.PageSetup);

        foreach (var width in ColumnWidths(source, columnCount, usable))
            table.AddColumn(Unit.FromPoint(width));

        for (var r = 0; r < rowCount; r++)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
        }

        foreach (var placement in placements)
        {
            var cell = table.Rows[placement.Row].Cells[placement.Column];

            if (placement.ColumnSpan > 1)
                cell.MergeRight = placement.ColumnSpan - 1;

            if (placement.ExtraRows > 0)
                cell.MergeDown = placement.ExtraRows;

            cell.Format.SpaceBefore = Unit.FromPoint(3);
            cell.Format.SpaceAfter = Unit.FromPoint(3);
            cell.Format.LeftIndent = Unit.FromPoint(4);
            cell.Format.RightIndent = Unit.FromPoint(4);

            foreach (var paragraph in placement.Source.Elements<W.Paragraph>())
            {
                var target = cell.AddParagraph();
                target.Format.SpaceAfter = Unit.FromPoint(2);

                // No successor factory: a page break inside a cell has nowhere to go.
                var cursor = new ParagraphCursor(target, successor: null);

                foreach (var run in paragraph.Elements<W.Run>())
                    WriteRun(cursor, main, run);

                if (!cursor.Wrote)
                    target.AddText(" ");
            }
        }

        var spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromPoint(10);
        spacer.Format.Font.Size = Unit.FromPoint(1);
    }

    /// <summary>Uses the declared grid where present, falling back to even columns.</summary>
    private static double[] ColumnWidths(W.Table table, int columnCount, double usable)
    {
        var grid = table.Elements<W.TableGrid>().FirstOrDefault();

        var declared = grid?.Elements<W.GridColumn>()
            .Select(c => double.TryParse(c.Width?.Value, out var twips) ? twips / TwipsPerPoint : 0)
            .Where(w => w > 0)
            .ToArray() ?? [];

        if (declared.Length != columnCount)
            return [.. Enumerable.Repeat(usable / columnCount, columnCount)];

        // Scale the declared widths to fit the printable area rather than overflowing it.
        var total = declared.Sum();
        if (total <= 0)
            return [.. Enumerable.Repeat(usable / columnCount, columnCount)];

        var scale = Math.Min(1.0, usable / total);
        return [.. declared.Select(w => w * scale)];
    }
}
