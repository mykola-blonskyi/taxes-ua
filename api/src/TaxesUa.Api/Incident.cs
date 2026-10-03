using TaxesUa.Api.Data;

namespace TaxesUa.Api;

/// <summary>
/// What an alert is about. A new kind (a failed backup, an expired Treasury account) is a member here,
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
}

/// <summary>
/// Something wrong that stays wrong until fixed. <c>Key</c> names this occurrence: it is claimed per
/// channel in <see cref="SentReminder"/> so it is alerted once, and a later occurrence of the same kind
/// has another key. <c>Since</c> is the instant the text mentions, when the source has one; <c>Year</c>
/// is the tax year it is about, when it is about one.
/// </summary>
internal sealed record Incident(string Key, IncidentKind Kind, DateTimeOffset? Since, int? Year = null);

/// <summary>
/// A place incidents come from. The sender asks each source for what is open for an owner right now; a
/// source that finds nothing open reports nothing, which is also how an incident clears.
/// </summary>
internal interface IIncidentSource
{
    Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken);
}
