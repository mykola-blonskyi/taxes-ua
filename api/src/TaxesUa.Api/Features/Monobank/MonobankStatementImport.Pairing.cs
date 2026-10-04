using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Monobank;
internal sealed partial class MonobankStatementImport
{
    // The hryvnia legs and foreign debits around a window that its pairing has to see. A leg pairs with a
    // debit up to a tolerance away and competes for it with every other leg within the tolerance of that
    // debit, so the range grows from the window by a tolerance on each side until the earliest and the
    // latest item loaded are a tolerance clear of its ends. Then nothing outside the range can change
    // who pairs with what inside it, whichever window reads a leg first. Nothing is loaded without a
    // debit, since nothing pairs without one.
    private async Task<(List<ForeignDebit> Debits, List<Transaction> Legs)> LoadAroundAsync(
        string ownerId, Statement statement, bool track, CancellationToken cancellationToken)
    {
        var tolerance = ReceiptClassifier.SaleTolerance;
        var low = statement.From - tolerance;
        var high = statement.To + tolerance;
        while (true)
        {
            var debitRows = database.ForeignDebits
                .Where(row => row.UserId == ownerId && row.BankTime >= low && row.BankTime <= high);
            var debits = await (track ? debitRows : debitRows.AsNoTracking()).ToListAsync(cancellationToken);
            if (debits.Count == 0)
            {
                return ([], []);
            }

            var legRows = database.Transactions
                .IgnoreQueryFilters()
                .Where(row => row.UserId == ownerId
                    && row.ExternalId != null
                    && row.Currency == Currency.UAH
                    && row.BankTime >= low
                    && row.BankTime <= high);
            var legs = await (track ? legRows : legRows.AsNoTracking()).ToListAsync(cancellationToken);

            var times = debits.Select(row => row.BankTime).Concat(legs.Select(row => row.BankTime!.Value)).ToList();
            var widenedLow = times.Min() - tolerance;
            var widenedHigh = times.Max() + tolerance;
            if (widenedLow >= low && widenedHigh <= high)
            {
                return (debits, legs);
            }

            low = Min(low, widenedLow);
            high = widenedHigh > high ? widenedHigh : high;
        }
    }

    // The NBU rate of every day a debit around this window falls on, read without the owner's lock. The
    // rows are only peeked at here (nothing tracked) and read again under the lock, where a debit that
    // appeared in between falls back to a lookup of its own.
    private async Task<Dictionary<(Currency, DateOnly), int?>> LookUpSaleRatesAsync(
        string ownerId, BankAccount account, Statement statement, CancellationToken cancellationToken)
    {
        var (stored, _) = await LoadAroundAsync(ownerId, statement, track: false, cancellationToken);
        var days = stored.Select(debit => (debit.Currency, Date: debit.BankTime.KyivDate())).ToHashSet();
        if (IsoCurrency.FromNumeric(account.CurrencyCode) is { } currency && currency != Currency.UAH)
        {
            days.UnionWith(SettledDebits(statement).Select(item => (currency, Date: item.Time.KyivDate())));
        }

        var rateOn = new Dictionary<(Currency, DateOnly), int?>();
        foreach (var day in days)
        {
            rateOn[day] = await RateAsync(day.Currency, day.Date, cancellationToken);
        }

        return rateOn;
    }

    private async Task<int?> RateAsync(Currency currency, DateOnly date, CancellationToken cancellationToken) =>
        await rates.GetAsync(currency, date, cancellationToken) is NbuLookup.Found found ? found.RateE4 : null;

