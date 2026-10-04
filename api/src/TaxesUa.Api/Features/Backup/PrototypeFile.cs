using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Backup;

/// <summary>
/// The owner's export from the prototype this app replaces. No sample of that export exists in the
/// repository, so this shape is derived from the ticket and the spec (knowledge/domain-model.md,
/// "Prototype file") and read strictly: a file that differs gets a 400 naming the field, rather than an
/// import of whatever happened to match.
/// </summary>
internal sealed record PrototypeFile(PrototypeIncome[] Incomes, PaidMonth[] PaidMonths)
{
    // Years of receipts are hundreds of records; this only bounds the work a hostile file can ask for.
    internal const int MaxIncomes = 10_000;

    private static readonly string[] TopLevel = ["settings", "incomes", "mpaid", "done"];

    private static readonly string[] IncomeFields = ["date", "amount", "currency", "rate", "uah", "client", "invoice", "comment"];

    private static readonly Regex Decimal = new(@"^-?[0-9]{1,30}(\.[0-9]{1,30})?$", RegexOptions.CultureInvariant);

    private static readonly Regex MonthKey = new(@"^([0-9]{4})-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant);

    public static PrototypeFile? Parse(JsonElement root, DateOnly today, out FieldErrors errors)
    {
        errors = new FieldErrors();
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Set("file", ProblemCodes.NotAnObject, "The file must be a JSON object with incomes and mpaid.");
            return null;
        }

        JsonElement? incomesElement = null;
        JsonElement? mpaidElement = null;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "incomes":
                    incomesElement = property.Value;
                    break;
                case "mpaid":
                    mpaidElement = property.Value;
                    break;
                case "settings" or "done":
                    break;
                default:
                    errors.Set(
                        property.Name,
                        ProblemCodes.UnknownField,
                        $"{property.Name} is not a field of a prototype file ({string.Join(", ", TopLevel)}).");
                    break;
            }
        }

        if (incomesElement is null && mpaidElement is null)
        {
            errors.Set(
                "file",
                ProblemCodes.NotAPrototypeExport,
                "The file has neither incomes nor mpaid, so it is not a prototype export.");
        }

        var incomes = incomesElement is { } list ? ParseIncomes(list, today, errors) : [];
        var paidMonths = mpaidElement is { } map ? ParsePaidMonths(map, today, errors) : [];

        return errors.Count == 0 ? new PrototypeFile(incomes, paidMonths) : null;
    }

    private static PrototypeIncome[] ParseIncomes(JsonElement list, DateOnly today, FieldErrors errors)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Set("incomes", ProblemCodes.NotAnArray, "incomes must be an array.");
            return [];
        }

        if (list.GetArrayLength() > MaxIncomes)
        {
            errors.Set(
                "incomes",
                ProblemCodes.TooManyRecords,
                $"incomes must not hold more than {MaxIncomes} records.");
            return [];
        }

        var incomes = new List<PrototypeIncome>();
        var i = 0;
        foreach (var element in list.EnumerateArray())
        {
            if (ParseIncome(element, $"incomes[{i}]", today, errors) is { } income)
            {
                incomes.Add(income);
            }

            i++;
        }

        return [.. incomes];
    }

    private static PrototypeIncome? ParseIncome(
        JsonElement element, string at, DateOnly today, FieldErrors errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Set(at, ProblemCodes.NotAnObject, "An income must be an object.");
            return null;
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var failed = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!IncomeFields.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Set(
                    $"{at}.{property.Name}",
                    ProblemCodes.UnknownField,
                    $"{property.Name} is not a field of an income ({string.Join(", ", IncomeFields)}).");
                failed = true;
            }
            else if (!fields.TryAdd(property.Name, property.Value))
            {
                errors.Set($"{at}.{property.Name}", ProblemCodes.DuplicateValue, $"{property.Name} appears twice.");
                failed = true;
            }
        }

        void Fail(string field, string code, string message)
        {
            errors.Set($"{at}.{field}", code, message);
            failed = true;
        }

        DateOnly date = default;
        if (Text(fields, "date") is not { } dateText
            || !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            Fail("date", ProblemCodes.InvalidDate, "date must be a YYYY-MM-DD string.");
        }

        Currency currency = default;
        if (Text(fields, "currency") is not { } currencyText
            || currencyText is not ("UAH" or "USD" or "EUR")
            || !Enum.TryParse(currencyText, out currency))
        {
            Fail("currency", ProblemCodes.InvalidValue, "currency must be UAH, USD or EUR.");
        }

        long amountMinor = 0;
        if (Scaled(fields, "amount", scale: 2, round: false) is { } amount)
        {
            amountMinor = amount;
        }
        else
        {
            Fail("amount", ProblemCodes.InvalidAmount, "amount must be a decimal number with at most 2 decimal places.");
        }

        // The prototype computes uah in floating point, so it can carry digits past the kopeck: they are
        // rounded half away from zero, as Rule 10 rounds everywhere else.
        long uahKop = 0;
        if (Scaled(fields, "uah", scale: 2, round: true) is { } uah)
        {
            uahKop = uah;
        }
        else
        {
            Fail("uah", ProblemCodes.InvalidAmount, "uah must be a decimal number.");
        }

        var rateE4 = Money.RateScale;
        if (fields.TryGetValue("rate", out var rateElement) && rateElement.ValueKind != JsonValueKind.Null)
        {
            if (Scaled(fields, "rate", scale: 4, round: true) is { } rate && rate is >= 0 and <= int.MaxValue)
            {
                rateE4 = (int)rate;
            }
            else
            {
                Fail("rate", ProblemCodes.InvalidRate, "rate must be a positive decimal number.");
            }
        }
        else if (currency != Currency.UAH)
        {
            Fail("rate", ProblemCodes.Required, "rate is required for a USD or EUR income.");
        }

        var client = OptionalText(fields, "client", Fail);
        var invoice = OptionalText(fields, "invoice", Fail);
        var comment = OptionalText(fields, "comment", Fail);
        if (failed)
        {
            return null;
        }

        if (currency == Currency.UAH && rateE4 != Money.RateScale)
        {
            Fail("rate", ProblemCodes.InvalidRate, "A UAH income has rate 1 or none.");
            return null;
        }

        var income = new PrototypeIncome(date, amountMinor, currency, rateE4, uahKop, client, invoice, comment);
        var request = income.ToRequest();
        if (TransactionsEndpoints.Validate(request, TransactionsEndpoints.Normalize(request), today) is { } invalid)
        {
            errors.Merge(at, invalid, PrototypeName);
            failed = true;
        }

        if (failed)
        {
            return null;
        }

        if (TransactionsEndpoints.ExceedsUahBound(amountMinor, rateE4))
        {
            Fail("amount", ProblemCodes.AmountTooLarge, "amount at this rate exceeds the largest hryvnia amount.");
            return null;
        }

        // Rule 2's formula is what every stored row satisfies and what a backup restore checks, so a uah
        // the rate cannot reproduce would import once and then break the owner's next restore.
        var expected = Money.ToUahKop(amountMinor, rateE4);
        if (uahKop != expected)
        {
            Fail("uah", ProblemCodes.UahMismatch, $"uah is {Hryvnia(uahKop)}, but amount × rate rounds to {Hryvnia(expected)}.");
            return null;
        }

        return income;
    }

    private static PaidMonth[] ParsePaidMonths(JsonElement map, DateOnly today, FieldErrors errors)
    {
        if (map.ValueKind != JsonValueKind.Object)
        {
            errors.Set("mpaid", ProblemCodes.NotAnObject, "mpaid must be an object of YYYY-MM keys and true or false.");
            return [];
        }

        var months = new List<PaidMonth>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in map.EnumerateObject())
        {
            var at = $"mpaid.{property.Name}";
            var match = MonthKey.Match(property.Name);
            if (!match.Success)
            {
                errors.Set(at, ProblemCodes.InvalidMonthKey, "An mpaid key must be a YYYY-MM month.");
                continue;
            }

            if (!seen.Add(property.Name))
            {
                errors.Set(at, ProblemCodes.DuplicateValue, "This month appears twice.");
                continue;
            }

            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                errors.Set(at, ProblemCodes.NotBoolean, "An mpaid value must be true or false.");
                continue;
            }

            var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (year < Limits.MinYear || year > Limits.MaxYear)
            {
                errors.Set(
                    at,
                    ProblemCodes.YearOutOfRange,
                    $"The year must be between {Limits.MinYear} and {Limits.MaxYear}.");
            }
            else if (property.Value.ValueKind == JsonValueKind.False)
            {
                continue;
            }
            else if (new DateOnly(year, month, 1) > today)
            {
                errors.Set(at, ProblemCodes.DateInFuture, "A month after the current one cannot have been paid.");
            }
            else
            {
                months.Add(new PaidMonth(year, month));
            }
        }

        return [.. months];
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? OptionalText(
        Dictionary<string, JsonElement> fields, string name, Action<string, string, string> fail)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            fail(name, ProblemCodes.InvalidValue, $"{name} must be a string.");
            return null;
        }

        return value.GetString();
    }

    /// <summary>
    /// A JSON number or numeric string, read from its text and scaled to <paramref name="scale"/>
    /// decimal places with integer arithmetic: a double cannot hold every kopeck of a large amount.
    /// </summary>
    private static long? Scaled(Dictionary<string, JsonElement> fields, string name, int scale, bool round)
    {
        if (!fields.TryGetValue(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString()!.Trim(),
            _ => null,
        };
        if (text is null || !Decimal.IsMatch(text))
        {
            return null;
        }

        var negative = text.StartsWith('-');
        var parts = text.TrimStart('-').Split('.');
        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if (fraction.Length > scale && !round)
        {
            return null;
        }

        var kept = fraction.Length > scale ? fraction[..scale] : fraction.PadRight(scale, '0');
        var magnitude = BigInteger.Parse(parts[0] + kept, CultureInfo.InvariantCulture);
        if (fraction.Length > scale && fraction[scale] >= '5')
        {
            magnitude += 1;
        }

        var result = negative ? -magnitude : magnitude;
        return result >= long.MinValue && result <= long.MaxValue ? (long)result : null;
    }

    private static string PrototypeName(string requestField) => requestField switch
    {
        "valueDate" => "date",
        "amountMinor" => "amount",
        "manualRateE4" => "rate",
        "clientName" => "client",
        "invoiceNumber" => "invoice",
        "description" => "comment",
        _ => requestField,
    };

    private static string Hryvnia(long kop) =>
        string.Create(CultureInfo.InvariantCulture, $"{(kop < 0 ? "-" : "")}{Math.Abs(kop / 100)}.{Math.Abs(kop % 100):00}");
}

internal sealed record PrototypeIncome(
    DateOnly Date,
    long AmountMinor,
    Currency Currency,
    int RateE4,
    long UahKop,
    string? Client,
    string? Invoice,
    string? Comment)
{
    public TransactionRequest ToRequest() => new(
        Date,
        AmountMinor,
        Currency,
        Currency == Currency.UAH ? null : RateE4,
        TransactionKind.Income,
        NonIncomeReason: null,
        Client,
        Invoice,
        Comment,
        RefundsTransactionId: null);
}

internal sealed record PaidMonth(int Year, int Month);
