using System.Diagnostics;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Transactions;

/// <summary>
/// Records a transaction: normalization, validation, the receipt link (Rule 8's transitive
/// exclusion needs it), the NBU rate (Rule 2) and client resolution, all in one save. This is the
/// operation behind <c>POST /transactions</c>, and the one a later background worker (the monobank
/// import) calls directly with an owner id and a request-shaped input, without an HttpContext.
/// </summary>
internal static class TransactionRecorder
{
    internal static async Task<RecordTransactionResult> RecordAsync(
        AppDbContext database,
        string userId,
        TransactionRequest request,
        FxRates rates,
        DateOnly today,
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
        if (await TransactionsEndpoints.ApplyAmountAsync(row, request, rates, cancellationToken) is { } problem)
        {
            return problem switch
            {
                AmountProblem.Invalid invalid => new RecordTransactionResult.Invalid(invalid.Errors),
                AmountProblem.RateUnavailable unavailable => new RecordTransactionResult.RateUnavailable(
                    unavailable.Currency, unavailable.Date, unavailable.Lookup),
                _ => throw new UnreachableException(),
            };
        }

        var now = DateTimeOffset.UtcNow;
        row.ClientId = await TransactionsEndpoints.ResolveClientAsync(
            database, userId, normalized.ClientName, cancellationToken);
        row.Kind = request.Kind;
        row.NonIncomeReason = normalized.NonIncomeReason;
        row.RefundsTransaction = await TransactionsEndpoints.FindReceiptAsync(database, request, cancellationToken);
        row.InvoiceNumber = normalized.InvoiceNumber;
        row.Description = normalized.Description;
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

    internal sealed record Invalid(Dictionary<string, string[]> Errors) : RecordTransactionResult;

    internal sealed record RateUnavailable(Currency Currency, DateOnly Date, NbuLookup Lookup)
        : RecordTransactionResult;
}

/// <summary>What <see cref="TransactionsEndpoints.ApplyAmountAsync"/> hands back when it cannot fix the rate.</summary>
internal abstract record AmountProblem
{
    private AmountProblem()
    {
    }

    internal sealed record Invalid(Dictionary<string, string[]> Errors) : AmountProblem;

    internal sealed record RateUnavailable(Currency Currency, DateOnly Date, NbuLookup Lookup) : AmountProblem;
}
