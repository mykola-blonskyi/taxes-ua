using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>An incoming operation to classify. <c>Id</c> only has to be unique within one call.</summary>
internal sealed record IncomingCredit(
    string Id, Currency AccountCurrency, DateTimeOffset Time, long AmountMinor, string? CounterIban);

/// <summary>A settled outgoing operation on one of the owner's FOP accounts. It is never recorded.</summary>
internal sealed record OutgoingDebit(
    string Id, Currency AccountCurrency, DateTimeOffset Time, long AmountMinor, int? NbuRateE4);

/// <summary>
/// Suggests a kind for each imported credit (Rule 12). A UAH credit paired with a foreign-currency debit
/// on the owner's own FOP account is a currency sale; a credit from one of the owner's own IBANs is an
/// own transfer; everything else is income. The owner confirms or changes every suggestion.
/// </summary>
internal static class ReceiptClassifier
{
    // Both legs of a sale are booked by one in-bank conversion, so they land within seconds of each
    // other; a minute absorbs processing lag without pairing unrelated operations.
    public static readonly TimeSpan SaleTolerance = TimeSpan.FromSeconds(60);

    // The bank converts at its own buying rate, which sits under the NBU rate by its spread: usually a
    // percent or two, several in stressed markets. 5 percent covers that, while an unrelated receipt
    // would have to land within a minute of the debit and match its worth to 5 percent to pass as a sale.
    public const int SpreadPercent = 5;

    public static IReadOnlyDictionary<string, TransactionKind> Classify(
        IReadOnlyCollection<IncomingCredit> credits,
        IReadOnlyCollection<OutgoingDebit> debits,
        IReadOnlySet<string> ownIbans)
    {
        var sold = CurrencySales(credits, debits);

        return credits.ToDictionary(
            credit => credit.Id,
            credit => sold.Contains(credit.Id) ? TransactionKind.FxSale
                : credit.CounterIban is { } iban && ownIbans.Contains(iban) ? TransactionKind.OwnTransfer
                : TransactionKind.Income);
    }

    // Rule 1 wants a reason on every non-income row, so a suggestion carries one the owner can overwrite.
    public static string? ReasonFor(TransactionKind kind) => kind switch
    {
        TransactionKind.OwnTransfer => "monobank: own transfer",
        TransactionKind.FxSale => "monobank: currency sale",
        _ => null,
    };

    private static bool Pairs(IncomingCredit credit, OutgoingDebit debit) =>
        credit.AccountCurrency == Currency.UAH
        && debit.AccountCurrency != Currency.UAH
        && debit.NbuRateE4 is { } rate
        && (credit.Time - debit.Time).Duration() <= SaleTolerance
        && WithinSpread(credit.AmountMinor, Money.ToUahKop(debit.AmountMinor, rate));

    private static bool WithinSpread(long creditKop, long worthKop) =>
        (Int128)Math.Abs(creditKop - worthKop) * 100 <= (Int128)worthKop * SpreadPercent;

    // A maximum matching, each leg used once, so one debit never turns two receipts into sales and
    // overlapping candidates still yield every sale they can (Kuhn's augmenting paths). The credit with
    // the closest debit claims first and each tries its closest debits first, which favours closer legs
    // and makes the same inputs give the same pairs whatever order they arrive in. It is a heuristic
    // for the choice among equally large matchings, not a guarantee of the smallest total gap: an
    // exact choice would be a minimum-cost matching, more than the few overlapping legs a minute can
    // hold are worth.
    private static HashSet<string> CurrencySales(
        IReadOnlyCollection<IncomingCredit> credits, IReadOnlyCollection<OutgoingDebit> debits)
    {
        var candidates = credits
            .Select(credit => (credit.Id, credit.Time, Debits: debits
                .Where(debit => Pairs(credit, debit))
                .Select(debit => (debit.Id, Gap: (credit.Time - debit.Time).Duration()))
                .OrderBy(debit => debit.Gap)
                .ThenBy(debit => debit.Id, StringComparer.Ordinal)
                .ToArray()))
            .Where(credit => credit.Debits.Length > 0)
            .OrderBy(credit => credit.Debits[0].Gap)
            .ThenBy(credit => credit.Time)
            .ThenBy(credit => credit.Id, StringComparer.Ordinal)
            .Select(credit => (credit.Id, Debits: credit.Debits.Select(debit => debit.Id).ToArray()))
            .ToArray();

        var creditOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var credit = 0; credit < candidates.Length; credit++)
        {
            TryPair(credit, []);
        }

        return creditOf.Values.Select(credit => candidates[credit].Id).ToHashSet(StringComparer.Ordinal);

        bool TryPair(int credit, HashSet<string> visited)
        {
            foreach (var debit in candidates[credit].Debits)
            {
                if (visited.Add(debit)
                    && (!creditOf.TryGetValue(debit, out var holder) || TryPair(holder, visited)))
                {
                    creditOf[debit] = credit;
                    return true;
                }
            }

            return false;
        }
    }
}
