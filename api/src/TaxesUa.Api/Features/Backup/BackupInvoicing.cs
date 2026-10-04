using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Backup;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoicingDetailsBackup(
    string SellerNameUk,
    string SellerNameEn,
    string Rnokpp,
    string AddressUk,
    string AddressEn,
    string AcceptanceClauseEn,
    string AcceptanceClauseUk,
    string FeesClauseEn,
    string FeesClauseUk,
    string TaxStatusClauseEn,
    string TaxStatusClauseUk,
    PaymentDetailsInput[] PaymentDetails,
    // The image as base64, since the file is JSON.
    string? SignatureImage,
    string? SignatureContentType,
    DateTimeOffset? SignatureUpdatedAt)
{
    public static InvoicingDetailsBackup From(InvoicingDetails details, IEnumerable<InvoicingPaymentDetails> payments) => new(
        details.SellerNameUk,
        details.SellerNameEn,
        details.Rnokpp,
        details.AddressUk,
        details.AddressEn,
        details.AcceptanceClauseEn,
        details.AcceptanceClauseUk,
        details.FeesClauseEn,
        details.FeesClauseUk,
        details.TaxStatusClauseEn,
        details.TaxStatusClauseUk,
        [.. payments.OrderBy(payment => payment.Currency).Select(payment => new PaymentDetailsInput(
            payment.Currency,
            payment.Iban,
            payment.BeneficiaryBank,
            payment.Swift,
            payment.IntermediaryBank,
            payment.IntermediarySwift,
            payment.IntermediaryAccount))],
        details.SignatureImage is null ? null : Convert.ToBase64String(details.SignatureImage),
        details.SignatureContentType,
        details.SignatureUpdatedAt);

    public InvoicingDetailsRequest ToRequest() => InvoicingEndpoints.Normalize(new InvoicingDetailsRequest(
        SellerNameUk,
        SellerNameEn,
        Rnokpp,
        AddressUk,
        AddressEn,
        AcceptanceClauseEn,
        AcceptanceClauseUk,
        FeesClauseEn,
        FeesClauseUk,
        TaxStatusClauseEn,
        TaxStatusClauseUk,
        PaymentDetails));

    // The endpoint's own rules, plus the image: it travels as text, so it is decoded and checked again.
    public FieldErrors? Validate()
    {
        if (Array.Exists(PaymentDetails, row => row is null))
        {
            var nulls = new FieldErrors();
            nulls.Set("paymentDetails", ProblemCodes.NullItem, "paymentDetails must not contain null.");

            return nulls;
        }

        var errors = InvoicingEndpoints.Validate(ToRequest()) ?? new FieldErrors();
        if (SignatureError(SignatureImage, SignatureContentType) is { } error)
        {
            errors.Set("signatureImage", error);
        }

        return errors.OrNull();
    }

    public (InvoicingDetails Details, InvoicingPaymentDetails[] Payments) ToEntities(string userId)
    {
        var request = ToRequest();
        var details = new InvoicingDetails { UserId = userId };
        InvoicingEndpoints.Apply(details, request);
        details.SignatureImage = DecodeSignature();
        details.SignatureContentType = details.SignatureImage is null ? null : SignatureContentType;
        details.SignatureUpdatedAt = details.SignatureImage is null ? null : SignatureUpdatedAt?.ToUniversalTime();

        return (
            details,
            [.. request.PaymentDetails.Select(payment => InvoicingEndpoints.ToEntity(userId, Guid.NewGuid(), payment))]);
    }

    private byte[]? DecodeSignature() => DecodeSignature(SignatureImage);

    internal static byte[]? DecodeSignature(string? base64)
    {
        if (base64 is null || base64.Length > InvoicingEndpoints.MaxSignatureBytes * 2)
        {
            return null;
        }

        var buffer = new byte[base64.Length];

        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }

    /// <summary>The image rule the signature upload enforces, for an image that travelled as base64.</summary>
    internal static Issue? SignatureError(string? base64, string? contentType)
    {
        if ((base64 is null) != (contentType is null))
        {
            return new Issue(ProblemCodes.InconsistentFields, "signatureImage and signatureContentType are set together or not at all.");
        }

        if (base64 is null)
        {
            return null;
        }

        return DecodeSignature(base64) is { } image
            ? InvoicingEndpoints.SignatureError(image, contentType!)
            : new Issue(ProblemCodes.SignatureNotAnImage, "signatureImage must be base64.");
    }
}

