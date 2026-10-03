using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.TaxYears;

/// <summary>What is wrong with next year's parameters: no row at all, or a row nobody has confirmed.</summary>
internal enum NewTaxYearState
{
    Missing,
    Unconfirmed,
}

internal sealed record NewTaxYearStatus(int Year, NewTaxYearState State);

/// <summary>
/// The December prompt to prepare the coming tax year (Rule 9). It is derived from the tax years and the
/// Kyiv date, never stored, so the dashboard card and the alert read one answer and it clears itself the
/// moment the year is verified. Outside December nothing is open: the rest of the year the missing-year
/// warnings of Rule 9 already speak.
/// </summary>
internal static class NewTaxYearCheck
{
    public static async Task<NewTaxYearStatus?> LoadAsync(
        AppDbContext database, DateOnly today, CancellationToken cancellationToken)
    {
        if (today.Month != 12)
        {
            return null;
        }

        var year = today.Year + 1;
        var next = await database.TaxYearConfigs.AsNoTracking()
            .Where(config => config.Year == year)
            .Select(config => new { config.VerifiedAt })
            .SingleOrDefaultAsync(cancellationToken);

        return next switch
        {
            null => new NewTaxYearStatus(year, NewTaxYearState.Missing),
            { VerifiedAt: null } => new NewTaxYearStatus(year, NewTaxYearState.Unconfirmed),
            _ => null,
        };
    }
}

/// <summary>The current Kyiv year has no parameters, so the ledger stops there and the home screen has no balance (Rule 7).</summary>
internal static class MissingTaxYearCheck
{
    public static async Task<int?> LoadAsync(AppDbContext database, DateOnly today, CancellationToken cancellationToken) =>
        await database.TaxYearConfigs.AsNoTracking().AnyAsync(config => config.Year == today.Year, cancellationToken)
            ? null
            : today.Year;
}

/// <summary>
/// Turns <see cref="NewTaxYearCheck"/> and <see cref="MissingTaxYearCheck"/> into alerts. The key is the year, so the owner hears of each
/// new year once per channel however many days of December it stays open. Tax years are not per owner,
/// so every owner is told. A current year with no row is a second incident, keyed by that year, which
/// takes over on 1 January when December's closes. The message goes out from 09:00 Kyiv, the hour of the deadline reminders,
/// since nothing here is urgent enough to wake anyone.
/// </summary>
internal sealed class NewTaxYearIncidentSource(TimeProvider time) : IIncidentSource
{
    public async Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        if (TimeOnly.FromDateTime(time.NowInKyiv()) < ReminderPlan.SendAt)
        {
            return [];
        }

        var today = time.TodayInKyiv();
        var open = new List<Incident>();
        if (await MissingTaxYearCheck.LoadAsync(database, today, cancellationToken) is { } missing)
        {
            open.Add(new Incident($"{IncidentKind.MissingTaxYear}:{missing}", IncidentKind.MissingTaxYear, null, missing));
        }

        if (await NewTaxYearCheck.LoadAsync(database, today, cancellationToken) is { } status)
        {
            open.Add(new Incident($"{IncidentKind.NewTaxYear}:{status.Year}", IncidentKind.NewTaxYear, null, status.Year));
        }

        return open;
    }
}
