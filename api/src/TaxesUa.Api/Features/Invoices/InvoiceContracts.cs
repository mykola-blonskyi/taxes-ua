using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>
/// <c>Status</c> is the stored lifecycle; <c>Standing</c> is what the owner sees, an issued invoice read
/// as paid or overdue from its linked receipts (Rule 14).
/// </summary>
internal sealed record InvoiceSummary(
    Guid Id,
    InvoiceStatus Status,
    InvoiceStanding Standing,
    string? Number,
    Guid ClientId,
    string ClientName,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    long TotalMinor,
    long PaidMinor,
    long? DueMinor);

internal sealed record InvoiceLineResponse(
    string DescriptionEn,
    string DescriptionUk,
    InvoiceUnit Unit,
    long QuantityThousandths,
    long RateMinor,
    long AmountMinor);

internal sealed record InvoiceResponse(
    Guid Id,
    InvoiceStatus Status,
    InvoiceStanding Standing,
    string? Number,
    Guid ClientId,
    string ClientName,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLineResponse[] Lines,
    long TotalMinor,
    long PaidMinor,
    long? DueMinor,
    LinkedReceipt[] Receipts,
    string? CancelReason,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    string PdfFileName);