/// <summary>
/// An invoice with its lines and, once issued, its frozen snapshot and signature. The number is the
/// year and sequence, so a restore keeps every number the owner already sent.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceBackup(
    Guid Id,
    Guid ClientId,
    InvoiceStatus Status,
    int? NumberYear,
    int? NumberSequence,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLine[] Lines,
    InvoiceSnapshot? Snapshot,
    string? SignatureImage,
    string? SignatureContentType,
    string? CancelReason,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static InvoiceBackup From(Invoice invoice) => new(
        invoice.Id,
        invoice.ClientId,
        invoice.Status,
        invoice.NumberYear,
        invoice.NumberSequence,
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Currency,
        invoice.Lines,
        invoice.Snapshot,
        invoice.SignatureImage is null ? null : Convert.ToBase64String(invoice.SignatureImage),
        invoice.SignatureContentType,
        invoice.CancelReason,
        invoice.IssuedAt,
        invoice.CancelledAt,
        invoice.CreatedAt,
        invoice.UpdatedAt);

    /// <summary>The draft rules on the lines and dates, and the rules each status sets for the rest.</summary>
    public FieldErrors? Validate()
    {
        if (Array.Exists(Lines, line => line is null))
        {
            var nulls = new FieldErrors();
            nulls.Set("lines", ProblemCodes.NullItem, "lines must not contain null.");

            return nulls;
        }

        var request = new InvoiceRequest(
            ClientId,
            IssueDate,
            DueDate,
            Currency,
            [.. Lines.Select(line => new InvoiceLineRequest(
                line.DescriptionEn, line.DescriptionUk, line.Unit, line.QuantityThousandths, line.RateMinor))]);
        var errors = InvoiceRules.Validate(request.Normalized()) ?? new FieldErrors();

        var numbered = NumberYear is not null || NumberSequence is not null;
        switch (Status)
        {
            case InvoiceStatus.Draft when numbered || Snapshot is not null || SignatureImage is not null
                || CancelReason is not null || IssuedAt is not null || CancelledAt is not null:
                errors.Set(
                    "status",
                    ProblemCodes.InconsistentFields,
                    "A draft has no number, snapshot, signature, cancel reason or issue and cancel times.");
                break;
            case InvoiceStatus.Issued or InvoiceStatus.Cancelled when NumberYear != IssueDate.Year
                || NumberSequence is not > 0 || Snapshot is null || IssuedAt is null:
                errors.Set(
                    "status",
                    ProblemCodes.InconsistentFields,
                    "An issued invoice has a number of its issue date's year, a snapshot and an issue time.");
                break;
            case InvoiceStatus.Issued when CancelReason is not null || CancelledAt is not null:
                errors.Set(
                    "cancelReason",
                    ProblemCodes.InconsistentFields,
                    "Only a cancelled invoice has a cancel reason.");
                break;
            case InvoiceStatus.Cancelled when CancelledAt is null || InvoiceRules.CancelReasonError(CancelReason?.Trim() ?? string.Empty) is not null:
                errors.Set(
                    "cancelReason",
                    InvoiceRules.CancelReasonError(CancelReason?.Trim() ?? string.Empty)
                    ?? new Issue(ProblemCodes.InconsistentFields, "A cancelled invoice has a cancel time."));
                break;
        }

        if (!Enum.IsDefined(Status))
        {
            errors.Set("status", ProblemCodes.InvalidValue, "status must be Draft, Issued or Cancelled.");
        }

        if (Snapshot is not null && SnapshotTexts(Snapshot).Any(text => text is not null && TextRules.HasDisallowedControlChar(text)))
        {
            errors.Set("snapshot", ProblemCodes.ControlCharacter, "The snapshot must not contain a control character.");
        }

        if (InvoicingDetailsBackup.SignatureError(SignatureImage, SignatureContentType) is { } signatureError)
        {
            errors.Set("signatureImage", signatureError);
        }

        return errors.OrNull();
    }

    public Invoice ToEntity(string userId, Func<Guid, Guid> id)
    {
        var lines = Lines.Select(line => line with
        {
            DescriptionEn = line.DescriptionEn.Trim(),
            DescriptionUk = line.DescriptionUk.Trim(),
        }).ToArray();
        var signature = InvoicingDetailsBackup.DecodeSignature(SignatureImage);

        return new Invoice
        {
            Id = id(Id),
            UserId = userId,
            ClientId = id(ClientId),
            Status = Status,
            NumberYear = NumberYear,
            NumberSequence = NumberSequence,
            IssueDate = IssueDate,
            DueDate = DueDate,
            Currency = Currency,
            Lines = lines,
            TotalMinor = InvoiceRules.TotalMinor(lines),
            Snapshot = Snapshot,
            SignatureImage = signature,
            SignatureContentType = signature is null ? null : SignatureContentType,
            CancelReason = CancelReason?.Trim(),
            IssuedAt = IssuedAt?.ToUniversalTime(),
            CancelledAt = CancelledAt?.ToUniversalTime(),
            CreatedAt = CreatedAt.ToUniversalTime(),
            UpdatedAt = UpdatedAt.ToUniversalTime(),
        };
    }

    private static IEnumerable<string?> SnapshotTexts(InvoiceSnapshot snapshot) =>
    [
        snapshot.Seller.NameUk, snapshot.Seller.NameEn, snapshot.Seller.Rnokpp, snapshot.Seller.AddressUk,
        snapshot.Seller.AddressEn, snapshot.Buyer.Name, snapshot.Buyer.Address, snapshot.Buyer.Country,
        snapshot.Buyer.CountryName, snapshot.Buyer.VatId, snapshot.Buyer.Email, snapshot.Payment.Iban,
        snapshot.Payment.BeneficiaryBank, snapshot.Payment.Swift, snapshot.Payment.IntermediaryBank,
        snapshot.Payment.IntermediarySwift, snapshot.Payment.IntermediaryAccount, snapshot.Clauses.AcceptanceEn,
        snapshot.Clauses.AcceptanceUk, snapshot.Clauses.FeesEn, snapshot.Clauses.FeesUk,
        snapshot.Clauses.TaxStatusEn, snapshot.Clauses.TaxStatusUk,
    ];
}
