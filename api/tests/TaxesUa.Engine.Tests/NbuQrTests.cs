using System.Buffers.Text;
using System.Text;

namespace TaxesUa.Engine.Tests;

public class NbuQrTests
{
    private const string Name = "ГУК у м.Києві/Печерський р-н/18050400";

    private const string Iban = "UA358999980333159998000026011";

    private const string Code = "37993783";

    private static readonly string SingleTaxQ3 = PaymentPurpose.ForQuarter(PaymentKind.SingleTax, 2026, 3);

    [Fact]
    public void A_single_tax_transfer_matches_the_golden_content()
    {
        Assert.Equal(
            "https://qr.bank.gov.ua/QkNECjAwMwoxClVDVAoK0JPQo9CaINGDINC8LtCa0LjRlNCy0ZYv0J_QtdGH0LXRgNGB0YzQutC40Lkg0YAt0L0vMTgwNTA0MDAKVUEzNTg5OTk5ODAzMzMxNTk5OTgwMDAwMjYwMTEKVUFIMTIzNC41MAozNzk5Mzc4MwpUQVhTL1RBWFMKCjEwMSDRlNC00LjQvdC40Lkg0L_QvtC00LDRgtC-0Log0LfQsCBJSUkg0LrQstCw0YDRgtCw0LsgMjAyNiDRgNC-0LrRgwoKRkVGRgoKCg",
            NbuQr.Content(Name, Iban, Code, 123_450, SingleTaxQ3));
    }

    [Theory]
    [InlineData(PaymentKind.SingleTax, "101 єдиний податок за III квартал 2026 року")]
    [InlineData(PaymentKind.MilitaryLevy, "101 військовий збір за III квартал 2026 року")]
    [InlineData(PaymentKind.Esv, "101 єдиний внесок за III квартал 2026 року")]
    public void Each_kind_carries_its_purpose_in_the_seventeen_fields(PaymentKind kind, string purpose)
    {
        var content = NbuQr.Content(Name, Iban, Code, 123_450, PaymentPurpose.ForQuarter(kind, 2026, 3));

        Assert.Equal(
            ["BCD", "003", "1", "UCT", "", Name, Iban, "UAH1234.50", Code, "TAXS/TAXS", "", purpose, "", "FEFF", "", "", ""],
            Fields(content!));
    }

    [Fact]
    public void A_month_purpose_is_carried_as_well()
    {
        var content = NbuQr.Content(Name, Iban, Code, 190_234, PaymentPurpose.ForMonth(PaymentKind.Esv, 2026, 9));

        Assert.Equal("101 єдиний внесок за вересень 2026 року", Fields(content!)[11]);
        Assert.Equal("UAH1902.34", Fields(content!)[7]);
    }

    [Theory]
    [InlineData(300, "UAH3")]
    [InlineData(123_450, "UAH1234.50")]
    [InlineData(19_023, "UAH190.23")]
    [InlineData(100_000, "UAH1000")]
    [InlineData(5, "UAH0.05")]
    [InlineData(99_999_999_999, "UAH999999999.99")]
    public void The_amount_takes_the_shortest_form_with_two_digit_kopecks(long amountKop, string expected) =>
        Assert.Equal(expected, NbuQr.Amount(amountKop));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000_000_000)]
    public void An_amount_outside_the_format_gives_no_content(long amountKop) =>
        Assert.Null(NbuQr.Content(Name, Iban, Code, amountKop, SingleTaxQ3));

    [Fact]
    public void Cyrillic_counts_in_bytes_against_the_475_byte_encoded_limit()
    {
        var longest = Enumerable.Range(1, 140)
            .Last(n => NbuQr.Content(new string('Ж', n), Iban, Code, 123_450, SingleTaxQ3) is not null);
        var fits = NbuQr.Content(new string('Ж', longest), Iban, Code, 123_450, SingleTaxQ3)!;

        Assert.True(longest < 140);
        Assert.InRange(fits.Length - NbuQr.StartCode.Length, 472, NbuQr.MaxEncodedBytes);
        Assert.True(fits.Length <= NbuQr.MaxBytes);
        Assert.Null(NbuQr.Content(new string('Ж', longest + 1), Iban, Code, 123_450, SingleTaxQ3));
    }

    [Fact]
    public void A_field_over_its_length_gives_no_content()
    {
        Assert.Null(NbuQr.Content(new string('A', 141), Iban, Code, 100, SingleTaxQ3));
        Assert.NotNull(NbuQr.Content(new string('A', 140), Iban, Code, 100, SingleTaxQ3));
        Assert.Null(NbuQr.Content(Name, Iban, "12345678901", 100, SingleTaxQ3));
        Assert.Null(NbuQr.Content(Name, Iban, Code, 100, new string('a', 421)));
        Assert.Null(NbuQr.Content(Name, Iban[..28], Code, 100, SingleTaxQ3));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ГУК\nКиїв")]
    public void An_empty_name_or_a_line_break_in_it_gives_no_content(string name) =>
        Assert.Null(NbuQr.Content(name, Iban, Code, 100, SingleTaxQ3));

    private static string[] Fields(string content)
    {
        Assert.StartsWith(NbuQr.StartCode, content);
        var block = Base64Url.DecodeFromChars(content.AsSpan(NbuQr.StartCode.Length));
        return Encoding.UTF8.GetString(block).Split('\n');
    }
}
