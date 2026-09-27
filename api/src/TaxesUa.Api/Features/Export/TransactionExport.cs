using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;
using Ox = DocumentFormat.OpenXml.Spreadsheet;

namespace TaxesUa.Api.Features.Export;

internal abstract record ExportCell
{
    internal sealed record Text(string? Value) : ExportCell;

    internal sealed record Date(DateOnly? Value) : ExportCell;

    internal sealed record Scaled(long Units, int Decimals) : ExportCell;
}

internal sealed record ExportColumn(string Header, double Width, Func<Transaction, ExportCell> Cell);

/// <summary>
/// One owner's year as every export format receives it. <see cref="TotalIncomeKop"/> is the
/// <c>IncomeLedger</c> total the receipts screen shows, not a sum of the hryvnia column, which also
/// carries own transfers and other non-income rows.
/// </summary>
internal sealed record ExportedYear(int Year, IReadOnlyList<Transaction> Rows, long TotalIncomeKop, DateTime GeneratedAtKyiv);

// Every writer iterates Columns, so the files cannot drift apart on order, headers or values.
internal static class TransactionExport
{
    private const uint MoneyStyleIndex = 1;
    private const uint RateStyleIndex = 2;
    private const uint DateStyleIndex = 3;
    private const uint HeaderStyleIndex = 4;

    // Serial 0 is 1899-12-30 rather than 1900-01-00 because Excel keeps Lotus's fictitious 1900-02-29.
    private static readonly DateOnly ExcelEpoch = new(1899, 12, 30);

    internal const string DateFormat = "dd.MM.yyyy";

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    internal static readonly ExportColumn[] Columns =
    [
        new("Дата", 12, transaction => new ExportCell.Date(transaction.ValueDate)),
        new("Сума", 14, transaction => new ExportCell.Scaled(transaction.AmountMinor, 2)),
        new("Валюта", 10, transaction => new ExportCell.Text(transaction.Currency.ToString())),
        new("Курс", 12, transaction => new ExportCell.Scaled(transaction.RateE4, 4)),
        new("Дата курсу", 14, transaction => new ExportCell.Date(transaction.RateDate)),
        new("Джерело курсу", 16, transaction => new ExportCell.Text(RateSourceLabel(transaction.RateSource))),
        new("Сума, грн", 14, transaction => new ExportCell.Scaled(
            transaction.Kind == TransactionKind.RefundToClient
                ? -transaction.AmountUahKop
                : transaction.AmountUahKop,
            2)),
        new("Тип", 32, transaction => new ExportCell.Text(KindLabel(transaction.Kind))),
        new("Клієнт", 24, transaction => new ExportCell.Text(transaction.Client?.Name)),
        new("Номер інвойсу", 18, transaction => new ExportCell.Text(transaction.InvoiceNumber)),
        new("Коментар", 32, transaction => new ExportCell.Text(transaction.Description)),
        new("Причина (не дохід)", 32, transaction => new ExportCell.Text(transaction.NonIncomeReason)),
    ];

    public static byte[] ToCsv(IReadOnlyList<Transaction> rows)
    {
        var builder = new StringBuilder();
        AppendCsvLine(builder, Columns.Select(column => column.Header));
        foreach (var row in rows)
        {
            AppendCsvLine(builder, Columns.Select(column => RenderCsvValue(column.Cell(row))));
        }

        // No `sep=;` first line: with one, Excel ignores the BOM and misreads Cyrillic.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static byte[] ToXlsx(IReadOnlyList<Transaction> rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();

            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var sheetData = new Ox.SheetData();
            sheetData.Append(BuildHeaderRow());
            var rowIndex = 2u;
            foreach (var row in rows)
            {
                sheetData.Append(BuildDataRow(rowIndex, row));
                rowIndex++;
            }

            worksheetPart.Worksheet = new Ox.Worksheet(
                new Ox.SheetViews(new Ox.SheetView
                {
                    WorkbookViewId = 0,
                    Pane = new Ox.Pane
                    {
                        HorizontalSplit = 0,
                        VerticalSplit = 1,
                        TopLeftCell = "A2",
                        ActivePane = Ox.PaneValues.BottomLeft,
                        State = Ox.PaneStateValues.Frozen,
                    },
                }),
                BuildColumns(),
                sheetData);
            worksheetPart.Worksheet.Save();

            workbookPart.Workbook = new Ox.Workbook(
                new Ox.Sheets(
                    new Ox.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = 1,
                        Name = "Надходження",
                    }));
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static void AppendCsvLine(StringBuilder builder, IEnumerable<string> fields)
    {
        builder.Append(string.Join(';', fields.Select(QuoteCsvField))).Append("\r\n");
    }

    private static string RenderCsvValue(ExportCell cell) => cell switch
    {
        ExportCell.Text(var value) => EscapeFormula(value ?? string.Empty),
        ExportCell.Date(var value) => value?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
        ExportCell.Scaled(var units, var decimals) => FormatScaled(units, decimals, ',', string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(cell)),
    };

    // OWASP formula-injection defence, for text only: numbers and dates are ours, and prefixing
    // "-100,00" would turn a refund amount into text.
    private static string EscapeFormula(string value) =>
        value.Length > 0 && FormulaTriggers.Contains(value[0]) ? "'" + value : value;

    private static string QuoteCsvField(string field) =>
        field.IndexOfAny(['"', ';', '\r', '\n']) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";

    private static Ox.Row BuildHeaderRow()
    {
        var cells = new List<Ox.Cell>();
        for (var i = 0; i < Columns.Length; i++)
        {
            cells.Add(new Ox.Cell
            {
                CellReference = ColumnLetter(i) + "1",
                StyleIndex = HeaderStyleIndex,
                DataType = Ox.CellValues.InlineString,
                InlineString = new Ox.InlineString(
                    new Ox.Text(Columns[i].Header) { Space = SpaceProcessingModeValues.Preserve }),
            });
        }

        return new Ox.Row(cells) { RowIndex = 1 };
    }

