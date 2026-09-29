using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// A settled debit on one of the owner's foreign-currency FOP accounts. It is never a transaction: it is
/// kept only so a hryvnia credit read in any window, before or after this account's, can still pair
/// with it as the other leg of a currency sale (Rule 12). One row per (<see cref="BankAccountId"/>,
/// <see cref="ExternalId"/>); a sync only inserts.
/// </summary>
internal sealed class ForeignDebit
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid BankAccountId { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public DateTimeOffset BankTime { get; set; }

    // What left the account, in its currency's minor units, as a positive number.
    public long AmountMinor { get; set; }

    public Currency Currency { get; set; }
}
