using TaxesUa.Engine;

namespace TaxesUa.Api.Features.TaxYears;

internal sealed class TaxYearConfig
{
    public int Year { get; set; }

    public long MinWageKop { get; set; }

    public int SingleTaxRateBp { get; set; }

    public int MilitaryLevyRateBp { get; set; }

    public int EsvRateBp { get; set; }

    public int ExcessRateBp { get; set; }

    // Both derived fields are stored for transparency, so they are written only by RecomputeDerived.
    // A settable pair could be told a figure that disagrees with the inputs it is derived from.
    public long EsvMonthlyKop { get; private set; }

    public int IncomeLimitMinWages { get; set; }

    public long IncomeLimitKop { get; private set; }

    public int[] LimitWarnThresholdsPct { get; set; } = [];

    public int EsvDeadlineDay { get; set; }

    public int DeclarationDays { get; set; }

    public int TaxPaymentDaysAfterDeclaration { get; set; }

    public int AdvanceRecommendedDay { get; set; }

    public DateOnly[] Holidays { get; set; } = [];

    public string Source { get; set; } = string.Empty;

    // DateTimeOffset where knowledge/domain-model.md writes DateTime?, matching
    // ApplicationUser.CreatedAt and the timestamptz Npgsql maps it to.
    public DateTimeOffset? VerifiedAt { get; set; }

    public void RecomputeDerived()
    {
        EsvMonthlyKop = Money.ApplyBp(MinWageKop, EsvRateBp);
        IncomeLimitKop = checked(MinWageKop * IncomeLimitMinWages);
    }

    // VerifiedAt is left unset on purpose: a verification attests to the numbers someone compared
    // against the law, and nobody has compared the copy.
    public TaxYearConfig CloneTo(int year)
    {
        var copy = new TaxYearConfig
        {
            Year = year,
            MinWageKop = MinWageKop,
            SingleTaxRateBp = SingleTaxRateBp,
            MilitaryLevyRateBp = MilitaryLevyRateBp,
            EsvRateBp = EsvRateBp,
            ExcessRateBp = ExcessRateBp,
            IncomeLimitMinWages = IncomeLimitMinWages,
            LimitWarnThresholdsPct = [.. LimitWarnThresholdsPct],
            EsvDeadlineDay = EsvDeadlineDay,
            DeclarationDays = DeclarationDays,
            TaxPaymentDaysAfterDeclaration = TaxPaymentDaysAfterDeclaration,
            AdvanceRecommendedDay = AdvanceRecommendedDay,
            Holidays = [.. Holidays],
            Source = Source,
        };

        copy.RecomputeDerived();
        return copy;
    }
}
