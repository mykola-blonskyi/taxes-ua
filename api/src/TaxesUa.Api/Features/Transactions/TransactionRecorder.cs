using System.Diagnostics;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Transactions;

/// <summary>
/// Records a transaction: normalization, validation, the receipt link (Rule 8's transitive
/// exclusion needs it), the NBU rate (Rule 2) and client resolution, all in one save. This is the
/// operation behind <c>POST /transactions</c>, and the one the monobank sync calls directly with an
/// owner id, a request-shaped input and the row's <see cref="ImportProvenance"/>, without an HttpContext.
/// </summary>
internal static class TransactionRecorder
{
    internal static async Task<RecordTransactionResult> RecordAsync(
        AppDbContext database,
        string userId,
        TransactionRequest request,
        ImportProvenance? provenance,
        FxRates rates,
        DateOnly today,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var normalized = TransactionsEndpoints.Normalize(request);
        if (TransactionsEndpoints.Validate(request, normalized, today) is { } errors)
        {
            return new RecordTransactionResult.Invalid(errors);
        }

        if (await TransactionsEndpoints.ValidateLinksAsync(database, userId, null, request, cancellationToken)
            is { } linkErrors)
        {
            return new RecordTransactionResult.Invalid(linkErrors);
        }

        var row = new Transaction { Id = Guid.NewGuid(), UserId = userId };
        var lookup = await TransactionsEndpoints.LookUpRateAsync(request, rates, cancellationToken);
        if (TransactionsEndpoints.ApplyAmount(row, request, lookup) is { } problem)
        {
            return problem switch
            {
                AmountProblem.Invalid invalid => new RecordTransactionResult.Invalid(invalid.Errors),
                AmountProblem.RateUnavailable unavailable => new RecordTransactionResult.RateUnavailable(
                    unavailable.Currency, unavailable.Date, unavailable.Lookup),
                _ => throw new UnreachableException(),
            };
        }

        var now = time.GetUtcNow();
        row.ClientId = await TransactionsEndpoints.ResolveClientAsync(
            database, userId, normalized.ClientName, cancellationToken);
        row.Kind = request.Kind;
        row.NonIncomeReason = normalized.NonIncomeReason;
        row.RefundsTransaction = await TransactionsEndpoints.FindReceiptAsync(database, request, cancellationToken);
        row.InvoiceNumber = normalized.InvoiceNumber;
        row.Description = normalized.Description;
        if (provenance is not null)
        {
            row.BankAccountId = provenance.BankAccountId;
            row.ExternalId = provenance.ExternalId;
            row.BankTime = provenance.BankTime;
            row.Counterparty = provenance.Counterparty;
            row.ImportBatchId = provenance.ImportBatchId;
            row.ReviewStatus = ReviewStatus.NeedsReview;
        }

        row.CreatedAt = now;
        row.UpdatedAt = now;
        database.Transactions.Add(row);
        await database.SaveChangesAsync(cancellationToken);

        var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken);
        var beforeRegistration = TransactionsEndpoints.IsBeforeRegistration(row, settings);

        return new RecordTransactionResult.Success(row, normalized.ClientName, beforeRegistration);
    }
}

/// <summary>
/// Where an imported row came from. A row recorded with one starts as <see cref="ReviewStatus.NeedsReview"/>,
/// since nothing is classified for the owner without their confirmation.
/// </summary>
internal sealed record ImportProvenance(
    Guid BankAccountId,
    string ExternalId,
    DateTimeOffset BankTime,
    string? Counterparty,
    Guid ImportBatchId);

/// <summary>
/// What <see cref="TransactionRecorder.RecordAsync"/> hands back: the saved row, a validation
/// failure, or an NBU rate that could not be looked up.
/// </summary>
internal abstract record RecordTransactionResult
{
    private RecordTransactionResult()
    {
    }

    internal sealed record Success(Transaction Row, string? ClientName, bool BeforeRegistration)
        : RecordTransactionResult;

    internal sealed record Invalid(FieldErrors Errors) : RecordTransactionResult;

    internal sealed record RateUnavailable(Currency Currency, DateOnly Date, NbuLookup Lookup)
        : RecordTransactionResult;
}

/// <summary>What <see cref="TransactionsEndpoints.ApplyAmount"/> hands back when it cannot fix the rate.</summary>
internal abstract record AmountProblem
{
    private AmountProblem()
    {
    }

    internal sealed record Invalid(FieldErrors Errors) : AmountProblem;

    internal sealed record RateUnavailable(Currency Currency, DateOnly Date, NbuLookup Lookup) : AmountProblem;
}
