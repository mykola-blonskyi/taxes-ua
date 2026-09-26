using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Fx;

public sealed class NbuRateClientTests
{
    [Theory]
    [InlineData("44.9729", 449_729)]
    [InlineData("44.97285", 449_729)]
    [InlineData("44.97284", 449_728)]
    public void ToRateE4_rounds_the_fifth_decimal_half_away_from_zero(string rate, int expectedRateE4) =>
        Assert.Equal(expectedRateE4, NbuRateClient.ToRateE4(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)));
}
