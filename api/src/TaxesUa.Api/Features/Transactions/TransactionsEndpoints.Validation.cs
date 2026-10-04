using System.Text.Json;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Transactions;

public static partial class TransactionsEndpoints
{
    internal static NormalizedText Normalize(TransactionRequest request) => new(
        Trim(request.NonIncomeReason),
        Trim(request.ClientName),
        Trim(request.InvoiceNumber),
        Trim(request.Description));

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static FieldErrors? Validate(
        TransactionRequest request, NormalizedText normalized, DateOnly today)
    {
        var errors = new FieldErrors();

        if (request.AmountMinor <= 0)
        {
            errors.Set(Field(nameof(request.AmountMinor)), ProblemCodes.NotPositive, "amountMinor must be positive.");
        }
        else if (request.AmountMinor > MaxAmountMinor)
        {
            errors.Set(
                Field(nameof(request.AmountMinor)),
                ProblemCodes.AmountTooLarge,
                $"amountMinor must not exceed {MaxAmountMinor}.");
        }

        if (request.ManualRateE4 is { } manualRateE4)
        {
            if (request.Currency == Currency.UAH)
            {
                errors.Set(
                    Field(nameof(request.ManualRateE4)),
                    ProblemCodes.ManualRateNotAllowed,
                    "manualRateE4 must be empty for UAH.");
            }
            else if (manualRateE4 < 1 || manualRateE4 > MaxRateE4)
            {
                errors.Set(
                    Field(nameof(request.ManualRateE4)),
                    ProblemCodes.RateOutOfRange,
                    $"manualRateE4 must be between 1 and {MaxRateE4}.");
            }
        }

        if (request.ValueDate.Year < Limits.MinYear || request.ValueDate.Year > Limits.MaxYear)
        {
            errors.Set(
                Field(nameof(request.ValueDate)),
                ProblemCodes.YearOutOfRange,
                $"valueDate year must be between {Limits.MinYear} and {Limits.MaxYear}.");
        }
        else if (request.ValueDate > today)
        {
            // Income arises on the credit date (Rule 2), so a real operation cannot be dated after
            // today in Kyiv.
            errors.Set(
                Field(nameof(request.ValueDate)),
                ProblemCodes.DateInFuture,
                "valueDate must not be after today.");
        }

        var isIncomeKind = request.Kind is TransactionKind.Income or TransactionKind.RefundToClient;
        if (!isIncomeKind && normalized.NonIncomeReason is null)
        {
            errors.Set(
                Field(nameof(request.NonIncomeReason)),
                ProblemCodes.Required,
                "nonIncomeReason is required for a non-income kind.");
        }
        else if (isIncomeKind && normalized.NonIncomeReason is not null)
        {
            errors.Set(
                Field(nameof(request.NonIncomeReason)),
                ProblemCodes.NotAllowed,
                "nonIncomeReason must be empty for an income kind.");
        }
        else if (normalized.NonIncomeReason is { Length: > MaxReasonLength })
        {
            errors.Set(
                Field(nameof(request.NonIncomeReason)),
                ProblemCodes.TooLong,
                $"nonIncomeReason must not exceed {MaxReasonLength} characters.");
        }
        else if (normalized.NonIncomeReason is { } reason && TextRules.HasDisallowedControlChar(reason))
        {
            errors.Set(
                Field(nameof(request.NonIncomeReason)),
                ProblemCodes.ControlCharacter,
                ControlCharMessage("nonIncomeReason"));
        }

        if (request.RefundsTransactionId is not null && request.Kind != TransactionKind.RefundToClient)
        {
            errors.Set(
                Field(nameof(request.RefundsTransactionId)),
                ProblemCodes.RefundLinkNotAllowed,
                "refundsTransactionId is allowed only on a refund to a client.");
        }

        if (normalized.ClientName is { Length: > Limits.MaxClientNameLength })
        {
            errors.Set(
                Field(nameof(request.ClientName)),
                ProblemCodes.TooLong,
                $"clientName must not exceed {Limits.MaxClientNameLength} characters.");
        }
        else if (normalized.ClientName is { } clientName && TextRules.HasDisallowedControlChar(clientName))
        {
            errors.Set(
                Field(nameof(request.ClientName)),
                ProblemCodes.ControlCharacter,
                ControlCharMessage("clientName"));
        }

        if (normalized.InvoiceNumber is { Length: > MaxInvoiceNumberLength })
        {
            errors.Set(
                Field(nameof(request.InvoiceNumber)),
                ProblemCodes.TooLong,
                $"invoiceNumber must not exceed {MaxInvoiceNumberLength} characters.");
        }
        else if (normalized.InvoiceNumber is { } invoiceNumber && TextRules.HasDisallowedControlChar(invoiceNumber))
        {
            errors.Set(
                Field(nameof(request.InvoiceNumber)),
                ProblemCodes.ControlCharacter,
                ControlCharMessage("invoiceNumber"));
        }

        if (normalized.Description is { Length: > MaxDescriptionLength })
        {
            errors.Set(
                Field(nameof(request.Description)),
                ProblemCodes.TooLong,
                $"description must not exceed {MaxDescriptionLength} characters.");
        }
        else if (normalized.Description is { } description && TextRules.HasDisallowedControlChar(description))
        {
            errors.Set(
                Field(nameof(request.Description)),
                ProblemCodes.ControlCharacter,
                ControlCharMessage("description"));
        }

        return errors.OrNull();
    }

    // Derived rather than spelled a second time, so the key the web reads an error under cannot drift
    // from the member it is about. CamelCase is the policy JsonSerializerDefaults.Web applies to the
    // same member.
    private static string Field(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string ControlCharMessage(string field) =>
        $"{field} must not contain a NUL or other control character (tab, line feed and carriage return are allowed).";

    internal readonly record struct NormalizedText(
        string? NonIncomeReason,
        string? ClientName,
        string? InvoiceNumber,
        string? Description);
}
