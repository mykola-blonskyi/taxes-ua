using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Tests.Features.Settings;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TaxesUa.Api.Tests.Features.Invoices;

public sealed class InvoicePdfTests
{
    [Fact]
    public void A_readable_signature_is_drawn_above_the_sellers_name()
    {
        using var pdf = PdfDocument.Open(InvoicePdf.ToPdf(Model(InvoicingTestData.SignaturePng)));

        Assert.Single(pdf.GetPage(1).GetImages());
        Assert.Contains("FOP Test Testovych / ФОП Тест Тестович", Text(pdf));
    }

    public static TheoryData<string, byte[]> UnreadableSignatures() => new()
    {
        { "truncated PNG", InvoicingTestData.SignaturePng[..200] },
        { "PNG the importer refuses", InvoicingTestData.Png },
        { "PNG header only", InvoicingTestData.SignaturePng[..33] },
        { "JPEG markers without an image", InvoicingTestData.Jpeg },
        { "not an image", "not an image"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(UnreadableSignatures))]
    public void An_unreadable_signature_falls_back_to_the_sellers_name(string name, byte[] signature)
    {
        using var pdf = PdfDocument.Open(InvoicePdf.ToPdf(Model(signature)));

        Assert.True(!pdf.GetPage(1).GetImages().Any(), name);
        Assert.Contains("FOP Test Testovych / ФОП Тест Тестович", Text(pdf));
        Assert.Contains("Total / Разом: 1 000.00 EUR", Text(pdf));
    }

    [Theory]
    [InlineData("Draft", "DRAFT")]
    [InlineData("Cancelled", "CANCELLED")]
    public void The_status_mark_sits_above_the_title_without_touching_it(string status, string mark)
    {
        using var pdf = PdfDocument.Open(InvoicePdf.ToPdf(Model(null) with { Status = Enum.Parse<InvoiceStatus>(status) }));

        var words = pdf.GetPage(1).GetWords().ToList();
        var markBox = words.First(word => word.Text == mark).BoundingBox;
        var titleBox = words.First(word => word.Text == "Invoice").BoundingBox;
        Assert.True(markBox.Bottom > titleBox.Top, $"{mark} bottom {markBox.Bottom} is above the title top {titleBox.Top}");
    }

    [Fact]
    public void Fifty_lines_run_onto_further_pages_with_every_line_and_the_total()
    {
        var lines = Enumerable.Range(1, InvoiceRules.MaxLines)
            .Select(i => new InvoiceLine($"Task {i}", $"Задача {i}", InvoiceUnit.Hour, 1_000, 10_00))
            .ToArray();

        using var pdf = PdfDocument.Open(InvoicePdf.ToPdf(Model(null) with { Lines = lines, TotalMinor = 500_00 }));

        Assert.True(pdf.NumberOfPages > 1);
        var text = Text(pdf);
        Assert.Contains("Task 50", text);
        Assert.Contains("Задача 50", text);
        Assert.Contains("Total / Разом: 500.00 EUR", text);
    }

    [Fact]
    public void An_unbroken_description_wraps_inside_its_column()
    {
        var word = new string('x', InvoiceRules.MaxDescriptionLength);
        var model = Model(null) with { Lines = [new InvoiceLine(word, "Опис", InvoiceUnit.Service, 1_000, 1_00)] };

        using var pdf = PdfDocument.Open(InvoicePdf.ToPdf(model));

        var page = pdf.GetPage(1);
        var pieces = page.GetWords().Where(w => w.Text.Trim('x').Length == 0 && w.Text.Length > 0).ToList();
        Assert.Equal(word.Length, pieces.Sum(w => w.Text.Length));
        var unitLeft = page.GetWords().First(w => w.Text == "service").BoundingBox.Left;
        Assert.All(pieces, piece => Assert.True(piece.BoundingBox.Right < unitLeft));
    }

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(123_456_789, "1 234 567.89")]
    [InlineData(100_000, "1 000.00")]
    public void Money_groups_thousands_and_keeps_two_decimals(long minor, string expected) =>
        Assert.Equal(expected.Replace(' ', '\u00a0'), InvoicePdf.Money(minor));

    [Theory]
    [InlineData(1_000, "1")]
    [InlineData(1_500, "1.5")]
    [InlineData(1, "0.001")]
    [InlineData(12_340, "12.34")]
    public void Quantity_prints_without_trailing_zeros(long thousandths, string expected) =>
        Assert.Equal(expected, InvoicePdf.Quantity(thousandths));

    // In drawing order, which keeps each table cell's text together where reading by position would
    // interleave the cells of one row, with every run of whitespace (a wrapped line, a no-break space
    // in a figure) read as one space.
    internal static string Text(PdfDocument pdf) => string.Join(
        ' ',
        Enumerable.Range(1, pdf.NumberOfPages).Select(page =>
            string.Join(' ', ContentOrderTextExtractor.GetText(pdf.GetPage(page))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

    private static InvoicePdfModel Model(byte[]? signature) => new(
        "2031-007",
        InvoiceStatus.Issued,
        new DateOnly(2031, 5, 1),
        new DateOnly(2031, 5, 15),
        Currency.EUR,
        [new InvoiceLine("Consulting", "Консультації", InvoiceUnit.Hour, 10_000, 100_00)],
        1_000_00,
        new InvoiceSnapshot(
            new InvoiceSeller("ФОП Тест Тестович", "FOP Test Testovych", "1234567890", "Київ", "Kyiv"),
            new InvoiceBuyer("Acme GmbH", "1 Hauptstraße, Berlin", "DE", "Germany", null, null),
            new InvoicePayment(InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", "", "", ""),
            new InvoiceClauses(
                InvoicingDefaults.AcceptanceEn, InvoicingDefaults.AcceptanceUk, InvoicingDefaults.FeesEn,
                InvoicingDefaults.FeesUk, InvoicingDefaults.TaxStatusEn, InvoicingDefaults.TaxStatusUk)),
        signature);
}
