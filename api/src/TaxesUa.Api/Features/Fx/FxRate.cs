namespace TaxesUa.Api.Features.Fx;

internal sealed class FxRate
{
    public Currency Currency { get; set; }

    // The date that was asked for. RateDate is the NBU date the rate belongs to, earlier than Date
    // when NBU published nothing on Date itself.
    public DateOnly Date { get; set; }

    public int RateE4 { get; set; }

    public DateOnly RateDate { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
