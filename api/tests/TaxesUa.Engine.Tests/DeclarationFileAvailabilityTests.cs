namespace TaxesUa.Engine.Tests;

public class DeclarationFileAvailabilityTests
{
    [Theory]
    [InlineData(2026, 1, "2026-04-01")]
    [InlineData(2026, 2, "2026-07-01")]
    [InlineData(2026, 3, "2026-10-01")]
    [InlineData(2026, 4, "2027-01-01")]
    public void The_file_opens_the_day_after_the_quarters_last_day(int year, int quarter, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), Declaration.FileAvailableFrom(year, quarter));
    }

    [Fact]
    public void The_last_day_of_the_quarter_is_still_too_early_and_the_next_day_is_not()
    {
        Assert.False(Declaration.FileAvailable(2026, 3, new DateOnly(2026, 9, 30)));
        Assert.True(Declaration.FileAvailable(2026, 3, new DateOnly(2026, 10, 1)));
        Assert.False(Declaration.FileAvailable(2026, 4, new DateOnly(2026, 12, 31)));
        Assert.True(Declaration.FileAvailable(2026, 4, new DateOnly(2027, 1, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_quarter_outside_1_to_4_is_a_caller_error(int quarter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Declaration.FileAvailableFrom(2026, quarter));
    }
}
