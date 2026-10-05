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
/// <c>ExcessKop</c>/<c>ExcessTaxKop</c> apply instead. <c>IncomeKop</c> is the income used, so a negative
/// year reads as 0.
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
        // A refund of last year's receipt can leave the year negative (Rule 1); none of the limit is used.
        incomeKop = Math.Max(incomeKop, 0);

        var limitKop = config.IncomeLimitKop;
        var percentBp = limitKop > 0 ? (int)Money.ShareBp(incomeKop, limitKop) : 0;
        var exceeded = incomeKop >= limitKop;

        var warnThresholds = config.LimitWarnThresholdsPct.Where(pct => pct < 100).OrderBy(pct => pct).ToArray();
        var warned = warnThresholds.Any(pct => Reached(incomeKop, limitKop, pct));
        var level = exceeded ? LimitLevel.Exceeded : warned ? LimitLevel.Warn : LimitLevel.Ok;

        var nextThresholdPct = warnThresholds.Where(pct => !Reached(incomeKop, limitKop, pct)).Select(pct => (int?)pct).FirstOrDefault();
        var nextBoundaryKop = nextThresholdPct is { } pct ? Money.Prorate(limitKop, pct, 100) : limitKop;
        var remainingKop = exceeded ? 0 : Math.Max(0, nextBoundaryKop - incomeKop);

        var excessKop = exceeded ? incomeKop - limitKop : 0;
        var excessTaxKop = Money.ApplyBp(excessKop, config.ExcessRateBp);

        return new LimitStatus(incomeKop, limitKop, percentBp, level, remainingKop, excessKop, excessTaxKop);
    }

    // In kopecks, not the rounded PercentBp: one basis point of the limit is about 1,009 UAH (Rule 4).
    private static bool Reached(long incomeKop, long limitKop, int pct) => incomeKop * 100 >= limitKop * pct;
}
