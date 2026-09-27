using TaxesUa.Api.Features.Fx;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Transactions;

internal sealed class Transaction
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly ValueDate { get; set; }

    public long AmountMinor { get; set; }

    public Currency Currency { get; set; } = Currency.UAH;

    public int RateE4 { get; set; } = Money.RateScale;

    public DateOnly? RateDate { get; set; }

    public RateSource? RateSource { get; set; }

    // Fixed at write time (Rule 2): a later change to the rate table must not move income that was
    // already recorded.
    public long AmountUahKop { get; set; }

    public TransactionKind Kind { get; set; }

    public string? NonIncomeReason { get; set; }

    public Guid? ClientId { get; set; }

    public Client? Client { get; set; }

    public Guid? RefundsTransactionId { get; set; }

    public Transaction? RefundsTransaction { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public TransactionInput ToEngineInput() => Kind switch
    {
        TransactionKind.Income => new TransactionInput.Income(ValueDate, AmountUahKop),
        TransactionKind.RefundToClient =>
            new TransactionInput.RefundToClient(ValueDate, AmountUahKop, RefundedReceiptValueDate()),
        _ => new TransactionInput.NonIncome(ValueDate, AmountUahKop, ToNonIncomeKind(Kind), NonIncomeReason!),
    };

    // A link whose receipt was not loaded would silently count a refund Rule 8 excludes, and a wrong
    // tax figure is worse than a crash.
    private DateOnly? RefundedReceiptValueDate() => RefundsTransactionId is null
        ? null
        : RefundsTransaction?.ValueDate ?? throw new InvalidOperationException(
            $"Transaction {Id} links receipt {RefundsTransactionId} but it was not loaded.");

    private static NonIncomeKind ToNonIncomeKind(TransactionKind kind) => kind switch
    {
        TransactionKind.OwnTransfer => NonIncomeKind.OwnTransfer,
        TransactionKind.FxSale => NonIncomeKind.FxSale,
        TransactionKind.OwnDeposit => NonIncomeKind.OwnDeposit,
        TransactionKind.ErroneousReturn => NonIncomeKind.ErroneousReturn,
        TransactionKind.OtherNonIncome => NonIncomeKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a non-income kind"),
    };
}

internal enum TransactionKind
{
    Income,
    RefundToClient,
    OwnTransfer,
    FxSale,
    OwnDeposit,
    ErroneousReturn,
    OtherNonIncome,
}
