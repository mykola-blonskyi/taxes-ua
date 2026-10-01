using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class EmailTextsTests
{
    [Fact]
    public void Html_is_the_text_escaped_with_a_paragraph_per_line_and_links_made_clickable()
    {
        var html = EmailTexts.ToHtml("<b>Tax</b> & more\nOpen: https://taxes.test/settings?a=1&b=2 now");

        Assert.Equal(
            "<!DOCTYPE html><html><body style=\"font-family:sans-serif;line-height:1.5\">"
            + "<p>&lt;b&gt;Tax&lt;/b&gt; &amp; more</p>"
            + "<p>Open: <a href=\"https://taxes.test/settings?a=1&amp;b=2\">https://taxes.test/settings?a=1&amp;b=2</a> now</p>"
            + "</body></html>",
            html);
    }

    [Theory]
    [InlineData("name@example.com", true)]
    [InlineData("  name+tag@sub.example.co.uk ", true)]
    [InlineData("o'brien@example.com", true)]
    [InlineData("name@example", false)]
    [InlineData("@example.com", false)]
    [InlineData("name@@example.com", false)]
    [InlineData("name@example..com", false)]
    [InlineData("Name <name@example.com>", false)]
    [InlineData("a@example.com,b@example.com", false)]
    [InlineData("a@example.com\nBcc: b@example.com", false)]
    [InlineData("\"quoted\"@example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_plain_address_is_accepted(string? input, bool accepted)
    {
        Assert.Equal(accepted, EmailTexts.TryNormalize(input, out var address));
        Assert.Equal(accepted ? input!.Trim() : string.Empty, address);
    }

    [Fact]
    public void An_address_longer_than_254_characters_is_refused()
    {
        Assert.False(EmailTexts.TryNormalize(new string('a', 250) + "@example.com", out _));
    }
}
