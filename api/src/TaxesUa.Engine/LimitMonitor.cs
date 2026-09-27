namespace TaxesUa.Engine;

public enum LimitLevel
{
    Ok,
    Warn,
    Exceeded,
}

/// <summary>
/// Where a year's income sits against its annual limit (Rule 4). <c>Exceeded</c> is a business fact
/// (income at or over the limit, whatever the configured warn thresholds are) because it triggers a
/// forced tax-system switch, not a UI-only warning. <c>RemainingKop</c> is the amount left before the
/// next boundary the caller has not yet crossed (a warn threshold, or the limit itself once every warn
/// threshold has been crossed); it is 0 once <c>Level</c> is <c>Exceeded</c>, where
/// <c>ExcessKop</c>/<c>ExcessTaxKop</c> apply instead.
/// </summary>
public sealed record LimitStatus(
    long IncomeKop,
    long LimitKop,
    int PercentBp,
    LimitLevel Level,
    long RemainingKop,
    long ExcessKop,
    long ExcessTaxKop);

/// <summary>
/// Rule 4 of <c>knowledge/business-rules.md</c>. Pure: the year's cumulative income and the year's
/// config arrive as arguments; nothing is recomputed here that <see cref="IncomeLedger"/> already
/// computed.
/// </summary>
public static class LimitMonitor
{
    public static LimitStatus Evaluate(long incomeKop, TaxYearConfigInput config)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(incomeKop);

        var limitKop = config.IncomeLimitKop;
        var percentBp = limitKop > 0 ? (int)Money.ShareBp(incomeKop, limitKop) : 0;
        var exceeded = incomeKop >= limitKop;

        var warnThresholds = config.LimitWarnThresholdsPct.Where(pct => pct < 100).OrderBy(pct => pct).ToArray();
        var warned = warnThresholds.Any(pct => pct * 100L <= percentBp);
        var level = exceeded ? LimitLevel.Exceeded : warned ? LimitLevel.Warn : LimitLevel.Ok;

        var nextThresholdPct = warnThresholds.Where(pct => pct * 100L > percentBp).Select(pct => (int?)pct).FirstOrDefault();
        var nextBoundaryKop = nextThresholdPct is { } pct ? Money.Prorate(limitKop, pct, 100) : limitKop;
        var remainingKop = exceeded ? 0 : Math.Max(0, nextBoundaryKop - incomeKop);

        var excessKop = exceeded ? incomeKop - limitKop : 0;
        var excessTaxKop = Money.ApplyBp(excessKop, config.ExcessRateBp);

        return new LimitStatus(incomeKop, limitKop, percentBp, level, remainingKop, excessKop, excessTaxKop);
    }
}
