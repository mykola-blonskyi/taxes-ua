using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Reads the last 31 days of one followed FOP account and records each settled credit through
/// <see cref="TransactionRecorder"/>, the same operation a manual entry takes. It only inserts: an
/// operation whose id the account already holds is left as the owner last saved it.
/// </summary>
internal sealed class MonobankStatementImport(
    AppDbContext database,
    FxRates rates,
    MonobankClient client,
    MonobankRateGate gate,
    TokenEncryptor encryptor,
    TimeProvider time,
    ILogger<MonobankStatementImport> logger)
{
    // monobank accepts a window of at most 31 days and 1 hour.
    public static readonly TimeSpan Window = TimeSpan.FromDays(31);

    private const string StatementMethod = "statement";

    private const string ExternalIdIndex = "IX_Transactions_BankAccountId_ExternalId";

    public async Task RunAsync(SyncWork work, CancellationToken cancellationToken)
    {
        var connection = await database.MonobankConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == work.OwnerId, cancellationToken);
        var account = await database.BankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == work.BankAccountId
                    && row.UserId == work.OwnerId
                    && row.Bank == Bank.Monobank
                    && row.IsFop
                    && row.IsActive,
                cancellationToken);
        if (connection is null || account is null || !encryptor.IsConfigured)
        {
            return;
        }

        var token = encryptor.Decrypt(connection.EncryptedToken);
        if (await FetchAsync(token, work.OwnerId, account.ExternalId, cancellationToken) is not { } statement)
        {
            return;
        }

        await ImportAsync(work.OwnerId, account.Id, statement, cancellationToken);
    }

    private async Task<Statement?> FetchAsync(
        string token, string ownerId, string accountId, CancellationToken cancellationToken)
    {
        await gate.WaitTurnAsync(ownerId, StatementMethod, cancellationToken);
        var to = time.GetUtcNow();
        var from = to - Window;

        var items = new List<MonobankStatementItem>();
        var pageTo = to;
        while (true)
        {
            var result = await client.GetStatementAsync(token, accountId, from, pageTo, cancellationToken);
            if (result is not StatementResult.Found found)
            {
                logger.LogWarning(
                    "monobank statement for account {AccountId} was not read: {Outcome}.",
                    accountId,
                    result is StatementResult.Unavailable unavailable ? unavailable.Reason : "the token was rejected");
                return null;
            }

            items.AddRange(found.Items);
            var oldest = found.Items.Count == 0 ? pageTo : found.Items.Min(item => item.Time);
            if (found.Items.Count < MonobankClient.StatementPageSize || oldest >= pageTo)
            {
                return new Statement(from, to, items);
            }

            pageTo = oldest;
            await gate.WaitTurnAsync(ownerId, StatementMethod, cancellationToken);
        }
    }

    // One database transaction per account under the owner's advisory lock, the lock restore and the
    // prototype import take, so a sync never interleaves with a restore's delete and insert.
    private async Task ImportAsync(
        string ownerId, Guid bankAccountId, Statement statement, CancellationToken cancellationToken)
    {
        var today = time.TodayInKyiv();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({ownerId}))", cancellationToken);

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            Source = ImportSource.Monobank,
            BankAccountId = bankAccountId,
            From = statement.From,
            To = statement.To,
            CreatedAt = time.GetUtcNow(),
        };
        database.ImportBatches.Add(batch);
        await database.SaveChangesAsync(cancellationToken);

        var credits = statement.Items
            .Where(item => item.Amount > 0)
            .DistinctBy(item => item.Id)
            .ToList();
        var ids = credits.Select(item => item.Id).ToList();
        var present = await database.Transactions
            .Where(row => row.BankAccountId == bankAccountId && ids.Contains(row.ExternalId!))
            .Select(row => row.ExternalId!)
            .ToListAsync(cancellationToken);

        var imported = 0;
        var skipped = 0;
        foreach (var item in credits.ExceptBy(present, item => item.Id).OrderBy(item => item.Time))
        {
            switch (await RecordAsync(ownerId, bankAccountId, batch.Id, item, today, cancellationToken))
            {
                case Outcome.Imported:
                    imported++;
                    break;
                case Outcome.Skipped:
                    skipped++;
                    break;
            }
        }

        await database.ImportBatches
            .Where(row => row.Id == batch.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.ImportedCount, imported)
                    .SetProperty(row => row.SkippedCount, skipped),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Outcome> RecordAsync(
        string ownerId,
        Guid bankAccountId,
        Guid batchId,
        MonobankStatementItem item,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // A held operation can still be reversed; it is picked up by a later sync once settled.
        if (item.Hold)
        {
            return Outcome.Skipped;
        }

        if (IsoCurrency.FromNumeric(item.CurrencyCode) is not { } currency)
        {
            logger.LogInformation(
                "monobank operation {OperationId} is in currency {CurrencyCode}, which is not recorded.",
                item.Id, item.CurrencyCode);
            return Outcome.Skipped;
        }

        var counterparty = Fit(item.CounterName?.Trim(), TransactionsEndpoints.MaxClientNameLength);
        var request = new TransactionRequest(
            item.Time.KyivDate(),
            item.Amount,
            currency,
            ManualRateE4: null,
            TransactionKind.Income,
            NonIncomeReason: null,
            ClientName: counterparty,
            InvoiceNumber: null,
            Description: Fit(Describe(item), TransactionsEndpoints.MaxDescriptionLength),
            RefundsTransactionId: null);
        var provenance = new ImportProvenance(bankAccountId, item.Id, item.Time, counterparty, batchId);

        RecordTransactionResult result;
        try
        {
            result = await TransactionRecorder.RecordAsync(
                database, ownerId, request, provenance, rates, today, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ExternalIdIndex,
            })
        {
            // EF rolled the failed save back to its savepoint; what it tracked for it must not be saved again.
            database.ChangeTracker.Clear();
            return Outcome.AlreadyPresent;
        }

        switch (result)
        {
            case RecordTransactionResult.Success:
                return Outcome.Imported;
            case RecordTransactionResult.Invalid invalid:
                logger.LogWarning(
                    "monobank operation {OperationId} was not recorded, it breaks {Fields}.",
                    item.Id, string.Join(", ", invalid.Errors.Keys));
                return Outcome.Skipped;
            case RecordTransactionResult.RateUnavailable:
                logger.LogWarning("monobank operation {OperationId} waits for its NBU rate.", item.Id);
                return Outcome.Skipped;
            default:
                throw new UnreachableException();
        }
    }

    private static string? Describe(MonobankStatementItem item)
    {
        var parts = new[] { item.Description?.Trim(), item.Comment?.Trim() }
            .Where(part => !string.IsNullOrEmpty(part))
            .Distinct(StringComparer.Ordinal);
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? null : text;
    }

    // Bank text longer than a column would otherwise fail validation on every sync and keep a real
    // receipt out of income for good.
    private static string? Fit(string? value, int maxLength) => value switch
    {
        null or "" => null,
        { Length: var length } when length > maxLength => value[..maxLength],
        _ => value,
    };

    private sealed record Statement(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<MonobankStatementItem> Items);

    private enum Outcome
    {
        Imported,
        Skipped,
        AlreadyPresent,
    }
}
