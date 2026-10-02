namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// A bank account exposed by a connected bank token, per knowledge/domain-model.md. One row per
/// (<see cref="UserId"/>, <see cref="Bank"/>, <see cref="ExternalId"/>). The encrypted token belongs
/// to <see cref="MonobankConnection"/>, one per owner, never to the account, per ADR-011.
/// </summary>
internal sealed class BankAccount
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Bank Bank { get; set; }

    // The bank's own account id (monobank: Accounts[].id), unique together with UserId and Bank.
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    // ISO 4217 numeric currency code as the bank reports it (monobank: Accounts[].currencyCode).
    // Kept as the raw numeric code rather than the shared Currency enum, because an account can carry
    // a currency the app has no engine support for; MonobankEndpoints maps the three the engine
    // understands to a display code and leaves the rest as the bare number.
    public int CurrencyCode { get; set; }

    public string Iban { get; set; } = string.Empty;

    // The account type as monobank reports it (monobank: Accounts[].type), kept as the raw open
    // string so settings can name an unsupported type (e.g. "diia") without another migration.
    public string AccountType { get; set; } = string.Empty;

    // Set from AccountType == "fop" at the moment the account is fetched. Only a FOP account can ever
    // be followed; every other type, known or not, is listed as not supported.
    public bool IsFop { get; set; }

    // Whether the owner chose to follow (sync) this account. Always false for a non-FOP account.
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // The end of the last statement window whose rows are committed, written in the same database
    // transaction as those rows. Null until the first window lands; the backfill starts from the
    // registration date then (Rule 12).
    public DateTimeOffset? SyncedThrough { get; set; }

    // When a window reaching the present was first committed. From then on the history counts as
    // imported, however old the cursor grows between syncs; a restore clears it with the cursor.
    public DateTimeOffset? HistoryImportedAt { get; set; }

    // When the account last began its backfill: added, followed again, or reset by a restore. Sync health
    // judges a backfill from this or its latest import batch, whichever is later, so a restart has its
    // own grace period. Not part of the backup format; a restore stamps it with the restore time.
    public DateTimeOffset BackfillStartedAt { get; set; }

    // The last sync that failed for a reason other than a rejected token, kept until a window of this
    // account imports again. Both set or both null.
    public DateTimeOffset? LastFailedAt { get; set; }

    public SyncFailure? LastFailure { get; set; }
}

internal enum SyncFailure
{
    BankUnreachable,
    BankTimeout,
    BankError,
    UnreadableAnswer,
    RateLimited,
    TokenUnreadable,
    TooManyInOneSecond,
    Unexpected,
}
