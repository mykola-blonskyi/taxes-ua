namespace TaxesUa.Engine;

/// <summary>
/// The purpose line of a budget transfer (Rule 16): payment type code 101, the kind in words and the
/// period. The payer's RNOKPP is not in it, it travels as the payer code of the transfer.
/// </summary>
public static class PaymentPurpose
{
    private const string TypeCode = "101";

    private static readonly string[] Quarters = ["I", "II", "III", "IV"];

    private static readonly string[] Months =
    [
        "січень", "лютий", "березень", "квітень", "травень", "червень",
        "липень", "серпень", "вересень", "жовтень", "листопад", "грудень",
    ];

    public static string ForQuarter(PaymentKind kind, int year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, Quarters.Length);
        return Compose(kind, $"{Quarters[quarter - 1]} квартал", year);
    }

    public static string ForMonth(PaymentKind kind, int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, Months.Length);
        return Compose(kind, Months[month - 1], year);
    }

    private static string Compose(PaymentKind kind, string period, int year) =>
        $"{TypeCode} {KindWords(kind)} за {period} {year} року";

    private static string KindWords(PaymentKind kind) => kind switch
    {
        PaymentKind.SingleTax => "єдиний податок",
        PaymentKind.MilitaryLevy => "військовий збір",
        PaymentKind.Esv => "єдиний внесок",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
