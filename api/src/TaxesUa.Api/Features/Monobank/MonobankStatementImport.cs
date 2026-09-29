using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Walks one followed FOP account's statement forward in windows from its cursor (or, before the first
/// window lands, from the backfill start) to now, and records each settled credit through
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

    private const int MaxRateLimitedAttempts = 5;

    public static DateOnly BackfillStart(DateOnly? registeredOn, DateOnly today) =>
        registeredOn ?? new DateOnly(today.Year, 1, 1);

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
        if (connection is null || account is null || connection.RejectedAt is not null)
        {
            return;
        }

        string token;
        try
        {
            token = encryptor.Decrypt(connection.EncryptedToken);
        }
        catch (CryptographicException exception)
        {
            logger.LogError(exception, "The monobank token of owner {OwnerId} could not be decrypted.", work.OwnerId);
            await RecordFailureAsync(account.Id, SyncFailure.TokenUnreadable, cancellationToken);
            return;
        }

        var start = await StartAsync(work.OwnerId, account, cancellationToken);
        // "Now" is read after the turn comes, or a sync that waited behind another account would end its
        // window where the wait began and spend one more call on the minutes since.
        await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
        var from = Min(start, time.GetUtcNow() - Window);
        while (await FetchAsync(connection, token, account, from, cancellationToken) is { } statement)
        {
            await ImportAsync(work.OwnerId, account, statement, cancellationToken);
            if (statement.ReachesNow)
            {
                return;
            }

            from = statement.To;
            await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
        }
    }

    public Task RecordFailureAsync(Guid bankAccountId, SyncFailure failure, CancellationToken cancellationToken) =>
        database.BankAccounts
            .Where(row => row.Id == bankAccountId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.LastFailedAt, time.GetUtcNow())
                    .SetProperty(row => row.LastFailure, failure),
                cancellationToken);

    private async Task<DateTimeOffset> StartAsync(string ownerId, BankAccount account, CancellationToken cancellationToken)
    {
        if (account.SyncedThrough is { } cursor)
        {
            return cursor;
        }

        var registeredOn = await database.Settings
            .Where(row => row.UserId == ownerId)
            .Select(row => row.FopRegistrationDate)
            .FirstOrDefaultAsync(cancellationToken);
        return BackfillStart(registeredOn, time.TodayInKyiv()).KyivMidnight();
    }

    // One window from `from` to at most a window later, paged back from its end; the caller has waited
    // its turn. Null when the bank gave no usable answer, which is recorded for the owner to see.
    private async Task<Statement?> FetchAsync(
        MonobankConnection connection,
        string token,
        BankAccount account,
        DateTimeOffset from,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var to = Min(from + Window, now);

        var items = new List<MonobankStatementItem>();
        var pageTo = to;
        var rateLimited = 0;
        while (true)
        {
            var result = await client.GetStatementAsync(token, account.ExternalId, from, pageTo, cancellationToken);
            switch (result)
            {
                case StatementResult.RateLimited limited when ++rateLimited < MaxRateLimitedAttempts:
                    if (limited.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
                    {
                        await Task.Delay(retryAfter, time, cancellationToken);
                    }

                    await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
                    continue;

                case StatementResult.RateLimited:
                    logger.LogWarning("monobank kept rate-limiting the statement of account {AccountId}.", account.ExternalId);
                    await RecordFailureAsync(account.Id, SyncFailure.RateLimited, cancellationToken);
                    return null;

                case StatementResult.InvalidToken:
                    logger.LogWarning("monobank rejected the token of owner {OwnerId}.", connection.UserId);
                    await RejectAsync(connection, cancellationToken);
                    return null;

                case StatementResult.Unavailable unavailable:
                    logger.LogWarning(
                        "monobank statement for account {AccountId} was not read: {Failure}.",
                        account.ExternalId,
                        unavailable.Failure);
                    await RecordFailureAsync(account.Id, unavailable.Failure, cancellationToken);
                    return null;
            }

            var found = (StatementResult.Found)result;
            rateLimited = 0;
            items.AddRange(found.Items);
            var oldest = found.Items.Count == 0 ? pageTo : found.Items.Min(item => item.Time);
            if (found.Items.Count < MonobankClient.StatementPageSize)
            {
                return new Statement(from, to, to == now, items);
            }

            if (oldest >= pageTo)
            {
                logger.LogWarning(
                    "monobank statement for account {AccountId} has a full page within one second; older operations wait for a later sync.",
                    account.ExternalId);
                return new Statement(from, to, to == now, items);
            }

            pageTo = oldest;
            await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
        }
    }

    // Only the token that was rejected: one saved while this call was in flight stays usable.
    private Task RejectAsync(MonobankConnection connection, CancellationToken cancellationToken) =>
        database.MonobankConnections
            .Where(row => row.UserId == connection.UserId && row.EncryptedToken == connection.EncryptedToken)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RejectedAt, time.GetUtcNow()), cancellationToken);

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;

    // One database transaction per account under the owner's advisory lock, the lock restore and the
    // prototype import take, so a sync never interleaves with a restore's delete and insert.
    private async Task ImportAsync(
        string ownerId, BankAccount account, Statement statement, CancellationToken cancellationToken)
    {
        var bankAccountId = account.Id;
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
            switch (await RecordAsync(ownerId, account, batch.Id, item, today, cancellationToken))
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
        await database.BankAccounts
            .Where(row => row.Id == bankAccountId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.SyncedThrough, statement.To)
                    .SetProperty(row => row.LastFailedAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.LastFailure, (SyncFailure?)null),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Outcome> RecordAsync(
        string ownerId,
        BankAccount account,
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

        // The amount is in the account's currency; the item's own code can name the currency a card
        // payment was made in, which would pair a hryvnia amount with a foreign rate.
        if (IsoCurrency.FromNumeric(account.CurrencyCode) is not { } currency)
        {
            logger.LogInformation(
                "monobank operation {OperationId} is on an account in currency {CurrencyCode}, which is not recorded.",
                item.Id, account.CurrencyCode);
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
        var provenance = new ImportProvenance(account.Id, item.Id, item.Time, counterparty, batchId);

        var result = await TransactionRecorder.RecordAsync(
            database, ownerId, request, provenance, rates, today, cancellationToken);

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
        { Length: var length } when length > maxLength =>
            value[..(char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength)],
        _ => value,
    };

    private sealed record Statement(
        DateTimeOffset From, DateTimeOffset To, bool ReachesNow, IReadOnlyList<MonobankStatementItem> Items);

    private enum Outcome
    {
        Imported,
        Skipped,
    }
}
