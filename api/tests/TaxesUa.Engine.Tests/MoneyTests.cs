using TaxesUa.Engine;

namespace TaxesUa.Engine.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(10_000, 449_729, 449_729)]
    [InlineData(1, 449_729, 45)]
    [InlineData(300_000, 423_532, 12_705_960)]
    [InlineData(100, 10_000, 100)]
    public void ToUahKop_rounds_half_up(long amountMinor, int rateE4, long expectedKop) =>
        Assert.Equal(expectedKop, Money.ToUahKop(amountMinor, rateE4));

    [Theory]
    [InlineData(864_700, 2_200, 190_234)]
    [InlineData(1_000_000, 500, 50_000)]
    [InlineData(1_000_000, 100, 10_000)]
    [InlineData(1, 500, 0)]
    [InlineData(10, 500, 1)]
    [InlineData(-100_010, 500, -5_001)]
    public void ApplyBp_rounds_half_up(long amountKop, int rateBp, long expectedKop) =>
        Assert.Equal(expectedKop, Money.ApplyBp(amountKop, rateBp));

    [Theory]
    [InlineData(190_234, 14, 28, 95_117)]
    [InlineData(190_234, 22, 31, 135_005)]
    [InlineData(190_234, 31, 31, 190_234)]
    [InlineData(190_234, 0, 28, 0)]
    public void Prorate_rounds_half_up(long amountKop, int part, int whole, long expectedKop) =>
        Assert.Equal(expectedKop, Money.Prorate(amountKop, part, whole));

    [Fact]
    public void ToRateE4_scales_nbu_rate() =>
        Assert.Equal(449_729, Money.ToRateE4(44.9729m));

    [Fact]
    public void Negative_refund_rounds_away_from_zero() =>
        Assert.Equal(-45, Money.ToUahKop(-1, 449_729));
}
