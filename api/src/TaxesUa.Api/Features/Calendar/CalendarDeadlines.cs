using System.Security.Cryptography;
using System.Text;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Calendar;

internal enum CalendarDeadlineKind
{
    Esv,
    TaxPayment,
    Declaration,
    Advance,
}

/// <summary>
/// One date in the owner's calendar. <c>Quarter</c> is set for the three quarterly kinds and
/// <c>Month</c> for an advance. The single tax and the military levy share <c>TaxPayment</c>, as in
/// <see cref="QuarterDeadlines"/>.
/// </summary>
internal sealed record CalendarDeadline(string Owner, CalendarDeadlineKind Kind, int Year, int? Quarter, int? Month, DateOnly Date)
{
    // Stable across calls and rotations, so a client that already holds the event updates it. The owner
    // part keeps two owners' events, or a feed and an import in one account, from sharing a UID.
    public string Uid => Kind switch
    {
        CalendarDeadlineKind.Advance => $"advance-{Year}-m{Month:00}-{Owner}@taxes-ua",
        _ => $"{Kind.ToString().ToLowerInvariant()}-{Year}-q{Quarter}-{Owner}@taxes-ua",
    };

    /// <summary>The first 8 hex characters of SHA-256 of the user id: not secret, only distinct.</summary>
    public static string OwnerKey(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..8];

    /// <summary>
    /// Every deadline dated in the current or the next calendar year that the periods screen shows: the
    /// engine's shifted dates, so last year's Q4 and December advance stay until they fall behind, the quarters still in group 3 (Rule 4), and in <c>MonthlyAdvance</c> mode each
    /// month's recommended date (Rule 6). A year without a tax year configuration has no dates.
    /// </summary>
    public static async Task<IReadOnlyList<CalendarDeadline>> LoadAsync(
        AppDbContext database, string userId, DateOnly today, CancellationToken cancellationToken)
    {
        var deadlines = new List<CalendarDeadline>();
        var owner = OwnerKey(userId);
        foreach (var year in new[] { today.Year - 1, today.Year, today.Year + 1 })
        {
            if (await YearAccruals.LoadAsync(database, userId, year, cancellationToken) is not { } loaded)
            {
                continue;
            }

            var viewed = loaded.Viewed;
            var registered = viewed.Settings.FopRegistrationDate;
            var config = viewed.Config.ToEngineInput();
            var settings = viewed.Settings.ToEngineInput();

            foreach (var accrual in viewed.Accrual.Quarters)
            {
                var quarter = accrual.Income.Quarter;
                if (registered is { } start && MonthEnd(year, 3 * quarter) < start)
                {
                    continue;
                }

                var due = DeadlineCalendar.ForQuarter(year, quarter, config, settings);
                if (!viewed.Settings.EsvExempt)
                {
                    deadlines.Add(new(owner, CalendarDeadlineKind.Esv, year, quarter, null, due.Esv.Due));
                }

                // Before group 3 starts only ESV is owed (Tax Code 298.1.4).
                if (!accrual.Group3)
                {
                    continue;
                }

                deadlines.Add(new(owner, CalendarDeadlineKind.TaxPayment, year, quarter, null, due.TaxPayment.Due));
                deadlines.Add(new(owner, CalendarDeadlineKind.Declaration, year, quarter, null, due.Declaration.Due));
            }

            if (viewed.Settings.PaymentMode != PaymentMode.MonthlyAdvance || !loaded.ViewedIsInLedger || registered is not { } registeredOn)
            {
                continue;
            }

            foreach (var month in viewed.Accrual.Months.Where(month => MonthEnd(year, month.Month) >= registeredOn))
            {
                var date = new DateOnly(year, month.Month, 1).AddMonths(1).AddDays(viewed.Config.AdvanceRecommendedDay - 1);
                deadlines.Add(new(owner, CalendarDeadlineKind.Advance, year, null, month.Month, date));
            }
        }

        return [.. deadlines.Where(deadline => deadline.Date.Year >= today.Year).OrderBy(deadline => deadline.Date).ThenBy(deadline => deadline.Uid, StringComparer.Ordinal)];
    }

    private static DateOnly MonthEnd(int year, int month) => new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
}
