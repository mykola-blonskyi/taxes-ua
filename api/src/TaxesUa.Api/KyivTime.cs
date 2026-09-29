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

    public static DateTimeOffset KyivMidnight(this DateOnly date) => date.InKyiv(TimeOnly.MinValue);

    public static DateTimeOffset InKyiv(this DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, Kyiv.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateOnly KyivDate(this DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Kyiv).DateTime);
}
