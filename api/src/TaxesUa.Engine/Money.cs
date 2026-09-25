namespace TaxesUa.Engine;

/// <summary>
/// Integer money arithmetic. Amounts are minor units (kopecks, cents), rates are scaled by 10^4,
/// percentages are basis points. Every operation rounds once, half away from zero.
/// </summary>
public static class Money
{
    public const int RateScale = 10_000;
    public const int BasisPointScale = 10_000;

    public static long ToUahKop(long amountMinor, int rateE4) =>
        DivRoundHalfUp(amountMinor * rateE4, RateScale);

    public static long ApplyBp(long amountKop, int rateBp) =>
        DivRoundHalfUp(amountKop * rateBp, BasisPointScale);

    public static int ToRateE4(decimal rate) =>
        checked((int)decimal.Round(rate * RateScale, 0, MidpointRounding.AwayFromZero));

    private static long DivRoundHalfUp(long numerator, long denominator)
    {
        var (q, r) = Math.DivRem(numerator, denominator);
        if (Math.Abs(r) * 2 >= denominator) q += Math.Sign(numerator);
        return q;
    }
}
