using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

/// <summary>
/// One actual payment into the budget. Exactly one of <see cref="PeriodQuarter"/> and
/// <see cref="PeriodMonth"/> is set: the endpoint validates it and a check constraint holds it in the
/// database, so <see cref="ToEngineInput"/> can name the engine's period case without a third branch.
/// </summary>
internal sealed class BudgetPayment
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly PaidOn { get; set; }

    public PaymentKind Kind { get; set; }

    public long AmountKop { get; set; }

    public int PeriodYear { get; set; }

    public int? PeriodQuarter { get; set; }

    public int? PeriodMonth { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public BudgetPaymentInput ToEngineInput() => new(
        Kind,
        AmountKop,
        PeriodYear,
        PeriodMonth is { } month
            ? new PaymentPeriod.Monthly(month)
            : new PaymentPeriod.Quarterly(PeriodQuarter!.Value));
}
