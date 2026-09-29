namespace TaxesUa.Api;

/// <summary>
/// Rule 10 dates every operation by Europe/Kyiv, so "today" is a Kyiv calendar day and never the
/// server's or UTC's.
/// </summary>
internal static class KyivTime
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public static DateOnly TodayInKyiv(this TimeProvider time) => DateOnly.FromDateTime(time.NowInKyiv());

    public static DateTime NowInKyiv(this TimeProvider time) => TimeZoneInfo.ConvertTime(time.GetUtcNow(), Kyiv).DateTime;

    public static DateTimeOffset KyivMidnight(this DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, Kyiv.GetUtcOffset(midnight)).ToUniversalTime();
    }

    public static DateOnly KyivDate(this DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Kyiv).DateTime);
}
