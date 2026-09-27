using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Periods;

/// <summary>
/// One owner's year run through <see cref="Accruals"/>: the stored rows, compiled to engine input and
/// computed once. Every caller that shows a year's tax figures loads them here, so no two screens can
/// show different numbers for the same year.
/// </summary>
internal sealed record YearAccruals(TaxYearConfig Config, SettingsEntity Settings, YearAccrual Accrual)
{
    /// <summary>
    /// Every configured year from the registration year onward, oldest first, so that Rule 7 can settle
    /// a kind's oldest debt with any later payment and carry balances across years. Years after
    /// <paramref name="year"/> are included because a payment named for them still settles an older
    /// debt, and every year's screen must agree on which quarters are paid. Years before registration
    /// accrue nothing and are left out unless <paramref name="year"/> is one of them; a year with no
    /// <see cref="TaxYearConfig"/> row cannot be computed. Empty when <paramref name="year"/> itself has
    /// no row.
    /// </summary>
    public static async Task<IReadOnlyList<YearAccruals>> LoadLedgerAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var settings = await database.Settings.FindAsync([userId], cancellationToken);
        var fromYear = Math.Min(settings?.FopRegistrationDate?.Year ?? year, year);
        var configured = await database.TaxYearConfigs
            .Where(config => config.Year >= fromYear)
            .OrderBy(config => config.Year)
            .Select(config => config.Year)
            .ToListAsync(cancellationToken);
        if (!configured.Contains(year))
        {
            return [];
        }

        var years = new List<YearAccruals>(configured.Count);
        foreach (var configuredYear in configured)
        {
            years.Add((await LoadAsync(database, userId, configuredYear, cancellationToken))!);
        }

        return years;
    }

    /// <summary>Null when the year has no <see cref="TaxYearConfig"/> row.</summary>
    public static async Task<YearAccruals?> LoadAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var config = await database.TaxYearConfigs.FindAsync([year], cancellationToken);
        if (config is null)
        {
            return null;
        }

        // The absent row answers with the defaults, as GET /api/settings does.
        var settings = await database.Settings.FindAsync([userId], cancellationToken)
            ?? new SettingsEntity { UserId = userId };

        var transactions = await database.Transactions
            .Include(row => row.RefundsTransaction)
            .Where(row => row.UserId == userId
                && row.ValueDate >= new DateOnly(year, 1, 1)
                && row.ValueDate < new DateOnly(year + 1, 1, 1))
            .ToListAsync(cancellationToken);

        var accrual = Accruals.ForYear(
            year,
            transactions.Select(row => row.ToEngineInput()).ToList(),
            config.ToEngineInput(),
            settings.ToEngineInput());

        return new YearAccruals(config, settings, accrual);
    }
}
