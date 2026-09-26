using System.Globalization;
using System.Text.Json;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Fx;

internal abstract record NbuLookup
{
    private NbuLookup() { }

    public sealed record Found(int RateE4, DateOnly RateDate) : NbuLookup;

    public sealed record NoRate : NbuLookup;

    public sealed record Unavailable(string Reason) : NbuLookup;
}

internal sealed class NbuRateClient(HttpClient http)
{
    public const int MaxDaysBack = 7;

    public async Task<NbuLookup> GetAsync(Currency currency, DateOnly date, CancellationToken cancellationToken)
    {
        for (var day = date; day > date.AddDays(-MaxDaysBack); day = day.AddDays(-1))
        {
            if (await GetDayAsync(currency, day, cancellationToken) is { } lookup)
            {
                return lookup;
            }
        }

        return new NbuLookup.NoRate();
    }

    internal static int ToRateE4(decimal rate) =>
        checked((int)decimal.Round(rate * Money.RateScale, 0, MidpointRounding.AwayFromZero));

    // Null means NBU answered `[]` for the day, the one answer that sends the search a day back. Any
    // other surprise stops it: a later day's rate is not a fallback for a broken answer.
    private async Task<NbuLookup?> GetDayAsync(Currency currency, DateOnly day, CancellationToken cancellationToken)
    {
        var uri = "NBUStatService/v1/statdirectory/exchange"
            + $"?valcode={currency}&date={day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&json";

        string body;
        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new NbuLookup.Unavailable($"NBU answered {(int)response.StatusCode} for {day:O}.");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new NbuLookup.Unavailable($"NBU request for {day:O} failed: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NbuLookup.Unavailable($"NBU did not answer for {day:O} within {http.Timeout}.");
        }

        return Parse(body, currency, day);
    }

    private static NbuLookup? Parse(string body, Currency currency, DateOnly day)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Malformed(day, "the body is not JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return Malformed(day, "the body is not an array");
            }

            if (root.GetArrayLength() == 0)
            {
                return null;
            }

            var row = root[0];
            if (row.ValueKind != JsonValueKind.Object)
            {
                return Malformed(day, "the row is not an object");
            }

            if (!row.TryGetProperty("cc", out var code)
                || code.ValueKind != JsonValueKind.String
                || code.GetString() != currency.ToString())
            {
                return Malformed(day, $"the row is not for {currency}");
            }

            if (!row.TryGetProperty("exchangedate", out var exchangeDate)
                || exchangeDate.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(
                    exchangeDate.GetString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var published)
                || published != day)
            {
                return Malformed(day, "the row is not for the requested date");
            }

            if (!row.TryGetProperty("rate", out var rate)
                || rate.ValueKind != JsonValueKind.Number
                || !rate.TryGetDecimal(out var value)
                || value <= 0)
            {
                return Malformed(day, "the rate is not a positive number");
            }

            int rateE4;
            try
            {
                rateE4 = ToRateE4(value);
            }
            catch (OverflowException)
            {
                return Malformed(day, "the rate is out of range");
            }

            return rateE4 > 0 ? new NbuLookup.Found(rateE4, day) : Malformed(day, "the rate rounds to zero");
        }
    }

    private static NbuLookup.Unavailable Malformed(DateOnly day, string what) =>
        new($"NBU answer for {day:O} is unusable: {what}.");
}
