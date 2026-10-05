using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

/// <summary>
/// The owner's word on when a stored Treasury account ends (Rule 16). <see cref="Unsaid"/>: the owner said
/// nothing, so the account ends on its year's default, if there is one. <see cref="On"/>: the owner's own last
/// day. <see cref="Removed"/>: the owner said the account does not end, which also sets the default aside.
/// </summary>
internal abstract record AccountEnd
{
    public static readonly AccountEnd Unsaid = new UnsaidEnd();

    public static readonly AccountEnd Removed = new RemovedEnd();

    private AccountEnd()
    {
    }

    public sealed record On(DateOnly Date) : AccountEnd;

    private sealed record UnsaidEnd : AccountEnd;

    private sealed record RemovedEnd : AccountEnd;

    /// <summary>The two columns an end is stored in; the check constraint forbids a date that was removed.</summary>
    public static AccountEnd Of(DateOnly? validUntil, bool removed) =>
        (validUntil, removed) switch
        {
            ({ } date, false) => new On(date),
            (null, true) => Removed,
            (null, false) => Unsaid,
            _ => throw new InvalidOperationException("An end cannot be both a date and removed."),
        };

    public (DateOnly? ValidUntil, bool Removed) Columns() => this switch
    {
        On on => (on.Date, false),
        RemovedEnd => (null, true),
        _ => (null, false),
    };
}

internal enum TreasuryEndSource
{
    Owner,
    Default,
}

/// <summary>The last day the account in use can receive a payment, and whose word that is.</summary>
internal sealed record AccountValidity(DateOnly ValidUntil, TreasuryEndSource Source);

/// <summary>
/// The default end of a military-levy account: the <c>MilitaryLevyAccountEnd</c> of the tax year the account
/// arrived in, for an account that arrived on or before it. A Learned account arrives on the day of the payment
/// it was learned from, so a December payment imported in January is still the year's temporary account; a
/// Manual account arrives when the owner last entered it, so entering the same IBAN after the end, as the
/// account of the new year, says it goes on. The year the account is used in would not do: the next year
/// carries no end of its own, so the old account would stop ending the day it closes.
/// </summary>
internal sealed class LevyAccountEnds(IReadOnlyDictionary<int, DateOnly> byYear)
{
    public static async Task<LevyAccountEnds> LoadAsync(AppDbContext database, CancellationToken cancellationToken) =>
        new((await database.TaxYearConfigs.AsNoTracking()
                .Where(config => config.MilitaryLevyAccountEnd != null)
                .Select(config => new { config.Year, End = config.MilitaryLevyAccountEnd!.Value })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Year, row => row.End));

    public AccountValidity? ValidityOf(TreasuryAccount row)
    {
        var (end, arrived) = row.IsManual ? (row.ManualEnd, row.ManualUpdatedAt!.Value.KyivDate())
            : row.LearnedIban is not null ? (row.LearnedEnd, row.LearnedPaidOn!.Value)
            : (null, default);

        return end switch
        {
            AccountEnd.On on => new AccountValidity(on.Date, TreasuryEndSource.Owner),
            _ when end == AccountEnd.Unsaid
                && row.Kind == PaymentKind.MilitaryLevy
                && byYear.TryGetValue(arrived.Year, out var yearEnd)
                && arrived <= yearEnd => new AccountValidity(yearEnd, TreasuryEndSource.Default),
            _ => null,
        };
    }
}

/// <summary>
/// The accounts in use whose end has passed, so the owner has to enter the new one (Rule 16). Derived, never
/// stored: entering a new account, or removing the end, clears it.
/// </summary>
internal static class ExpiredTreasuryAccounts
{
    public static async Task<IReadOnlyList<ExpiredTreasuryAccount>> LoadAsync(
        AppDbContext database, string userId, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await database.TreasuryAccounts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ends = await LevyAccountEnds.LoadAsync(database, cancellationToken);

        return
        [
            .. rows
                .Select(row => (row.Kind, Validity: ends.ValidityOf(row)))
                .Where(each => each.Validity is { } validity && today > validity.ValidUntil)
                .OrderBy(each => each.Kind)
                .Select(each => new ExpiredTreasuryAccount(each.Kind, each.Validity!.ValidUntil)),
        ];
    }
}

/// <summary>
/// Alerts each expired account once per channel, keyed by its kind and end, from 09:00 Kyiv on the first
/// working day after the end (Rule 17's hour; the owner's weekend days and the holidays of that day's tax year,
/// none when the year has no parameters yet). A new account with a later end that expires in turn is another
/// key, so it alerts again.
/// </summary>
internal sealed class ExpiredTreasuryAccountIncidentSource(TimeProvider time) : IIncidentSource
{
    public async Task<IReadOnlyList<Incident>> OpenAsync(AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var today = time.TodayInKyiv();
        if (TimeOnly.FromDateTime(time.NowInKyiv()) < ReminderPlan.SendAt)
        {
            return [];
        }

        var expired = await ExpiredTreasuryAccounts.LoadAsync(database, userId, today, cancellationToken);
        if (expired.Count == 0)
        {
            return [];
        }

        var weekendDays = (await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken)).WeekendDays;
        var open = new List<Incident>();
        foreach (var account in expired)
        {
            var dayAfter = account.ValidUntil.AddDays(1);
            var holidays = await database.TaxYearConfigs.AsNoTracking()
                .Where(config => config.Year == dayAfter.Year)
                .Select(config => config.Holidays)
                .SingleOrDefaultAsync(cancellationToken) ?? [];
            if (today < DeadlineCalendar.NextBusinessDay(dayAfter, holidays, weekendDays))
            {
                continue;
            }

            open.Add(new Incident(
                $"{IncidentKind.TreasuryAccountExpired}:{account.Kind}:{account.ValidUntil:yyyy-MM-dd}",
                IncidentKind.TreasuryAccountExpired,
                null,
                Account: account));
        }

        return open;
    }
}
