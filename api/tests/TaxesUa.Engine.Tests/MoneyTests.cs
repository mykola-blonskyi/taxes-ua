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
    public void ApplyBp_rounds_half_up(long amountKop, int rateBp, long expectedKop) =>
        Assert.Equal(expectedKop, Money.ApplyBp(amountKop, rateBp));

    [Fact]
    public void ToRateE4_scales_nbu_rate() =>
        Assert.Equal(449_729, Money.ToRateE4(44.9729m));

    [Fact]
    public void Negative_refund_rounds_away_from_zero() =>
        Assert.Equal(-45, Money.ToUahKop(-1, 449_729));
}
