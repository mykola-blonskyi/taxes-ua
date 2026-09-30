using System.Globalization;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Invoices;

internal sealed record InvoiceLineRequest(
    string DescriptionEn,
    string DescriptionUk,
    InvoiceUnit Unit,
    long QuantityThousandths,
    long RateMinor);

internal sealed record InvoiceRequest(
    Guid ClientId,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLineRequest[] Lines)
{
    public InvoiceRequest Normalized() => this with
    {
        Lines = [.. (Lines ?? []).Select(line => line with
        {
            DescriptionEn = line.DescriptionEn?.Trim() ?? string.Empty,
            DescriptionUk = line.DescriptionUk?.Trim() ?? string.Empty,
        })],
    };

    public InvoiceLine[] ToLines() =>
        [.. Lines.Select(line => new InvoiceLine(
            line.DescriptionEn, line.DescriptionUk, line.Unit, line.QuantityThousandths, line.RateMinor))];
}

internal sealed record CancelInvoiceRequest(string Reason);

internal static class InvoiceNumbers
{
    public static string Format(int year, int sequence) =>
        $"{year.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("000", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// What a draft must satisfy to be saved, and what it and the owner's details must satisfy to be
/// issued. The endpoints and a restored backup run the same checks.
/// </summary>
internal static class InvoiceRules
{
    public const int MaxLines = 50;

    public const int MaxDescriptionLength = 500;

    public const int MaxCancelReasonLength = 500;

    /// <summary>A quantity of at most one million, with up to three decimals, held as whole thousandths.</summary>
    public const long MaxQuantityThousandths = 1_000_000_000;

    // 10 billion in the currency, far above any invoice a FOP writes, and far below where a long
    // could overflow in a total of fifty lines.
    public const long MaxAmountMinor = 1_000_000_000_000;

    /// <summary>
    /// A line's amount in minor units: quantity times rate, rounded once to a whole minor unit, half
    /// away from zero (Rule 10). The total is the sum of the rounded lines, so the lines printed on the
    /// invoice always add up to its total. Both factors are never negative, so adding half and
    /// truncating rounds half away from zero.
    /// </summary>
    public static long LineAmountMinor(long quantityThousandths, long rateMinor) =>
        (long)(((Int128)quantityThousandths * rateMinor + 500) / 1000);

    public static long TotalMinor(IEnumerable<InvoiceLine> lines) =>
        lines.Sum(line => LineAmountMinor(line.QuantityThousandths, line.RateMinor));

    /// <summary>The errors of an already <see cref="InvoiceRequest.Normalized"/> draft, keyed by camelCase field.</summary>
    public static Dictionary<string, string[]>? Validate(InvoiceRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ClientId == Guid.Empty)
        {
            errors["clientId"] = ["clientId is required."];
        }

        if (request.IssueDate.Year is < TransactionsEndpoints.MinYear or > TransactionsEndpoints.MaxYear)
        {
            errors["issueDate"] =
                [$"issueDate must be in {TransactionsEndpoints.MinYear} to {TransactionsEndpoints.MaxYear}."];
        }

        if (request.DueDate < request.IssueDate)
        {
            errors["dueDate"] = ["dueDate must not be before issueDate."];
        }

        if (!Enum.IsDefined(request.Currency))
        {
            errors["currency"] = ["currency must be UAH, USD or EUR."];
        }

        if (request.Lines.Length > MaxLines)
        {
            errors["lines"] = [$"An invoice has at most {MaxLines} lines."];
            return errors;
        }

        for (var i = 0; i < request.Lines.Length; i++)
        {
            var line = request.Lines[i];
            var at = $"lines[{i}]";
            if (line is null)
            {
                errors[at] = ["A line must not be null."];
                continue;
            }

            Description(errors, $"{at}.descriptionEn", line.DescriptionEn);
            Description(errors, $"{at}.descriptionUk", line.DescriptionUk);

            if (!Enum.IsDefined(line.Unit))
            {
                errors[$"{at}.unit"] = ["unit must be Service, Hour, Day or Month."];
            }

            if (line.QuantityThousandths is <= 0 or > MaxQuantityThousandths)
            {
                errors[$"{at}.quantityThousandths"] =
                    [$"quantityThousandths must be 1 to {MaxQuantityThousandths}: a quantity above 0 and at most 1 000 000, in thousandths."];
            }
            else if (line.RateMinor < 0 || line.RateMinor > MaxAmountMinor)
            {
                errors[$"{at}.rateMinor"] = [$"rateMinor must be 0 to {MaxAmountMinor}."];
            }
            else if (LineAmountMinor(line.QuantityThousandths, line.RateMinor) > MaxAmountMinor)
            {
                errors[$"{at}.rateMinor"] = [$"A line's amount must not exceed {MaxAmountMinor} minor units."];
            }
        }

