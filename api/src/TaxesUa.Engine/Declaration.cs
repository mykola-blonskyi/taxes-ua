namespace TaxesUa.Engine;

/// <summary>
/// The group 3 lines of the single tax declaration (form F0103309) for one reporting period, per
/// Rule 15 of <c>knowledge/business-rules.md</c>. The period is cumulative from 1 January: Q1 is the
/// quarter, Q2 the half-year, Q3 nine months, Q4 the year; after a return to group 3 mid-year (Rule 4)
/// it runs from the quarter of the return instead. The lines of other groups, of the 3% rate
/// and of corrections are not here.
/// </summary>
/// <param name="IncomeKop">Line 06, income taxed at 5%: the income through the period up to the year's
/// limit.</param>
/// <param name="ExcessIncomeKop">Line 07, income taxed at 15%: the income through the period over the
/// limit (Rule 4), nonzero only in the quarter the limit is crossed in.</param>
/// <param name="SingleTaxKop">Line 11, line 06 at the single tax rate.</param>
/// <param name="ExcessTaxKop">Line 09, line 07 at the excess rate.</param>
/// <param name="PreviousSingleTaxKop">Line 13, line 12 of the previous quarter's declaration of the
/// same period; zero for its first quarter.</param>
/// <param name="SingleTaxPayableKop">Line 14.1, and with 14.2 empty line 14: line 12 minus line 13.
/// Negative when refunds shrank the cumulative income; the form has no separate line for that.</param>
/// <param name="MilitaryLevyKop">Line 23, the military levy on lines 05 to 07.</param>
/// <param name="PreviousMilitaryLevyKop">Line 24, line 23 of the previous quarter's declaration;
/// zero for the period's first quarter.</param>
/// <param name="MilitaryLevyPayableKop">Line 25, line 23 minus line 24, negative like line
/// 14.1.</param>
/// <param name="EsvAnnex">Annex 1, on the year's last group 3 declaration only: Q4, or the quarter
/// the limit was crossed in; null for every other quarter and when no month owes ESV.</param>
public sealed record DeclarationFigures(
    int Year,
    int Quarter,
    long IncomeKop,
    long ExcessIncomeKop,
    long SingleTaxKop,
    long ExcessTaxKop,
    long PreviousSingleTaxKop,
    long SingleTaxPayableKop,
    long MilitaryLevyKop,
    long PreviousMilitaryLevyKop,
    long MilitaryLevyPayableKop,
    EsvAnnex? EsvAnnex)
{
    /// <summary>Line 21, the annex's total ESV.</summary>
    public long? EsvKop => EsvAnnex?.EsvKop;

    /// <summary>Line 08, lines 06 and 07.</summary>
    public long TotalIncomeKop => IncomeKop + ExcessIncomeKop;

    /// <summary>Line 12, lines 09 and 11.</summary>
    public long TotalSingleTaxKop => SingleTaxKop + ExcessTaxKop;
}

/// <summary>
/// Reads the declaration off a year's accruals. It never applies a rate itself, so the declaration
/// and the periods screen cannot disagree by a kopeck.
/// </summary>
public static class Declaration
{
    /// <summary>
    /// A quarter after a limit crossing is outside group 3 (Rule 4) until the owner is back on it, and
    /// has no group 3 declaration, so asking for one is a caller error.
    /// </summary>
    public static DeclarationFigures ForQuarter(YearAccrual year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        if (!year.InGroup3(quarter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(quarter), quarter, $"Quarter {quarter} of {year.Year} is not in group 3.");
        }

        var current = year.QuarterOf(quarter);
        return new DeclarationFigures(
            year.Year,
            quarter,
            current.Income.CumulativeIncomeKop - current.CumulativeExcessIncomeKop,
            current.CumulativeExcessIncomeKop,
            current.CumulativeSingleTaxKop - current.CumulativeExcessTaxKop,
            current.CumulativeExcessTaxKop,
            current.CumulativeSingleTaxKop - current.SingleTaxKop,
            current.SingleTaxKop,
            current.CumulativeMilitaryLevyKop,
            current.CumulativeMilitaryLevyKop - current.MilitaryLevyKop,
            current.MilitaryLevyKop,
            year.EsvAnnex?.Quarter == quarter ? year.EsvAnnex : null);
    }
}