    private static Ox.Row BuildDataRow(uint rowIndex, Transaction row)
    {
        var cells = new List<Ox.Cell>();
        for (var i = 0; i < Columns.Length; i++)
        {
            var cell = BuildXlsxCell(ColumnLetter(i) + rowIndex.ToString(CultureInfo.InvariantCulture), Columns[i].Cell(row));
            if (cell is not null)
            {
                cells.Add(cell);
            }
        }

        return new Ox.Row(cells) { RowIndex = rowIndex };
    }

    // Inline strings are never evaluated as formulas, so text needs no injection prefix here,
    // unlike the CSV path that a spreadsheet application parses on open.
    private static Ox.Cell? BuildXlsxCell(string reference, ExportCell cell) => cell switch
    {
        ExportCell.Text(null) => null,
        ExportCell.Text(var value) => new Ox.Cell
        {
            CellReference = reference,
            DataType = Ox.CellValues.InlineString,
            InlineString = new Ox.InlineString(new Ox.Text(value) { Space = SpaceProcessingModeValues.Preserve }),
        },
        ExportCell.Date(null) => null,
        ExportCell.Date(var value) => new Ox.Cell
        {
            CellReference = reference,
            StyleIndex = DateStyleIndex,
            CellValue = new Ox.CellValue(DateSerial(value!.Value).ToString(CultureInfo.InvariantCulture)),
        },
        ExportCell.Scaled(var units, var decimals) => new Ox.Cell
        {
            CellReference = reference,
            StyleIndex = decimals == 4 ? RateStyleIndex : MoneyStyleIndex,
            CellValue = new Ox.CellValue(FormatScaled(units, decimals, '.', string.Empty)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(cell)),
    };

    private static Ox.Columns BuildColumns()
    {
        var columns = new Ox.Columns();
        for (var i = 0; i < Columns.Length; i++)
        {
            columns.Append(new Ox.Column
            {
                Min = (uint)(i + 1),
                Max = (uint)(i + 1),
                Width = Columns[i].Width,
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static Ox.Stylesheet BuildStylesheet()
    {
        var numberingFormats = new Ox.NumberingFormats(
            new Ox.NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00" },
            new Ox.NumberingFormat { NumberFormatId = 165, FormatCode = "0.0000" })
        {
            Count = 2,
        };

        var fonts = new Ox.Fonts(
            new Ox.Font(),
            new Ox.Font(new Ox.Bold()))
        {
            Count = 2,
        };

        // Both fills are required by the OOXML spec even though neither is used by a CellFormat
        // here: readers expect index 0 = none and index 1 = gray125.
        var fills = new Ox.Fills(
            new Ox.Fill(new Ox.PatternFill { PatternType = Ox.PatternValues.None }),
            new Ox.Fill(new Ox.PatternFill { PatternType = Ox.PatternValues.Gray125 }))
        {
            Count = 2,
        };

        var borders = new Ox.Borders(
            new Ox.Border(
                new Ox.LeftBorder(),
                new Ox.RightBorder(),
                new Ox.TopBorder(),
                new Ox.BottomBorder(),
                new Ox.DiagonalBorder()))
        {
            Count = 1,
        };

        var cellFormats = new Ox.CellFormats(
            new Ox.CellFormat(),
            new Ox.CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new Ox.CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },
            new Ox.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
            new Ox.CellFormat { FontId = 1, ApplyFont = true })
        {
            Count = 5,
        };

        return new Ox.Stylesheet(numberingFormats, fonts, fills, borders, cellFormats);
    }

    private static string ColumnLetter(int index)
    {
        var dividend = index + 1;
        var letters = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            letters = (char)('A' + modulo) + letters;
            dividend = (dividend - modulo - 1) / 26;
        }

        return letters;
    }

    private static int DateSerial(DateOnly date) => date.DayNumber - ExcelEpoch.DayNumber;

    // Integer arithmetic only, so no binary fraction ever stands in for an amount. Negation cannot
    // overflow: the endpoints cap every amount at 1e14 and every rate at 1e7.
    internal static string FormatScaled(long units, int decimals, char decimalSeparator, string groupSeparator)
    {
        var scale = Scale(decimals);
        var negative = units < 0;
        var magnitude = negative ? -units : units;
        var whole = magnitude / scale;
        var fraction = magnitude % scale;
        var wholeText = whole.ToString("#,0", new NumberFormatInfo { NumberGroupSeparator = groupSeparator });
        var fractionText = fraction.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');

        return (negative ? "-" : string.Empty) + wholeText + decimalSeparator + fractionText;
    }

    private static long Scale(int decimals) => decimals switch
    {
        2 => 100,
        4 => 10_000,
        _ => throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "unsupported scale"),
    };

    private static string KindLabel(TransactionKind kind) => kind switch
    {
        TransactionKind.Income => "Дохід",
        TransactionKind.RefundToClient => "Повернення клієнту",
        TransactionKind.OwnTransfer => "Переказ між своїми рахунками",
        TransactionKind.FxSale => "Продаж валюти",
        TransactionKind.OwnDeposit => "Поповнення власними коштами",
        TransactionKind.ErroneousReturn => "Повернення помилкового платежу",
        TransactionKind.OtherNonIncome => "Інше, не дохід",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string? RateSourceLabel(RateSource? source) => source switch
    {
        RateSource.Nbu => "НБУ",
        RateSource.Manual => "вручну",
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
