using TaxesUa.Api.Data;
using TaxesUa.Engine;

namespace TaxesUa.Api;

/// <summary>
/// What an alert is about. A new kind is a member here,
/// an arm in <see cref="IncidentTexts"/> and an <see cref="IIncidentSource"/> that reports it.
/// </summary>
internal enum IncidentKind
{
    SyncStale,
    TokenRejected,
    TokenUnreadable,
    NewTaxYear,
    MissingTaxYear,
    RestoreCheckFailed,
    TreasuryAccountExpired,
}

/// <summary>
/// Something wrong that stays wrong until fixed. <c>Key</c> names this occurrence: it is claimed per
/// channel in <see cref="SentReminder"/> so it is alerted once, and a later occurrence of the same kind
/// has another key. <c>Since</c> is the instant the text mentions, when the source has one; <c>Year</c>
/// is the tax year it is about, when it is about one; <c>Account</c> the Treasury account that ended.
/// </summary>
/// <summary>A Treasury account in use whose last day has passed (Rule 16).</summary>
internal sealed record ExpiredTreasuryAccount(PaymentKind Kind, DateOnly ValidUntil);

internal sealed record Incident(
    string Key, IncidentKind Kind, DateTimeOffset? Since, int? Year = null, ExpiredTreasuryAccount? Account = null);

/// <summary>
/// A place incidents come from. The sender asks each source for what is open for an owner right now; a
/// source that finds nothing open reports nothing, which is also how an incident clears.
/// </summary>
internal interface IIncidentSource
{
    Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken);
}
