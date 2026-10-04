using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Transactions;

internal sealed record TransactionRequest(
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    int? ManualRateE4,
    TransactionKind Kind,
    string? NonIncomeReason,
    string? ClientName,
    string? InvoiceNumber,
    string? Description,
    Guid? RefundsTransactionId);

internal sealed record TransactionResponse(
    Guid Id,
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    int RateE4,
    DateOnly? RateDate,
    RateSource? RateSource,
    long AmountUahKop,
    TransactionKind Kind,
    string? NonIncomeReason,
    string? ClientName,
    Guid? ClientId,
    Guid? InvoiceId,
    string? InvoiceNumber,
    string? Description,
    bool BeforeRegistration,
    RefundedReceipt? RefundsReceipt,
    TransactionSource? Source,
    ReviewStatus ReviewStatus,
    SetAsideResponse? SetAside);

/// <summary>
/// What to set aside from a receipt for tax (Rule 13): its hryvnia amount times its year's rates, zero
/// for a non-income kind and negative for a refund. Null when the operation is left out of income
/// (Rule 8) or no rate is configured for its year.
/// </summary>
internal sealed record SetAsideResponse(long SingleTaxKop, long MilitaryLevyKop);

internal sealed record ConfirmRequest(TransactionKind Kind);

// Where an imported row came from; null on a row the owner typed.
internal sealed record TransactionSource(Bank Bank, string AccountCurrency);

internal sealed record RefundedReceipt(Guid Id, DateOnly ValueDate, long AmountMinor, Currency Currency);

internal sealed record ReceiptOption(
    Guid Id,
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    string? ClientName,
    Guid? ClientId);

internal sealed record TransactionListResponse(
    int Year,
    DateOnly? FopRegistrationDate,
    long TotalIncomeKop,
    TransactionResponse[] Items);
