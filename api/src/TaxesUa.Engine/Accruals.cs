namespace TaxesUa.Engine;

/// <summary>
/// What ESV the month of registration costs. Rule 3 calls the full amount the default and leaves the
/// alternative to be confirmed, so <c>Prorated</c> charges the month by its active days. The owner
/// still has to settle that: ESV for oneself is a fixed monthly sum, so a part-month accrual leaves
/// Rule 7 with a balance no payment clears, and the alternative may be a month charged at zero.
/// </summary>
public enum EsvRegistrationMonthPolicy
{
    FullMonth,
    Prorated,
}

/// <summary>
/// One quarter of the declaration. The cumulative figures are what the declaration carries; the
/// per-quarter tax is the cumulative figure minus what the earlier quarters already accrued, and is
/// negative when refunds shrank the cumulative income (Rule 3, settled per Rule 7).
/// </summary>
public sealed record QuarterAccrual(
    QuarterIncome Income,
    long SingleTaxKop,
    long CumulativeSingleTaxKop,
    long MilitaryLevyKop,
    long CumulativeMilitaryLevyKop,
    long EsvKop)
{
    public long TotalKop => SingleTaxKop + MilitaryLevyKop + EsvKop;
}

/// <summary>A year of accruals, with the ledger's monthly income beside the quarterly figures.</summary>
public sealed record YearAccrual(
    int Year,
    IReadOnlyList<MonthIncome> Months,
    IReadOnlyList<QuarterAccrual> Quarters,
    IReadOnlyList<EngineWarning> Warnings);

/// <summary>
/// The single tax, the military levy and ESV for a year, per Rule 3 of
/// <c>knowledge/business-rules.md</c>. Pure: the year, the rates and the registration date all
/// arrive as arguments.
/// </summary>
public static class Accruals
{
    public static YearAccrual ForYear(
        int year,
        IReadOnlyList<TransactionInput> transactions,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        var income = IncomeLedger.ForYear(year, transactions, settings);
        var esvByMonthKop = EsvByMonth(year, config, settings);

        var quarters = new QuarterAccrual[4];
        var accruedSingleTaxKop = 0L;
        var accruedMilitaryLevyKop = 0L;
        foreach (var quarterIncome in income.Quarters)
        {
            var cumulativeSingleTaxKop =
                Money.ApplyBp(quarterIncome.CumulativeIncomeKop, config.SingleTaxRateBp);
            var cumulativeMilitaryLevyKop =
                Money.ApplyBp(quarterIncome.CumulativeIncomeKop, config.MilitaryLevyRateBp);

            quarters[quarterIncome.Quarter - 1] = new QuarterAccrual(
                quarterIncome,
                cumulativeSingleTaxKop - accruedSingleTaxKop,
                cumulativeSingleTaxKop,
                cumulativeMilitaryLevyKop - accruedMilitaryLevyKop,
                cumulativeMilitaryLevyKop,
                esvByMonthKop[(3 * quarterIncome.Quarter - 3)..(3 * quarterIncome.Quarter)].Sum());

            accruedSingleTaxKop = cumulativeSingleTaxKop;
            accruedMilitaryLevyKop = cumulativeMilitaryLevyKop;
        }

        return new YearAccrual(year, income.Months, quarters, income.Warnings);
    }

    private static long[] EsvByMonth(
        int year,
        TaxYearConfigInput config,
        FopSettingsInput settings)
    {
        var esvKop = new long[12];
        if (settings.EsvExempt || settings.FopRegistrationDate is not { } registrationDate)
        {
            return esvKop;
        }

        var monthKop = Money.ApplyBp(config.MinWageKop, config.EsvRateBp);
        for (var month = 1; month <= 12; month++)
        {
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd < registrationDate)
            {
                continue;
            }

            esvKop[month - 1] = monthStart >= registrationDate
                ? monthKop
                : RegistrationMonthKop(monthKop, registrationDate, monthEnd, settings);
        }

        return esvKop;
    }

    private static long RegistrationMonthKop(
        long monthKop,
        DateOnly registrationDate,
        DateOnly monthEnd,
        FopSettingsInput settings) =>
        settings.EsvRegistrationMonthPolicy switch
        {
            EsvRegistrationMonthPolicy.FullMonth => monthKop,
            EsvRegistrationMonthPolicy.Prorated => Money.Prorate(
                monthKop, monthEnd.Day - registrationDate.Day + 1, monthEnd.Day),
            var policy => throw new ArgumentOutOfRangeException(
                nameof(settings), policy, "Unknown ESV registration-month policy."),
        };
}
