using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Fx;

internal sealed class FxRates(AppDbContext database, NbuRateClient nbu, TimeProvider time, ILogger<FxRates> logger)
{
    // Saves the cache row with its own SaveChanges, so a caller must call this before it changes
    // anything else in the shared AppDbContext.
    public async Task<NbuLookup> GetAsync(Currency currency, DateOnly date, CancellationToken cancellationToken)
    {
        var cached = await database.FxRates
            .AsNoTracking()
            .FirstOrDefaultAsync(rate => rate.Currency == currency && rate.Date == date, cancellationToken);
        if (cached is not null)
        {
            return new NbuLookup.Found(cached.RateE4, cached.RateDate);
        }

        var lookup = await nbu.GetAsync(currency, date, cancellationToken);
        switch (lookup)
        {
            // NBU answers [] for a date it has not published yet, so a fallback for a future date is
            // provisional and must not be frozen in the cache.
            case NbuLookup.Found found when date <= time.TodayInKyiv():
                await StoreAsync(new FxRate
                {
                    Currency = currency,
                    Date = date,
                    RateE4 = found.RateE4,
                    RateDate = found.RateDate,
                    FetchedAt = time.GetUtcNow(),
                }, cancellationToken);
                break;
            case NbuLookup.NoRate:
                logger.LogWarning(
                    "NBU published no {Currency} rate for the {Days} days up to {Date}.",
                    currency, NbuRateClient.MaxDaysBack, date);
                break;
            case NbuLookup.Unavailable unavailable:
                logger.LogWarning(
                    "NBU {Currency} rate for {Date} is unavailable: {Reason}",
                    currency, date, unavailable.Reason);
                break;
        }

        return lookup;
    }

    private async Task StoreAsync(FxRate row, CancellationToken cancellationToken)
    {
        database.FxRates.Add(row);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request stored the same rate first. Left tracked, the row would be inserted
            // again by the caller's own SaveChanges.
            database.Entry(row).State = EntityState.Detached;
        }
    }
}
