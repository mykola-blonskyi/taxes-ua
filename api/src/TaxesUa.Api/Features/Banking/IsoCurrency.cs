using System.Globalization;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Banking;

/// <summary>The ISO 4217 numeric codes a bank reports, mapped to the currencies the app records.</summary>
internal static class IsoCurrency
{
    private static readonly Dictionary<int, Currency> Known = new()
    {
        [980] = Currency.UAH,
        [840] = Currency.USD,
        [978] = Currency.EUR,
    };

    public static Currency? FromNumeric(int code) => Known.TryGetValue(code, out var currency) ? currency : null;

    // An unsupported currency stays visible as its bare number rather than disappearing.
    public static string Display(int code) =>
        FromNumeric(code)?.ToString() ?? code.ToString(CultureInfo.InvariantCulture);
}