        if (errors.Count == 0 && TotalMinor(request.ToLines()) > MaxAmountMinor)
        {
            errors["lines"] = [$"The total must not exceed {MaxAmountMinor} minor units."];
        }

        return errors.Count == 0 ? null : errors;
    }

    public static string? CancelReasonError(string reason) => reason switch
    {
        { Length: 0 } => "reason is required: say why the invoice is cancelled.",
        { Length: > MaxCancelReasonLength } => $"reason must not exceed {MaxCancelReasonLength} characters.",
        _ when TextRules.HasDisallowedControlChar(reason) => "reason must not contain a control character.",
        _ => null,
    };

    /// <summary>
    /// Everything missing for issuing, keyed by where the owner fixes it: <c>invoicing.*</c> in the
    /// invoicing details, <c>client.*</c> on the client, <c>lines*</c> on the invoice. Empty when complete.
    /// </summary>
    public static Dictionary<string, string[]> Completeness(
        Invoice invoice,
        InvoicingDetails details,
        InvoicingPaymentDetails? payment,
        Client client)
    {
        var errors = new Dictionary<string, string[]>();

        void Required(string key, string? value, string what)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[key] = [$"{what} is missing."];
            }
        }

        Required("invoicing.sellerNameUk", details.SellerNameUk, "Your name in Ukrainian");
        Required("invoicing.sellerNameEn", details.SellerNameEn, "Your name in Latin letters");
        Required("invoicing.rnokpp", details.Rnokpp, "Your RNOKPP");
        Required("invoicing.addressUk", details.AddressUk, "Your address in Ukrainian");
        Required("invoicing.addressEn", details.AddressEn, "Your address in English");
        if (payment is null)
        {
            errors[$"invoicing.paymentDetails.{invoice.Currency}"] =
                [$"Payment details for {invoice.Currency} are missing."];
        }

        Required("client.name", client.Name, "The client's legal name");
        Required("client.address", client.Address, "The client's address");
        Required("client.country", client.Country, "The client's country");

        if (invoice.Lines.Length == 0)
        {
            errors["lines"] = ["The invoice has no lines."];
        }

        for (var i = 0; i < invoice.Lines.Length; i++)
        {
            Required($"lines[{i}].descriptionEn", invoice.Lines[i].DescriptionEn, $"Line {i + 1}'s English description");
            Required($"lines[{i}].descriptionUk", invoice.Lines[i].DescriptionUk, $"Line {i + 1}'s Ukrainian description");
        }

        if (invoice.Lines.Length > 0 && invoice.TotalMinor <= 0)
        {
            errors["totalMinor"] = ["The total must be more than zero."];
        }

        return errors;
    }

    /// <summary>
    /// The parties, payment and clauses as they read now. Issuing freezes this; a draft's preview
    /// renders it on the fly, so the preview shows exactly what issuing would keep.
    /// </summary>
    public static InvoiceSnapshot Snapshot(InvoicingDetails details, InvoicingPaymentDetails? payment, Client client) => new(
        new InvoiceSeller(details.SellerNameUk, details.SellerNameEn, details.Rnokpp, details.AddressUk, details.AddressEn),
        new InvoiceBuyer(
            client.Name,
            client.Address ?? string.Empty,
            client.Country ?? string.Empty,
            CountryName(client.Country),
            client.VatId,
            client.Email),
        new InvoicePayment(
            payment?.Iban ?? string.Empty,
            payment?.BeneficiaryBank ?? string.Empty,
            payment?.Swift ?? string.Empty,
            payment?.IntermediaryBank ?? string.Empty,
            payment?.IntermediarySwift ?? string.Empty,
            payment?.IntermediaryAccount ?? string.Empty),
        new InvoiceClauses(
            details.AcceptanceClauseEn,
            details.AcceptanceClauseUk,
            details.FeesClauseEn,
            details.FeesClauseUk,
            details.TaxStatusClauseEn,
            details.TaxStatusClauseUk));

    private static string CountryName(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    private static void Description(Dictionary<string, string[]> errors, string key, string value)
    {
        if (value.Length > MaxDescriptionLength)
        {
            errors[key] = [$"{key} must not exceed {MaxDescriptionLength} characters."];
        }
        else if (TextRules.HasDisallowedControlChar(value))
        {
            errors[key] = [$"{key} must not contain a control character."];
        }
    }
}
