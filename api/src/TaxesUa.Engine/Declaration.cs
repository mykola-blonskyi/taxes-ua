namespace TaxesUa.Engine;

/// <summary>
/// The group 3 lines of the single tax declaration (form F0103309) for one reporting period, per
/// Rule 15 of <c>knowledge/business-rules.md</c>. The period is cumulative from 1 January: Q1 is the
/// quarter, Q2 the half-year, Q3 nine months, Q4 the year. The lines of other groups, of the 3% rate
/// and of corrections are not here.
/// </summary>
/// <param name="IncomeKop">Line 06, income taxed at 5%: the income through the period up to the year's
/// limit.</param>
/// <param name="ExcessIncomeKop">Line 07, income taxed at 15%: the income through the period over the
/// limit (Rule 4), nonzero only in the quarter the limit is crossed in.</param>
/// <param name="SingleTaxKop">Line 11, line 06 at the single tax rate.</param>
/// <param name="ExcessTaxKop">Line 09, line 07 at the excess rate.</param>
/// <param name="PreviousSingleTaxKop">Line 13, line 12 of the previous quarter's declaration of the
/// same year; zero for Q1.</param>
/// <param name="SingleTaxPayableKop">Line 14.1, and with 14.2 empty line 14: line 12 minus line 13.
/// Negative when refunds shrank the cumulative income; the form has no separate line for that.</param>
/// <param name="MilitaryLevyKop">Line 23, the military levy on lines 05 to 07.</param>
/// <param name="PreviousMilitaryLevyKop">Line 24, line 23 of the previous quarter's declaration;
/// zero for Q1.</param>
/// <param name="MilitaryLevyPayableKop">Line 25, line 23 minus line 24, negative like line
/// 14.1.</param>
/// <param name="EsvKop">Line 21, the year's ESV from annex 1, which only the annual declaration
/// carries; null for Q1 to Q3.</param>
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
    long? EsvKop)
{
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
    /// A quarter after the one the limit was crossed in is outside group 3 (Rule 4) and has no group 3
    /// declaration, so asking for one is a caller error.
    /// </summary>
    public static DeclarationFigures ForQuarter(YearAccrual year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);
        if (!year.InGroup3(quarter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(quarter), quarter, $"Group 3 ended with quarter {year.Quarters.Count} of {year.Year}.");
        }

        var current = year.Quarters[quarter - 1];
        var previous = quarter == 1 ? null : year.Quarters[quarter - 2];
        return new DeclarationFigures(
            year.Year,
            quarter,
            current.Income.CumulativeIncomeKop - current.CumulativeExcessIncomeKop,
            current.CumulativeExcessIncomeKop,
            current.CumulativeSingleTaxKop - current.CumulativeExcessTaxKop,
            current.CumulativeExcessTaxKop,
            previous?.CumulativeSingleTaxKop ?? 0,
            current.SingleTaxKop,
            current.CumulativeMilitaryLevyKop,
            previous?.CumulativeMilitaryLevyKop ?? 0,
            current.MilitaryLevyKop,
            quarter == 4 ? year.Quarters.Sum(accrual => accrual.EsvKop) : null);
    }
}
