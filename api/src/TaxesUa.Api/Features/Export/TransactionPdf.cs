using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Export;

/// <summary>
/// The year's receipts as an A4 landscape table for printing or mailing to an accountant. Rows, columns
/// and figures come from <see cref="TransactionExport.Columns"/>, as the CSV and XLSX do.
/// </summary>
internal static class TransactionPdf
{
    private const string FontFamily = "Noto Sans";
    private const string NoBreakSpace = " ";
    private const double PageMarginMm = 10;
    private const double PrintableWidthMm = 297 - 2 * PageMarginMm;
    private const double CellPaddingMm = 1;
    private const double CellFontSize = 7;

    static TransactionPdf()
    {
        // Process-wide and set once: the container has no system fonts, and PDFsharp's Core build
        // never reads them anyway.
        GlobalFontSettings.FontResolver = new NotoSansFontResolver();
    }

    public static byte[] ToPdf(ExportedYear year)
    {
        var document = new Document();
        document.Info.Title = $"Надходження за {year.Year}";
        document.Styles[StyleNames.Normal]!.Font.Name = FontFamily;
        document.Styles[StyleNames.Normal]!.Font.Size = 8;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = Unit.FromMillimeter(PageMarginMm);
        section.PageSetup.RightMargin = Unit.FromMillimeter(PageMarginMm);
        section.PageSetup.TopMargin = Unit.FromMillimeter(PageMarginMm);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(15);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(7);

        var title = section.AddParagraph($"Надходження за {year.Year}");
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;

        section.AddParagraph(
            $"Сформовано {year.GeneratedAtKyiv.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} (за київським часом)");

        var total = section.AddParagraph();
        total.Format.SpaceBefore = Unit.FromMillimeter(3);
        total.Format.Font.Size = 11;
        total.AddFormattedText($"Дохід за {year.Year}: ", TextFormat.NotBold);
        total.AddFormattedText($"{Money(year.TotalIncomeKop)} грн", TextFormat.Bold);

        var hint = section.AddParagraph("Дохід мінус повернення клієнтам, без операцій до дати реєстрації ФОП.");
        hint.Format.SpaceAfter = Unit.FromMillimeter(4);
        hint.Format.Font.Color = Colors.DimGray;

        section.Add(BuildTable(year.Rows));

        var footer = section.Footers.Primary.AddParagraph("Сторінка ");
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddPageField();
        footer.AddText(" з ");
        footer.AddNumPagesField();

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static Table BuildTable(IReadOnlyList<Transaction> rows)
    {
        var table = new Table
        {
            Borders = { Width = 0.5, Color = Colors.Gray },
            LeftPadding = Unit.FromMillimeter(CellPaddingMm),
            RightPadding = Unit.FromMillimeter(CellPaddingMm),
        };
        table.Format.Font.Size = CellFontSize;

        // The Excel character widths, scaled to fill the printable width in the same proportions.
        var totalWidth = TransactionExport.Columns.Sum(column => column.Width);
        var textWidths = new double[TransactionExport.Columns.Length];
        for (var i = 0; i < TransactionExport.Columns.Length; i++)
        {
            var width = Unit.FromMillimeter(PrintableWidthMm * TransactionExport.Columns[i].Width / totalWidth);
            table.AddColumn(width);
            // The spare point keeps a cut piece off the exact edge, where MigraDoc wraps it again and
            // leaves a blank line under it.
            textWidths[i] = width.Point - Unit.FromMillimeter(2 * CellPaddingMm).Point - 1;
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Colors.Gainsboro;
        header.Format.Font.Bold = true;
        for (var i = 0; i < TransactionExport.Columns.Length; i++)
        {
            header.Cells[i].AddParagraph(TransactionExport.Columns[i].Header);
        }

        using var measure = XGraphics.CreateMeasureContext(new XSize(1, 1), XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont(FontFamily, CellFontSize);
        foreach (var transaction in rows)
        {
            var row = table.AddRow();
            for (var i = 0; i < TransactionExport.Columns.Length; i++)
            {
                var paragraph = row.Cells[i].AddParagraph();
                switch (TransactionExport.Columns[i].Cell(transaction))
                {
                    case ExportCell.Text(var text):
                        var lines = BreakOverlongWords(text ?? string.Empty, textWidths[i], value => measure.MeasureString(value, font).Width);
                        for (var line = 0; line < lines.Count; line++)
                        {
                            if (line > 0)
                            {
                                paragraph.AddLineBreak();
                            }

                            paragraph.AddText(lines[line]);
                        }

                        break;
                    case ExportCell.Date(var date):
                        paragraph.AddText(date?.ToString(TransactionExport.DateFormat, CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                    case ExportCell.Scaled(var units, var decimals):
                        paragraph.AddText(TransactionExport.FormatScaled(units, decimals, ',', NoBreakSpace));
                        paragraph.Format.Alignment = ParagraphAlignment.Right;
                        break;
                }
            }
        }

        return table;
    }

    // MigraDoc wraps only at spaces and lets a longer word, such as a pasted URL, run into the next
    // columns. Such a word is cut where the cell ends; everything else is left for MigraDoc to wrap.
    internal static List<string> BreakOverlongWords(string text, double width, Func<string, double> measure)
    {
        var lines = new List<string>();
        foreach (var sourceLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in sourceLine.Split(' '))
            {
                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                var rest = word;
                while (measure(rest) > width && rest.Length > 1)
                {
                    var fits = 1;
                    while (fits < rest.Length && measure(rest[..(fits + 1)]) <= width)
                    {
                        fits++;
                    }

                    current.Append(rest[..fits]);
                    lines.Add(current.ToString());
                    current.Clear();
                    rest = rest[fits..];
                }

                current.Append(rest);
            }

            lines.Add(current.ToString());
        }

        return lines;
    }

    private static string Money(long kop) => TransactionExport.FormatScaled(kop, 2, ',', NoBreakSpace);

    // Every family resolves to Noto Sans, so a style that names another font still renders Cyrillic.
    private sealed class NotoSansFontResolver : IFontResolver
    {
        private const string Regular = "NotoSans-Regular";
        private const string Bold = "NotoSans-Bold";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? Bold : Regular);

        public byte[] GetFont(string faceName)
        {
            using var resource = typeof(TransactionPdf).Assembly
                .GetManifestResourceStream($"TaxesUa.Api.Fonts.{faceName}.ttf")!;
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
