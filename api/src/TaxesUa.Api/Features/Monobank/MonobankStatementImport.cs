using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Walks one followed FOP account's statement forward in windows from its cursor (or, before the first
/// window lands, from the backfill start) to now, and records each settled credit, with the kind
/// <see cref="ReceiptClassifier"/> suggests, through <see cref="TransactionRecorder"/>, the same
/// operation a manual entry takes. An operation whose id the account already holds is never recorded
/// again. A settled debit to the Treasury becomes a <see cref="BudgetPaymentCandidate"/> instead.
/// </summary>
internal sealed partial class MonobankStatementImport(
    AppDbContext database,
    FxRates rates,
    MonobankClient client,
    MonobankRateGate gate,
    MonobankSyncQueue queue,
    TokenEncryptor encryptor,
    TimeProvider time,
    ILogger<MonobankStatementImport> logger)
{
    // monobank accepts a window of at most 31 days and 1 hour.
    public static readonly TimeSpan Window = TimeSpan.FromDays(31);

    private const string StatementMethod = "statement";

    private const int MaxRateLimitedAttempts = 5;

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

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
            token = encryptor.Decrypt(connection.EncryptedToken, connection.UserId);
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
        var now = time.GetUtcNow();
        var from = Min(start, now - Window);
        var cursor = account.SyncedThrough;
        while (await FetchAsync(work, connection, token, account, from, now, cancellationToken) is { } statement)
        {
            if (!await ImportAsync(connection, account, cursor, statement, cancellationToken) || statement.ReachesNow)
            {
                return;
            }

            from = statement.To;
            cursor = statement.To;
            await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
            now = time.GetUtcNow();
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
        SyncWork work,
        MonobankConnection connection,
        string token,
        BankAccount account,
        DateTimeOffset from,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
                        // One worker serves every owner, so the bank cannot park it for longer than this.
                        await Task.Delay(retryAfter < MaxRetryAfter ? retryAfter : MaxRetryAfter, time, cancellationToken);
                    }

                    await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
                    continue;

                case StatementResult.RateLimited:
                    logger.LogWarning("monobank kept rate-limiting the statement of account {AccountId}.", account.ExternalId);
                    await RecordFailureAsync(account.Id, SyncFailure.RateLimited, cancellationToken);
                    return null;

                case StatementResult.InvalidToken:
                    logger.LogWarning("monobank rejected the token of owner {OwnerId}.", connection.UserId);
                    if (!await RejectAsync(connection, cancellationToken))
                    {
                        // The token was replaced while this call was in flight; walk on with the new one.
                        queue.Enqueue(work);
                    }

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
                    "monobank statement for account {AccountId} has a full page within one second, so its window is not committed.",
                    account.ExternalId);
                await RecordFailureAsync(account.Id, SyncFailure.TooManyInOneSecond, cancellationToken);
                return null;
            }

            pageTo = oldest;
            await gate.WaitTurnAsync(connection.UserId, StatementMethod, cancellationToken);
        }
    }

    // Only the token that was rejected: one saved while this call was in flight stays usable. The first
    // rejection stays, since RejectedAt keys the incident alert; a token already rejected counts as done.
    internal async Task<bool> RejectAsync(MonobankConnection connection, CancellationToken cancellationToken)
    {
        var sameToken = database.MonobankConnections
            .Where(row => row.UserId == connection.UserId && row.EncryptedToken == connection.EncryptedToken);

        return await sameToken.Where(row => row.RejectedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RejectedAt, time.GetUtcNow()), cancellationToken) > 0
            || await sameToken.AnyAsync(row => row.RejectedAt != null, cancellationToken);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;

    // One database transaction per window under the owner's advisory lock, the lock restore and the
    // prototype import take, so a sync never interleaves with a restore's delete and insert. False, with
    // nothing written, when the walk no longer holds: a restore moved the cursor, the owner unfollowed
    // the account, or the token was replaced, disconnected or rejected since the walk began. Also false,
    // with only the failure written, when a credit's NBU rate is unavailable: the cursor stays before the
    // window so the next run reads it again, and a backfill that writes no batch is judged stale (Rule 17).
    private async Task<bool> ImportAsync(
        MonobankConnection connection,
        BankAccount account,
        DateTimeOffset? cursor,
        Statement statement,
        CancellationToken cancellationToken)
    {
        var ownerId = connection.UserId;
        var bankAccountId = account.Id;
        var today = time.TodayInKyiv();
        // NBU can take seconds to answer and a restore waits on the lock, so the rates the pairing needs
        // are fetched before it is taken, as the transaction edit and POST do. FxRates saves its cache
        // rows as it goes, so this also has to run before anything is changed in the context.
        var saleRates = await LookUpSaleRatesAsync(ownerId, account, statement, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OwnerLock.AcquireAsync(database, ownerId, cancellationToken);
        // Rows an earlier window of this walk left tracked would otherwise stand in for what the owner
        // has since confirmed or edited; nothing is pending here, so dropping them loses nothing.
        database.ChangeTracker.Clear();

        var holds = await database.BankAccounts.AnyAsync(
                row => row.Id == bankAccountId && row.IsActive && row.SyncedThrough == cursor,
                cancellationToken)
            && await database.MonobankConnections.AnyAsync(
                row => row.UserId == ownerId
                    && row.EncryptedToken == connection.EncryptedToken
                    && row.RejectedAt == null,
                cancellationToken);
        if (!holds)
        {
            return false;
        }

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
        // A dismissed row is a tombstone: it still holds its operation id, so the bank cannot bring it back.
        var present = await database.Transactions
            .IgnoreQueryFilters()
            .Where(row => row.BankAccountId == bankAccountId && ids.Contains(row.ExternalId!))
            .Select(row => row.ExternalId!)
            .ToListAsync(cancellationToken);
        var fresh = credits.ExceptBy(present, item => item.Id).OrderBy(item => item.Time).ToList();
        await StoreForeignDebitsAsync(ownerId, account, statement, cancellationToken);
        var suggestions = await SuggestAsync(ownerId, account, statement, fresh, saleRates, cancellationToken);

        var imported = 0;
        var skipped = 0;
        foreach (var item in fresh)
        {
            var kind = suggestions.Kinds.GetValueOrDefault(Fresh(item), TransactionKind.Income);
            switch (await RecordAsync(ownerId, account, batch.Id, item, kind, today, cancellationToken))
            {
                case Outcome.Imported:
                    imported++;
                    break;
                case Outcome.Skipped:
                    skipped++;
                    break;
                case Outcome.RateUnavailable:
                    await transaction.RollbackAsync(cancellationToken);
                    await RecordFailureAsync(bankAccountId, SyncFailure.NbuRateUnavailable, cancellationToken);
                    return false;
            }
        }

        await MoveStoredSuggestionsAsync(suggestions, cancellationToken);
        imported += await StoreCandidatesAsync(ownerId, account, statement, cancellationToken);

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
                    .SetProperty(row => row.HistoryImportedAt, row => row.HistoryImportedAt ?? (statement.ReachesNow ? statement.To : null))
                    .SetProperty(row => row.LastFailedAt, (DateTimeOffset?)null)
                    .SetProperty(row => row.LastFailure, (SyncFailure?)null),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Outcome> RecordAsync(
        string ownerId,
        BankAccount account,
        Guid batchId,
        MonobankStatementItem item,
        TransactionKind kind,
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

        var counterparty = Fit(item.CounterName?.Trim(), Limits.MaxClientNameLength);
        var request = new TransactionRequest(
            item.Time.KyivDate(),
            item.Amount,
            currency,
            ManualRateE4: null,
            kind,
            ReceiptClassifier.ReasonFor(kind),
            ClientName: counterparty,
            InvoiceNumber: null,
            Description: Fit(Describe(item), TransactionsEndpoints.MaxDescriptionLength),
            RefundsTransactionId: null);
        var provenance = new ImportProvenance(account.Id, item.Id, item.Time, counterparty, batchId);

        var result = await TransactionRecorder.RecordAsync(
            database, ownerId, request, provenance, rates, today, time, cancellationToken);

        switch (result)
        {
            case RecordTransactionResult.Success:
                return Outcome.Imported;
            case RecordTransactionResult.Invalid invalid:
                logger.LogWarning(
                    "monobank operation {OperationId} was not recorded, it breaks {Fields}.",
                    item.Id, string.Join(", ", invalid.Errors.Fields));
                return Outcome.Skipped;
            case RecordTransactionResult.RateUnavailable:
                logger.LogWarning("monobank operation {OperationId} waits for its NBU rate.", item.Id);
                return Outcome.RateUnavailable;
            default:
                throw new UnreachableException();
        }
    }

    // A statement holds one account, so the two legs of a sale arrive in different walks, in either
    // order and possibly months apart. The foreign leg is kept here so whichever leg is read second
    // still finds the other one stored.
    private async Task StoreForeignDebitsAsync(
        string ownerId, BankAccount account, Statement statement, CancellationToken cancellationToken)
    {
        if (IsoCurrency.FromNumeric(account.CurrencyCode) is not { } currency || currency == Currency.UAH)
        {
            return;
        }

        var debits = SettledDebits(statement);
        var ids = debits.Select(item => item.Id).ToList();
        var stored = await database.ForeignDebits
            .Where(row => row.BankAccountId == account.Id && ids.Contains(row.ExternalId))
            .Select(row => row.ExternalId)
            .ToListAsync(cancellationToken);
        database.ForeignDebits.AddRange(debits.ExceptBy(stored, item => item.Id).Select(item => new ForeignDebit
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            BankAccountId = account.Id,
            ExternalId = item.Id,
            BankTime = item.Time,
            AmountMinor = -item.Amount,
            Currency = currency,
        }));
        await database.SaveChangesAsync(cancellationToken);
    }

    // A payment into the budget is always in hryvnia, so only a UAH account's debits are read. A candidate
    // of any status still holds its operation id, so a confirmed or dismissed one never comes back; one
    // stored without the counterparty's code gains it when the bank sends it.
    private async Task<int> StoreCandidatesAsync(
        string ownerId, BankAccount account, Statement statement, CancellationToken cancellationToken)
    {
        if (IsoCurrency.FromNumeric(account.CurrencyCode) is not Currency.UAH)
        {
            return 0;
        }

        var payments = statement.Items
            .Where(item => item.Amount < 0 && !item.Hold && TreasuryPayment.IsTreasury(item.CounterIban))
            .DistinctBy(item => item.Id)
            .ToList();
        var ids = payments.Select(item => item.Id).ToList();
        var stored = await database.BudgetPaymentCandidates
            .Where(row => row.BankAccountId == account.Id && ids.Contains(row.ExternalId))
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var candidate in stored.Where(row => row.CounterEdrpou is null))
        {
            var item = payments.First(payment => payment.Id == candidate.ExternalId);
            candidate.CounterEdrpou = Fit(item.CounterEdrpou?.Trim(), TreasuryAccountsEndpoints.MaxEdrpouLength);
            if (candidate is { CounterEdrpou: not null, ConfirmedKind: { } kind })
            {
                await TreasuryAccountsEndpoints.RelearnAsync(database, candidate, kind, now, cancellationToken);
            }
        }

        var fresh = payments.ExceptBy(stored.Select(row => row.ExternalId), item => item.Id).ToList();
        database.BudgetPaymentCandidates.AddRange(fresh.Select(item => new BudgetPaymentCandidate
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            BankAccountId = account.Id,
            ExternalId = item.Id,
            BankTime = item.Time,
            AmountKop = -item.Amount,
            CounterIban = TreasuryPayment.Normalize(item.CounterIban)!,
            CounterName = Fit(item.CounterName?.Trim(), Limits.MaxClientNameLength),
            CounterEdrpou = Fit(item.CounterEdrpou?.Trim(), TreasuryAccountsEndpoints.MaxEdrpouLength),
            Purpose = Fit(Describe(item), TransactionsEndpoints.MaxDescriptionLength),
            Status = CandidateStatus.Pending,
            CreatedAt = now,
        }));
        await database.SaveChangesAsync(cancellationToken);
        return fresh.Count;
    }

    private static List<MonobankStatementItem> SettledDebits(Statement statement) => statement.Items
        .Where(item => item.Amount < 0 && !item.Hold)
        .DistinctBy(item => item.Id)
        .ToList();


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
        RateUnavailable,
    }
}
