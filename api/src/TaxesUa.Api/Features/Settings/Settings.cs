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

    public string DefaultCurrency { get; set; } = "UAH";
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
