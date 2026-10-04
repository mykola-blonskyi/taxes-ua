using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Transactions;

public static partial class TransactionsEndpoints
{
    // The NBU rate a request would be recorded at, or null when it brings its own (a manual rate, or
    // UAH). Callers look it up before they take the owner's lock, since NBU can take seconds to answer
    // and the sync and restore wait on that lock. FxRates saves its cache row with its own SaveChanges,
    // so this also runs before any change to the context.
    internal static async Task<NbuLookup?> LookUpRateAsync(
        TransactionRequest request, FxRates rates, CancellationToken cancellationToken) =>
        request.ManualRateE4 is null && request.Currency != Currency.UAH
            ? await rates.GetAsync(request.Currency, request.ValueDate, cancellationToken)
            : null;

    // The one place POST and PUT fix the rate (Rule 2), so the two cannot diverge. On PUT, row still
    // holds the stored values it compares against.
    internal static AmountProblem? ApplyAmount(Transaction row, TransactionRequest request, NbuLookup? lookup)
    {
        (int RateE4, DateOnly? RateDate, RateSource? Source) rate;
        if (request.ManualRateE4 is { } manualRateE4)
        {
            rate = (manualRateE4, null, RateSource.Manual);
        }
        else if (request.Currency == Currency.UAH)
        {
            rate = (Money.RateScale, null, null);
        }
        else if (row.RateSource == RateSource.Nbu
            && row.Currency == request.Currency
            && row.ValueDate == request.ValueDate)
        {
            // The rate is fixed when recorded, so an edit of the other fields must not move it.
            rate = (row.RateE4, row.RateDate, RateSource.Nbu);
        }
        else if (lookup is NbuLookup.Found found)
        {
            rate = (found.RateE4, found.RateDate, RateSource.Nbu);
        }
        else
        {
            return new AmountProblem.RateUnavailable(request.Currency, request.ValueDate, lookup!);
        }

        if (ExceedsUahBound(request.AmountMinor, rate.RateE4))
        {
            var tooLarge = new FieldErrors();
            tooLarge.Set(
                Field(nameof(request.AmountMinor)),
                ProblemCodes.AmountTooLarge,
                $"amountMinor at this rate must not exceed {MaxAmountMinor} kopecks in hryvnia.");

            return new AmountProblem.Invalid(tooLarge);
        }

        row.ValueDate = request.ValueDate;
        row.AmountMinor = request.AmountMinor;
        row.Currency = request.Currency;
        row.RateE4 = rate.RateE4;
        row.RateDate = rate.RateDate;
        row.RateSource = rate.Source;
        row.AmountUahKop = Money.ToUahKop(request.AmountMinor, rate.RateE4);

        return null;
    }

    internal static bool ExceedsUahBound(long amountMinor, int rateE4) =>
        (Int128)amountMinor * rateE4 > (Int128)MaxAmountMinor * Money.RateScale;

    internal static async Task<Guid?> ResolveClientAsync(
        AppDbContext database, string userId, string? name, CancellationToken cancellationToken)
    {
        if (name is null)
        {
            return null;
        }

        var existingId = await database.Clients
            .Where(client => client.UserId == userId && client.Name == name)
            .Select(client => (Guid?)client.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId;
        }

        var client = new Client { Id = Guid.NewGuid(), UserId = userId, Name = name };
        database.Clients.Add(client);

        return client.Id;
    }
}
