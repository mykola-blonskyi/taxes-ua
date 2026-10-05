namespace TaxesUa.Engine.Tests;

public class LimitMonitorTests
{
    // A round 10 000 000 kop limit keeps 85%/100% boundaries exact kopeck amounts.
    private static readonly TaxYearConfigInput Config = new(
        MinWageKop: 1_000_000,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 100,
        EsvRateBp: 2_200,
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        Holidays: [],
        IncomeLimitKop: 10_000_000,
        ExcessRateBp: 1_500,
        LimitWarnThresholdsPct: [85, 100],
        Group3ApplicationDays: 10);

    [Fact]
    public void Below_the_warn_threshold_is_Ok()
    {
        var status = LimitMonitor.Evaluate(8_499_000, Config);

        Assert.Equal(LimitLevel.Ok, status.Level);
        Assert.Equal(8_499, status.PercentBp);
    }

    [Fact]
    public void Exactly_the_warn_threshold_is_Warn()
    {
        var status = LimitMonitor.Evaluate(8_500_000, Config);

        Assert.Equal(LimitLevel.Warn, status.Level);
    }

    [Fact]
    public void Exactly_the_limit_is_Exceeded_with_no_excess()
    {
        var status = LimitMonitor.Evaluate(10_000_000, Config);

        Assert.Equal(LimitLevel.Exceeded, status.Level);
        Assert.Equal(0, status.ExcessKop);
        Assert.Equal(0, status.ExcessTaxKop);
    }

    [Fact]
    public void One_kopeck_over_the_limit_taxes_the_excess()
    {
        var status = LimitMonitor.Evaluate(10_000_001, Config);

        Assert.Equal(LimitLevel.Exceeded, status.Level);
        Assert.Equal(1, status.ExcessKop);

        // Money.ApplyBp(1, 1_500) = DivRoundHalfUp(1 * 1_500, 10_000): remainder 1_500, its double
        // (3_000) does not reach the denominator (10_000), so it rounds down to 0.
        Assert.Equal(0, status.ExcessTaxKop);
    }

    [Fact]
    public void RemainingKop_counts_down_to_the_warn_threshold_while_Ok()
    {
        var status = LimitMonitor.Evaluate(8_000_000, Config);

        Assert.Equal(LimitLevel.Ok, status.Level);
        Assert.Equal(500_000, status.RemainingKop);
    }

    [Fact]
    public void RemainingKop_counts_down_to_the_limit_itself_while_Warn()
    {
        var status = LimitMonitor.Evaluate(9_000_000, Config);

        Assert.Equal(LimitLevel.Warn, status.Level);
        Assert.Equal(1_000_000, status.RemainingKop);
    }

    [Fact]
    public void Exceeded_does_not_depend_on_any_configured_warn_threshold()
    {
        var configWithNoWarnThresholds = Config with { LimitWarnThresholdsPct = [] };

        var status = LimitMonitor.Evaluate(10_000_000, configWithNoWarnThresholds);

        Assert.Equal(LimitLevel.Exceeded, status.Level);
        Assert.Equal(0, status.RemainingKop);
    }

    // A refund in January of a December receipt, before any new income, leaves the year negative.
    [Fact]
    public void Negative_income_uses_none_of_the_limit()
    {
        var status = LimitMonitor.Evaluate(-100_000, Config);

        Assert.Equal(new LimitStatus(0, 10_000_000, 0, LimitLevel.Ok, 8_500_000, 0, 0), status);
    }

    // The 2026 limit, 1_167 x 864_700 kop: one basis point of it is about 1_009 UAH, so a percentage
    // rounded to whole basis points would call income just under 85% Warn.
    private static readonly TaxYearConfigInput Config2026 = Config with { IncomeLimitKop = 1_009_104_900 };

    private const long Exactly85PercentOf2026Kop = 857_739_165;

    [Fact]
    public void One_kopeck_under_85_percent_of_the_2026_limit_is_Ok()
    {
        var status = LimitMonitor.Evaluate(Exactly85PercentOf2026Kop - 1, Config2026);

        Assert.Equal((LimitLevel.Ok, 1L), (status.Level, status.RemainingKop));
    }

    [Fact]
    public void Exactly_85_percent_of_the_2026_limit_is_Warn()
    {
        var status = LimitMonitor.Evaluate(Exactly85PercentOf2026Kop, Config2026);

        Assert.Equal((LimitLevel.Warn, 1_009_104_900L - Exactly85PercentOf2026Kop), (status.Level, status.RemainingKop));
    }
}
