using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;
using UglyToad.PdfPig;

namespace TaxesUa.Api.Tests.Features.Export;

public sealed class TransactionPdfTests
{
    private static readonly DateTime GeneratedAt = new(2026, 9, 27, 14, 5, 0);

    [Fact]
    public void A_short_year_fits_one_a4_landscape_page_with_its_rows_and_total()
    {
        var refund = Row(new DateOnly(2026, 2, 1), 5_000, TransactionKind.RefundToClient);
        var usd = Row(new DateOnly(2026, 3, 4), 123_456, TransactionKind.Income);
        usd.Currency = Currency.USD;
        usd.RateE4 = 412_345;
        usd.RateDate = new DateOnly(2026, 3, 4);
        usd.RateSource = RateSource.Nbu;
        usd.AmountUahKop = 5_090_615_76;
        usd.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = "ТОВ «Київські рішення»" };

        using var pdf = Open(new ExportedYear(2026, [Row(new DateOnly(2026, 1, 15), 10_000), refund, usd], 5_090_620_76, GeneratedAt));

        Assert.Equal(1, pdf.NumberOfPages);
        var page = pdf.GetPage(1);
        Assert.Equal(842, page.Width, 0);
        Assert.Equal(595, page.Height, 0);

        var text = Text(pdf);
        Assert.Contains("Надходження за 2026", text);
        Assert.Contains("Сформовано 27.09.2026 14:05", text);
        Assert.Contains("Дохід за 2026: 5 090 620,76 грн", text);
        Assert.Contains("15.01.2026", text);
        Assert.Contains("-50,00", text);
        Assert.Contains("Повернення клієнту", text);
        Assert.Contains("1 234,56", text);
        Assert.Contains("41,2345", text);
        Assert.Contains("5 090 615,76", text);
        Assert.Contains("ТОВ «Київські рішення»", text);
        Assert.Contains("Сторінка 1 з 1", text);
    }

    [Fact]
    public void Every_column_header_is_printed()
    {
        using var pdf = Open(new ExportedYear(2026, [Row(new DateOnly(2026, 1, 15), 10_000)], 10_000, GeneratedAt));

        var words = Words(pdf, 1);
        foreach (var header in TransactionExport.Columns)
        {
            Assert.All(header.Header.Split(' '), word => Assert.Contains(word, words));
        }
    }

    [Fact]
    public void A_long_client_name_wraps_inside_its_cell_and_keeps_every_word()
    {
        var name = "Товариство з обмеженою відповідальністю «Дуже довга назва клієнта для перевірки перенесення рядків у таблиці»";
        var row = Row(new DateOnly(2026, 1, 15), 10_000);
        row.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = name };

        using var pdf = Open(new ExportedYear(2026, [row], 10_000, GeneratedAt));

        var words = Words(pdf, 1);
        Assert.All(name.Split(' '), word => Assert.Contains(word, words));
        var clientWords = pdf.GetPage(1).GetWords()
            .Where(word => word.Text.Length > 3 && name.Split(' ').Contains(word.Text))
            .ToList();
        Assert.True(clientWords.Select(word => Math.Round(word.BoundingBox.Bottom)).Distinct().Count() > 1, "the name wraps onto several lines");
        var clientColumnRight = clientWords.Max(word => word.BoundingBox.Right);
        var invoiceHeaderLeft = pdf.GetPage(1).GetWords().First(word => word.Text == "Номер").BoundingBox.Left;
        Assert.True(clientColumnRight < invoiceHeaderLeft, "the name stays inside the client column");
    }

    [Fact]
    public void An_unbroken_client_name_at_the_length_limit_stays_inside_its_cell()
    {
        var row = Row(new DateOnly(2026, 1, 15), 10_000);
        row.Client = new Client { Id = Guid.NewGuid(), UserId = "user", Name = new string('Ж', 200) };

        using var pdf = Open(new ExportedYear(2026, [row], 10_000, GeneratedAt));

        var page = pdf.GetPage(1);
        var invoiceHeaderLeft = page.GetWords().First(word => word.Text == "Номер").BoundingBox.Left;
        var clientLetters = page.Letters.Where(letter => letter.Value == "Ж").ToList();
        Assert.Equal(200, clientLetters.Count);
        Assert.True(clientLetters.Max(letter => letter.BoundingBox.Right) < invoiceHeaderLeft);
    }

    [Fact]
    public void A_long_year_spans_pages_numbered_out_of_the_total_with_the_header_repeated()
    {
        var rows = Enumerable.Range(0, 120)
            .Select(i => Row(new DateOnly(2026, 1, 1).AddDays(i), 10_000 + i))
            .ToList();

        using var pdf = Open(new ExportedYear(2026, rows, rows.Sum(row => row.AmountUahKop), GeneratedAt));

        Assert.True(pdf.NumberOfPages > 1);
        var text = Text(pdf);
        for (var page = 1; page <= pdf.NumberOfPages; page++)
        {
            Assert.Contains($"Сторінка {page} з {pdf.NumberOfPages}", PageText(pdf, page));
            Assert.Contains("Причина", Words(pdf, page));
        }

        Assert.Contains("01.01.2026", text);
        Assert.Contains("30.04.2026", text);
        Assert.Contains("101,19", text);
    }

    [Fact]
    public void An_empty_year_still_renders_the_header_and_a_zero_total()
    {
        using var pdf = Open(new ExportedYear(2026, [], 0, GeneratedAt));

        Assert.Equal(1, pdf.NumberOfPages);
        Assert.Contains("Дохід за 2026: 0,00 грн", Text(pdf));
    }

    [Fact]
    public void Only_a_word_wider_than_the_cell_is_cut_and_line_breaks_are_kept()
    {
        var lines = TransactionPdf.BreakOverlongWords("ab abcdefgh\r\nxy", 3, text => text.Length);

        Assert.Equal(["ab abc", "def", "gh", "xy"], lines);
    }

    [Fact]
    public void Text_that_fits_is_left_whole_for_the_layout_engine_to_wrap()
    {
        var lines = TransactionPdf.BreakOverlongWords("один два три", 4, text => text.Length);

        Assert.Equal(["один два три"], lines);
    }

    private static PdfDocument Open(ExportedYear year) => PdfDocument.Open(TransactionPdf.ToPdf(year));

    private static string Text(PdfDocument pdf) =>
        string.Join('\n', Enumerable.Range(1, pdf.NumberOfPages).Select(page => PageText(pdf, page)));

    private static string PageText(PdfDocument pdf, int page) =>
        string.Join(' ', pdf.GetPage(page).GetWords().Select(word => Normalize(word.Text)));

    private static HashSet<string> Words(PdfDocument pdf, int page) =>
        pdf.GetPage(page).GetWords().Select(word => Normalize(word.Text)).ToHashSet();

    // Money is grouped with a no-break space so a figure never wraps inside its cell.
    private static string Normalize(string text) => text.Replace(' ', ' ');

    private static Transaction Row(DateOnly date, long amountKop, TransactionKind kind = TransactionKind.Income) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user",
        ValueDate = date,
        AmountMinor = amountKop,
        Currency = Currency.UAH,
        RateE4 = 10_000,
        AmountUahKop = amountKop,
        Kind = kind,
    };
}
