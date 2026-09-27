using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Tests.Features.Export;

public sealed class TransactionExportTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void Csv_starts_with_a_utf8_bom()
    {
        var csv = TransactionExport.ToCsv([Income()]);

        Assert.Equal(Utf8Bom, csv.Take(3).ToArray());
    }

    [Fact]
    public void Csv_header_line_lists_the_column_headers_in_order()
    {
        var csv = TransactionExport.ToCsv([]);

        var headerLine = FirstLine(csv);
        Assert.Equal(string.Join(';', TransactionExport.Columns.Select(c => c.Header)), headerLine);
    }

    [Fact]
    public void The_column_layout_is_the_one_handed_to_the_accountant()
    {
        string[] expected =
        [
            "Дата", "Сума", "Валюта", "Курс", "Дата курсу", "Джерело курсу", "Сума, грн", "Тип", "Клієнт",
            "Номер інвойсу", "Коментар", "Причина (не дохід)",
        ];

        Assert.Equal(expected, TransactionExport.Columns.Select(column => column.Header));
    }

    // Stands in for "opens cleanly in Excel and Numbers", which no CI runner can check.
    [Fact]
    public void Xlsx_passes_the_open_xml_schema_validator()
    {
        var transaction = Income();
        transaction.Kind = TransactionKind.RefundToClient;
        transaction.Currency = Currency.EUR;
        transaction.RateE4 = 450_001;
        transaction.RateDate = new DateOnly(2026, 1, 14);
        transaction.RateSource = RateSource.Nbu;
        transaction.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = "Клієнт" };
        transaction.Description = "=cmd";

        using var document = SpreadsheetDocument.Open(new MemoryStream(TransactionExport.ToXlsx([transaction, Income()])), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2016).Validate(document).Select(error => $"{error.Path?.XPath}: {error.Description}");

        Assert.Empty(errors);
    }

    [Fact]
    public void Csv_row_count_equals_rows_plus_header()
    {
        var csv = TransactionExport.ToCsv([Income(), Income(), Income()]);

        var lines = Lines(csv);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void A_usd_row_renders_the_expected_fields()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = "user",
            ValueDate = new DateOnly(2026, 3, 4),
            AmountMinor = 10_000,
            Currency = Currency.USD,
            RateE4 = 412345,
            RateDate = new DateOnly(2026, 3, 3),
            RateSource = RateSource.Nbu,
            AmountUahKop = 412345,
            Kind = TransactionKind.Income,
        };

        var csv = TransactionExport.ToCsv([transaction]);
        var fields = Lines(csv)[1].Split(';');

        // Дата, Сума, Валюта, Курс, Дата курсу, Джерело курсу, Сума грн
        Assert.Equal("04.03.2026", fields[0]);
        Assert.Equal("100,00", fields[1]);
        Assert.Equal("USD", fields[2]);
        Assert.Equal("41,2345", fields[3]);
        Assert.Equal("03.03.2026", fields[4]);
        Assert.Equal("НБУ", fields[5]);
        Assert.Equal("4123,45", fields[6]);
    }

    [Fact]
    public void A_refund_negates_only_the_hryvnia_cell()
    {
        var transaction = Income();
        transaction.Kind = TransactionKind.RefundToClient;
        transaction.AmountMinor = 5_000;
        transaction.AmountUahKop = 5_000;

        var fields = Lines(TransactionExport.ToCsv([transaction]))[1].Split(';');

        Assert.Equal("50,00", fields[1]);
        Assert.Equal("-50,00", fields[6]);
    }

    [Fact]
    public void A_manual_rate_row_has_an_empty_rate_date_and_a_manual_source_label()
    {
        var transaction = Income();
        transaction.Currency = Currency.USD;
        transaction.RateE4 = 400_000;
        transaction.RateDate = null;
        transaction.RateSource = RateSource.Manual;

        var fields = Lines(TransactionExport.ToCsv([transaction]))[1].Split(';');

        Assert.Equal(string.Empty, fields[4]);
        Assert.Equal("вручну", fields[5]);
    }

    [Fact]
    public void Cyrillic_text_survives_a_utf8_round_trip()
    {
        var transaction = Income();
        transaction.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = "Товариство «Крок»" };

        var csv = TransactionExport.ToCsv([transaction]);
        var text = Encoding.UTF8.GetString(csv, Utf8Bom.Length, csv.Length - Utf8Bom.Length);

        Assert.Contains("Товариство «Крок»", text);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    public void Formula_trigger_prefixes_are_neutralised(string value)
    {
        var transaction = Income();
        transaction.Description = value;

        var fields = Lines(TransactionExport.ToCsv([transaction]))[1].Split(';');

        Assert.Equal("'" + value, Unquote(fields[10]));
    }

    [Fact]
    public void A_negative_refund_amount_is_never_formula_prefixed()
    {
        var transaction = Income();
        transaction.Kind = TransactionKind.RefundToClient;
        transaction.AmountUahKop = 5_000;

        var fields = Lines(TransactionExport.ToCsv([transaction]))[1].Split(';');

        Assert.Equal("-50,00", fields[6]);
        Assert.DoesNotContain('\'', fields[6]);
    }

    [Fact]
    public void A_value_with_a_delimiter_and_a_quote_is_quoted_and_escaped()
    {
        var transaction = Income();
        transaction.Description = "note; with \"quotes\"";

        var line = Lines(TransactionExport.ToCsv([transaction]))[1];

        Assert.Contains("\"note; with \"\"quotes\"\"\"", line);
    }

    [Fact]
    public void Xlsx_header_row_matches_the_column_headers()
    {
        var bytes = TransactionExport.ToXlsx([Income(), Income()]);

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheetData = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Elements<SheetData>().Single();
        var rows = sheetData.Elements<Row>().ToList();

        Assert.Equal(3, rows.Count);
        var headerTexts = rows[0].Elements<Cell>()
            .Select(cell => cell.InlineString!.Text!.Text)
            .ToArray();
        Assert.Equal(TransactionExport.Columns.Select(c => c.Header), headerTexts);
    }

    [Fact]
    public void Xlsx_money_cell_uses_the_money_style_and_no_cell_is_a_formula()
    {
        var transaction = Income();
        transaction.AmountUahKop = 412345;

        var bytes = TransactionExport.ToXlsx([transaction]);

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheetData = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Elements<SheetData>().Single();
        var allCells = sheetData.Elements<Row>().SelectMany(row => row.Elements<Cell>()).ToList();

        Assert.All(allCells, cell => Assert.Null(cell.CellFormula));

        var uahCell = allCells.Single(cell => cell.CellReference == "G2");
        Assert.Equal("4123.45", uahCell.CellValue!.Text);

        var stylesheet = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        var cellFormats = stylesheet.CellFormats!.Elements<CellFormat>().ToList();
        var styleIndex = (int)uahCell.StyleIndex!.Value;
        Assert.Equal(164u, cellFormats[styleIndex].NumberFormatId!.Value);
    }

    [Fact]
    public void Xlsx_text_cells_are_inline_strings()
    {
        var transaction = Income();
        transaction.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = "Acme" };

        var bytes = TransactionExport.ToXlsx([transaction]);

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheetData = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Elements<SheetData>().Single();
        var clientCell = sheetData.Descendants<Cell>().Single(cell => cell.CellReference == "I2");

        Assert.Equal(CellValues.InlineString, clientCell.DataType!.Value);
        Assert.Equal("Acme", clientCell.InlineString!.Text!.Text);
    }

    private static Transaction Income() => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user",
        ValueDate = new DateOnly(2026, 1, 15),
        AmountMinor = 10_000,
        Currency = Currency.UAH,
        RateE4 = 10_000,
        RateDate = null,
        RateSource = null,
        AmountUahKop = 10_000,
        Kind = TransactionKind.Income,
    };

    private static string FirstLine(byte[] csv) => Lines(csv)[0];

    private static string[] Lines(byte[] csv) =>
        Encoding.UTF8.GetString(csv, Utf8Bom.Length, csv.Length - Utf8Bom.Length)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static string Unquote(string field) =>
        field.Length >= 2 && field[0] == '"' && field[^1] == '"'
            ? field[1..^1].Replace("\"\"", "\"")
            : field;
}