    // One classification over the window's fresh credits, the hryvnia legs already stored around it and
    // every stored foreign debit around it, so a sale pairs whichever account was read first. It runs
    // before anything else is changed in this window, since FxRates saves its cache rows as it goes.
    private async Task<Suggestions> SuggestAsync(
        string ownerId,
        BankAccount account,
        Statement statement,
        IReadOnlyList<MonobankStatementItem> fresh,
        Dictionary<(Currency, DateOnly), int?> rateOn,
        CancellationToken cancellationToken)
    {
        var (debits, legs) = await LoadAroundAsync(ownerId, statement, track: true, cancellationToken);
        var ownIbans = await database.BankAccounts
            .Where(row => row.UserId == ownerId && row.Iban != "")
            .Select(row => row.Iban)
            .ToListAsync(cancellationToken);

        foreach (var key in debits.Select(debit => (debit.Currency, Date: debit.BankTime.KyivDate())).Distinct())
        {
            if (!rateOn.ContainsKey(key))
            {
                rateOn[key] = await RateAsync(key.Currency, key.Date, cancellationToken);
            }
        }

        IncomingCredit[] credits = IsoCurrency.FromNumeric(account.CurrencyCode) is { } currency
            ? [.. fresh.Select(item => new IncomingCredit(Fresh(item), currency, item.Time, item.Amount, item.CounterIban))]
            : [];
        var kinds = ReceiptClassifier.Classify(
            [.. credits, .. legs.Select(row => new IncomingCredit(Stored(row), Currency.UAH, row.BankTime!.Value, row.AmountMinor, null))],
            [.. debits.Select(debit => new OutgoingDebit(
                debit.Id.ToString(),
                debit.Currency,
                debit.BankTime,
                debit.AmountMinor,
                rateOn[(debit.Currency, debit.BankTime.KyivDate())]))],
            ownIbans.ToHashSet(StringComparer.OrdinalIgnoreCase));

        return new Suggestions(kinds, legs);
    }

    // Moves the suggestion of every stored leg the pairing now reads differently: to FxSale once its
    // foreign leg is read, and back to Income when a guess no longer pairs (the real leg settled later
    // and closer, or took the debit this one shared). Only unreviewed rows move, since any edit or
    // confirmation sets Confirmed under the owner's lock this runs under. Legs outside the window move
    // too: the range loaded around it holds everything that can compete for their debits.
    //
    // A leg that goes back goes to Income, never to OwnTransfer, though its counterparty may be one of
    // the owner's own accounts: Transaction does not keep the counterparty's IBAN, and keeping it would
    // mean a column and a backup schema change for a case that needs a sale guess displaced by a closer
    // leg. The row stays unreviewed and counts as income until the owner reviews it, which Rule 12 says.
    private async Task MoveStoredSuggestionsAsync(Suggestions suggestions, CancellationToken cancellationToken)
    {
        var saleReason = ReceiptClassifier.ReasonFor(TransactionKind.FxSale);
        var unreviewed = suggestions.Legs
            .Where(row => row.ReviewStatus == ReviewStatus.NeedsReview)
            .ToList();
        var sold = unreviewed
            .Where(row => row.Kind != TransactionKind.FxSale && suggestions.Kinds[Stored(row)] == TransactionKind.FxSale)
            .ToList();
        var unsold = unreviewed
            .Where(row => row.Kind == TransactionKind.FxSale
                && row.NonIncomeReason == saleReason
                && suggestions.Kinds[Stored(row)] != TransactionKind.FxSale)
            .ToList();
        var soldIds = sold.Select(row => row.Id).ToList();
        // A receipt with linked refunds must stay Income, as the transaction edit enforces.
        var linked = await database.Transactions
            .Where(row => row.RefundsTransactionId != null && soldIds.Contains(row.RefundsTransactionId.Value))
            .Select(row => row.RefundsTransactionId!.Value)
            .ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var row in sold.Where(row => !linked.Contains(row.Id)))
        {
            row.Kind = TransactionKind.FxSale;
            row.NonIncomeReason = saleReason;
            row.UpdatedAt = now;
        }

        foreach (var row in unsold)
        {
            row.Kind = TransactionKind.Income;
            row.NonIncomeReason = null;
            row.UpdatedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    // The classifier's ids share one namespace; a bank operation id and a row id never collide this way.
    private static string Fresh(MonobankStatementItem item) => $"op:{item.Id}";

    private static string Stored(Transaction row) => $"row:{row.Id}";

    private sealed record Suggestions(IReadOnlyDictionary<string, TransactionKind> Kinds, IReadOnlyList<Transaction> Legs);
}
