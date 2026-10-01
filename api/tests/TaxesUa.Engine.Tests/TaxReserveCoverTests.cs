namespace TaxesUa.Engine.Tests;

public class TaxReserveCoverTests
{
    private static readonly DateOnly First = new(2026, 5, 20);
    private static readonly DateOnly Second = new(2026, 8, 19);
    private static readonly DateOnly Third = new(2026, 11, 19);

    // 1,000.00 by 20 May (ESV only), 2,500.00 by 19 August (tax and levy), 400.00 by 19 November.
    private static readonly ReserveNeed Need = new(
    [
        new ReserveDue(First, ObligationStatus.Overdue, 0, 0, 100_000),
        new ReserveDue(Second, ObligationStatus.Upcoming, 200_000, 50_000, 0),
        new ReserveDue(Third, ObligationStatus.Upcoming, 0, 0, 40_000),
    ]);

    [Theory]
    [InlineData(390_000, 0, 0, 0)]
    [InlineData(500_000, 110_000, 0, 0)]
    [InlineData(390_001, 1, 0, 0)]
    public void A_balance_that_covers_everything_is_a_surplus_and_asks_for_nothing(
        long balance, long surplus, long shortfall, long topUp)
    {
        var cover = TaxReserve.Cover(Need, balance);

        Assert.Equal(new ReserveCover(balance, surplus, shortfall, null, topUp), cover);
    }

    [Fact]
    public void A_balance_short_of_the_first_deadline_asks_for_the_rest_of_it_by_that_date()
    {
        var cover = TaxReserve.Cover(Need, 30_000);

        Assert.Equal(new ReserveCover(30_000, 0, 360_000, First, 70_000), cover);
    }

    [Fact]
    public void A_balance_that_pays_the_early_deadlines_is_short_from_the_first_one_it_does_not_cover()
    {
        // 1,000.00 pays the ESV; of the 2,500.00 due in August only 500.00 is there, so 2,000.00 more is needed by then.
        var cover = TaxReserve.Cover(Need, 150_000);

        Assert.Equal(new ReserveCover(150_000, 0, 240_000, Second, 200_000), cover);
    }

    [Fact]
    public void When_only_the_last_deadline_is_uncovered_the_top_up_is_the_whole_shortfall()
    {
        var cover = TaxReserve.Cover(Need, 380_000);

        Assert.Equal(new ReserveCover(380_000, 0, 10_000, Third, 10_000), cover);
        Assert.Equal(cover.ShortfallKop, cover.TopUpKop);
    }

    [Fact]
    public void A_balance_exactly_at_a_deadline_covers_it()
    {
        var cover = TaxReserve.Cover(Need, 350_000);

        Assert.Equal(new ReserveCover(350_000, 0, 40_000, Third, 40_000), cover);
    }

    [Fact]
    public void Nothing_needed_is_covered_by_any_balance_including_none()
    {
        var empty = new ReserveNeed([]);

        Assert.Equal(new ReserveCover(0, 0, 0, null, 0), TaxReserve.Cover(empty, 0));
        Assert.Equal(new ReserveCover(12_300, 12_300, 0, null, 0), TaxReserve.Cover(empty, 12_300));
    }

    [Fact]
    public void A_negative_balance_counts_as_zero()
    {
        Assert.Equal(TaxReserve.Cover(Need, 0), TaxReserve.Cover(Need, -5_000));
    }

    [Fact]
    public void The_shortfall_and_the_surplus_are_never_both_set()
    {
        foreach (var balance in new long[] { 0, 99_999, 100_000, 349_999, 350_000, 389_999, 390_000, 390_001, 10_000_000 })
        {
            var cover = TaxReserve.Cover(Need, balance);

            Assert.True(cover.SurplusKop == 0 || cover.ShortfallKop == 0, $"balance {balance}");
            Assert.Equal(balance - Need.TotalKop, cover.SurplusKop - cover.ShortfallKop);
            Assert.True(cover.TopUpKop <= cover.ShortfallKop, $"balance {balance}");
        }
    }
}
