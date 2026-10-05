using EngineEsvRegistrationMonthPolicy = TaxesUa.Engine.EsvRegistrationMonthPolicy;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Settings;

internal sealed class Settings
{
    public string UserId { get; set; } = string.Empty;

    public DateOnly? FopRegistrationDate { get; set; }

    // The initialisers are the defaults knowledge/domain-model.md documents. They live here so a GET
    // for an owner who has never saved can answer from a fresh instance instead of storing a row.
    public PaymentMode PaymentMode { get; set; } = PaymentMode.Quarterly;

    public EsvRegistrationMonthPolicy EsvRegistrationMonthPolicy { get; set; } =
        EsvRegistrationMonthPolicy.FullMonth;

    public bool EsvExempt { get; set; }

    public bool TaxPaymentCountsFromStatutoryDeclarationDate { get; set; } = true;

    public bool ShiftTaxPaymentFromWeekend { get; set; } = true;

    public DayOfWeek[] WeekendDays { get; set; } = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    public string Locale { get; set; } = "uk";

    public string Theme { get; set; } = "system";

    // When the owner chose each, in UTC, so the newest choice from any device wins (ADR-032). Null is a
    // value from before the times were kept, or a restore, and counts as the oldest.
    public DateTimeOffset? LocaleChosenAt { get; set; }

    public DateTimeOffset? ThemeChosenAt { get; set; }

    public string DefaultCurrency { get; set; } = "UAH";

    // Rule 4: the quarter the FOP is back on group 3 from after a limit crossing. Both set or both null,
    // which SettingsEndpoints.Apply keeps by writing them from one YearQuarter.
    public int? BackOnGroup3FromYear { get; set; }

    public int? BackOnGroup3FromQuarter { get; set; }

    public YearQuarter? BackOnGroup3From =>
        BackOnGroup3FromYear is { } year && BackOnGroup3FromQuarter is { } quarter ? new YearQuarter(year, quarter) : null;

    // Tax Code 298.1.4: the day the DPS register has group 3 from when it is later than registration.
    // Null means from the registration date.
    public DateOnly? Group3Since { get; set; }

    // The DPS receipt for the group 3 application. Both set or both null, which DpsStatusEndpoints
    // keeps by writing them from one confirmation.
    public DateOnly? Group3ConfirmedOn { get; set; }

    public string? Group3ReceiptNumber { get; set; }

    public bool DpsFopRegistered { get; set; }

    public bool DpsEsvRegistered { get; set; }

    public bool DpsAccountsRegistered { get; set; }

    public FopSettingsInput ToEngineInput() => new(
        WeekendDays,
        TaxPaymentCountsFromStatutoryDeclarationDate,
        ShiftTaxPaymentFromWeekend,
        FopRegistrationDate,
        EsvRegistrationMonthPolicy switch
        {
            EsvRegistrationMonthPolicy.FullMonth => EngineEsvRegistrationMonthPolicy.FullMonth,
            EsvRegistrationMonthPolicy.Prorated => EngineEsvRegistrationMonthPolicy.Prorated,
            _ => throw new ArgumentOutOfRangeException(
                nameof(EsvRegistrationMonthPolicy), EsvRegistrationMonthPolicy, message: null),
        },
        EsvExempt,
        BackOnGroup3From,
        Group3Since,
        Group3ConfirmedOn is not null);
}

internal enum PaymentMode
{
    Quarterly,
    MonthlyAdvance,
}

internal enum EsvRegistrationMonthPolicy
{
    FullMonth,
    Prorated,
}
