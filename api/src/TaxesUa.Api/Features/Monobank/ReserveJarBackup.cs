using System.Text.Json.Serialization;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// The reserve jar as the backup file carries it: the choice and the last balance with the time it was
/// true, so a restored card says how old its figure is until the next sync refreshes it. The monobank
/// token is never part of a backup (ADR-011).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ReserveJarBackup(string JarId, string Title, long BalanceKop, DateTimeOffset FetchedAt)
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static ReserveJarBackup From(ReserveJar row) => new(row.JarId, row.Title, row.BalanceKop, row.FetchedAt);

    // The column limits of ReserveJarConfiguration, and the only balance a UAH jar can hold.
    public (string Key, Issue Issue)? Error(DateTimeOffset now) => this switch
    {
        { JarId: null } or { Title: null } => ("jarId", new Issue(ProblemCodes.Required, "jarId and title are required.")),
        { JarId.Length: 0 or > ReserveJarConfiguration.MaxJarIdLength } =>
            ("jarId", new Issue(ProblemCodes.TooLong, $"jarId must be 1 to {ReserveJarConfiguration.MaxJarIdLength} characters.")),
        _ when TextRules.HasDisallowedControlChar(JarId) => ("jarId", new Issue(ProblemCodes.ControlCharacter, "jarId must not contain a control character.")),
        { Title.Length: > ReserveJarConfiguration.MaxTitleLength } =>
            ("title", new Issue(ProblemCodes.TooLong, $"title must not exceed {ReserveJarConfiguration.MaxTitleLength} characters.")),
        _ when TextRules.HasDisallowedControlChar(Title) => ("title", new Issue(ProblemCodes.ControlCharacter, "title must not contain a control character.")),
        { BalanceKop: < 0 } => ("balanceKop", new Issue(ProblemCodes.InvalidValue, "balanceKop must not be negative.")),
        { FetchedAt.Year: < 2000 } => ("fetchedAt", new Issue(ProblemCodes.InvalidValue, "fetchedAt must be a real time.")),
        _ when FetchedAt > now + FutureTolerance => ("fetchedAt", new Issue(ProblemCodes.InvalidValue, "fetchedAt must not be in the future.")),
        _ => null,
    };

    public ReserveJar ToEntity(string userId) => new()
    {
        UserId = userId,
        JarId = JarId,
        Title = Title,
        BalanceKop = BalanceKop,
        FetchedAt = FetchedAt.ToUniversalTime(),
    };
}
