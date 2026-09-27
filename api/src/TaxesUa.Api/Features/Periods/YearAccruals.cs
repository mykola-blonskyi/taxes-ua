using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;
using PaymentMode = TaxesUa.Api.Features.Settings.PaymentMode;
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
    /// The viewed year and the years Rule 7 allocates payments over. Null when the viewed year has no
    /// <see cref="TaxYearConfig"/> row.
    /// </summary>
    public static async Task<LoadedYears?> LoadAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        // The absent row answers with the defaults, as GET /api/settings does.
        var settings = await database.Settings.FindAsync([userId], cancellationToken)
            ?? new SettingsEntity { UserId = userId };
        var registeredYear = settings.FopRegistrationDate?.Year;
        var fromYear = Math.Min(registeredYear ?? year, year);

        var configs = await database.TaxYearConfigs
            .Where(config => config.Year >= fromYear)
            .OrderBy(config => config.Year)
            .ToListAsync(cancellationToken);
        var viewedConfig = configs.SingleOrDefault(config => config.Year == year);
        if (viewedConfig is null)
        {
            return null;
        }

        var ledgerConfigs = registeredYear is { } start ? Contiguous(configs, start) : [];
        var computed = ledgerConfigs.Append(viewedConfig).DistinctBy(config => config.Year).ToArray();
        var firstDay = new DateOnly(computed.Min(config => config.Year), 1, 1);
        var endDay = new DateOnly(computed.Max(config => config.Year) + 1, 1, 1);

        var transactions = await database.Transactions
            .Include(row => row.RefundsTransaction)
            .Where(row => row.UserId == userId && row.ValueDate >= firstDay && row.ValueDate < endDay)
            .ToListAsync(cancellationToken);
        var byYear = transactions.ToLookup(row => row.ValueDate.Year, row => row.ToEngineInput());

        var settingsInput = settings.ToEngineInput();
        var accruals = computed.ToDictionary(
            config => config.Year,
            config => new YearAccruals(
                config,
                settings,
                Accruals.ForYear(config.Year, [.. byYear[config.Year]], config.ToEngineInput(), settingsInput)));

        int? missingTaxYear = registeredYear is { } registered && year >= registered + ledgerConfigs.Count
            ? registered + ledgerConfigs.Count
            : null;
        return new LoadedYears(
            accruals[year],
            [.. ledgerConfigs.Select(config => accruals[config.Year])],
            missingTaxYear);
    }

    /// <summary>
    /// The configured years from <paramref name="start"/> up to the first one missing. A year without
    /// a row cannot be computed, and skipping it would turn its payments into credit against later
    /// years and hide its debt, so the ledger stops there instead.
    /// </summary>
    private static List<TaxYearConfig> Contiguous(IReadOnlyList<TaxYearConfig> configs, int start)
    {
        var run = new List<TaxYearConfig>();
        foreach (var config in configs.Where(config => config.Year >= start))
        {
            if (config.Year != start + run.Count)
            {
                break;
            }

            run.Add(config);
        }

        return run;
    }
}

/// <summary>
/// <c>Ledger</c> runs from the registration year through every consecutive configured year, oldest
/// first, whatever year is viewed, so every view allocates the same payments to the same quarters.
/// It is empty without a registration date. <c>Viewed</c> is one of its entries when the viewed year
/// falls inside it; a year before registration or past a missing year is computed on its own and has
/// no obligations. <c>MissingTaxYear</c> names the year the ledger stopped at when that is why the
/// viewed year is outside it.
/// </summary>
internal sealed record LoadedYears(YearAccruals Viewed, IReadOnlyList<YearAccruals> Ledger, int? MissingTaxYear)
{
    public bool ViewedIsInLedger => Ledger.Contains(Viewed);

    /// <summary>
    /// Rule 6's advances over every ledger year, or null in <c>Quarterly</c> mode. They are read off
    /// <paramref name="ledger"/>, never fed back into it: the mode changes recommendations only.
    /// </summary>
    public IReadOnlyList<MonthlyAdvance>? AdvancesOf(PaymentLedger ledger) =>
        Viewed.Settings.PaymentMode == PaymentMode.MonthlyAdvance
            ? [.. Ledger.SelectMany(year => MonthlyAdvances.ForYear(year.Accrual, ledger, year.Config.AdvanceRecommendedDay))]
            : null;
}
