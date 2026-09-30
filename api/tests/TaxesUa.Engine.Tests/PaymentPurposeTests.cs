namespace TaxesUa.Engine.Tests;

public class PaymentPurposeTests
{
    [Theory]
    [InlineData(PaymentKind.SingleTax, "101 єдиний податок за III квартал 2026 року")]
    [InlineData(PaymentKind.MilitaryLevy, "101 військовий збір за III квартал 2026 року")]
    [InlineData(PaymentKind.Esv, "101 єдиний внесок за III квартал 2026 року")]
    public void Each_kind_is_named_in_words(PaymentKind kind, string expected) =>
        Assert.Equal(expected, PaymentPurpose.ForQuarter(kind, 2026, 3));

    [Theory]
    [InlineData(1, "101 єдиний податок за I квартал 2026 року")]
    [InlineData(2, "101 єдиний податок за II квартал 2026 року")]
    [InlineData(3, "101 єдиний податок за III квартал 2026 року")]
    [InlineData(4, "101 єдиний податок за IV квартал 2026 року")]
    public void A_quarter_is_a_roman_numeral(int quarter, string expected) =>
        Assert.Equal(expected, PaymentPurpose.ForQuarter(PaymentKind.SingleTax, 2026, quarter));

    [Theory]
    [InlineData(1, "101 єдиний внесок за січень 2026 року")]
    [InlineData(2, "101 єдиний внесок за лютий 2026 року")]
    [InlineData(3, "101 єдиний внесок за березень 2026 року")]
    [InlineData(4, "101 єдиний внесок за квітень 2026 року")]
    [InlineData(5, "101 єдиний внесок за травень 2026 року")]
    [InlineData(6, "101 єдиний внесок за червень 2026 року")]
    [InlineData(7, "101 єдиний внесок за липень 2026 року")]
    [InlineData(8, "101 єдиний внесок за серпень 2026 року")]
    [InlineData(9, "101 єдиний внесок за вересень 2026 року")]
    [InlineData(10, "101 єдиний внесок за жовтень 2026 року")]
    [InlineData(11, "101 єдиний внесок за листопад 2026 року")]
    [InlineData(12, "101 єдиний внесок за грудень 2026 року")]
    public void A_month_is_in_the_nominative(int month, string expected) =>
        Assert.Equal(expected, PaymentPurpose.ForMonth(PaymentKind.Esv, 2026, month));

    [Fact]
    public void The_year_follows_the_period_across_a_year_boundary()
    {
        Assert.Equal("101 єдиний податок за IV квартал 2026 року", PaymentPurpose.ForQuarter(PaymentKind.SingleTax, 2026, 4));
        Assert.Equal("101 єдиний податок за I квартал 2027 року", PaymentPurpose.ForQuarter(PaymentKind.SingleTax, 2027, 1));
        Assert.Equal("101 єдиний внесок за грудень 2026 року", PaymentPurpose.ForMonth(PaymentKind.Esv, 2026, 12));
        Assert.Equal("101 єдиний внесок за січень 2027 року", PaymentPurpose.ForMonth(PaymentKind.Esv, 2027, 1));
    }

    [Fact]
    public void A_purpose_has_no_separators_of_the_old_format_and_fits_the_limit()
    {
        var purposes = Enum.GetValues<PaymentKind>().SelectMany(kind => Enumerable.Range(1, 12)
            .Select(month => PaymentPurpose.ForMonth(kind, 2026, month))
            .Concat(Enumerable.Range(1, 4).Select(quarter => PaymentPurpose.ForQuarter(kind, 2026, quarter))));

        Assert.All(purposes, purpose =>
        {
            Assert.DoesNotContain('*', purpose);
            Assert.DoesNotContain(';', purpose);
            Assert.True(purpose.Length <= 420);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_quarter_outside_1_to_4_throws(int quarter) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentPurpose.ForQuarter(PaymentKind.SingleTax, 2026, quarter));

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void A_month_outside_1_to_12_throws(int month) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentPurpose.ForMonth(PaymentKind.Esv, 2026, month));

    [Fact]
    public void An_undefined_kind_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentPurpose.ForQuarter((PaymentKind)7, 2026, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentPurpose.ForMonth((PaymentKind)7, 2026, 1));
    }
}
