using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Engine;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Periods;

/// <summary>
/// One owner's year run through <see cref="Accruals"/>: the stored rows, compiled to engine input and
/// computed once. The periods screen reads it, and the export reads it too, so the two cannot show
/// different numbers for the same year.
/// </summary>
internal sealed record YearAccruals(TaxYearConfig Config, SettingsEntity Settings, YearAccrual Accrual)
{
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
