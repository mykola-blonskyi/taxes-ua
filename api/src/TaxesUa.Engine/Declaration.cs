namespace TaxesUa.Engine;

/// <summary>
/// The group 3 lines of the single tax declaration (form F0103309) for one reporting period, per
/// Rule 14 of <c>knowledge/business-rules.md</c>. The period is cumulative from 1 January: Q1 is the
/// quarter, Q2 the half-year, Q3 nine months, Q4 the year. Only the 5% lines are here: the 15% lines
/// for income over the limit are not accrued yet (#118), and the other lines belong to other groups
/// or to corrections.
/// </summary>
/// <param name="IncomeKop">Line 06, income taxed at 5%, which is also the total of line 08.</param>
/// <param name="SingleTaxKop">Line 11, the tax at 5%, which is also the total of line 12.</param>
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
    long SingleTaxKop,
    long PreviousSingleTaxKop,
    long SingleTaxPayableKop,
    long MilitaryLevyKop,
    long PreviousMilitaryLevyKop,
    long MilitaryLevyPayableKop,
    long? EsvKop);

/// <summary>
/// Reads the declaration off a year's accruals. It never applies a rate itself, so the declaration
/// and the periods screen cannot disagree by a kopeck.
/// </summary>
public static class Declaration
{
    public static DeclarationFigures ForQuarter(YearAccrual year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);

        var current = year.Quarters[quarter - 1];
        var previous = quarter == 1 ? null : year.Quarters[quarter - 2];
        return new DeclarationFigures(
            year.Year,
            quarter,
            current.Income.CumulativeIncomeKop,
            current.CumulativeSingleTaxKop,
            previous?.CumulativeSingleTaxKop ?? 0,
            current.SingleTaxKop,
            current.CumulativeMilitaryLevyKop,
            previous?.CumulativeMilitaryLevyKop ?? 0,
            current.MilitaryLevyKop,
            quarter == 4 ? year.Quarters.Sum(accrual => accrual.EsvKop) : null);
    }
}
