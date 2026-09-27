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
        LimitWarnThresholdsPct: [85, 100]);

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

    [Fact]
    public void Negative_income_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LimitMonitor.Evaluate(-1, Config));
}
