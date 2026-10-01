using MailKit.Security;
using Microsoft.Extensions.Configuration;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Notifications;

public sealed class EmailSettingsTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Smtp:Host"] = "smtp.example.com",
        ["Smtp:Port"] = "587",
        ["Smtp:Tls"] = "starttls",
        ["Smtp:User"] = "mailer",
        ["Smtp:Password"] = "p4ss",
        ["Smtp:From"] = "Taxes <noreply@example.com>",
        ["App:PublicUrl"] = "https://taxes.example.com",
    };

    [Fact]
    public void Complete_settings_make_the_channel_available()
    {
        var settings = Settings(Complete);

        Assert.True(settings.IsConfigured);
        Assert.Equal(("smtp.example.com", 587, SmtpTls.StartTls, "mailer"), (settings.Host, settings.Port, settings.Tls, settings.User));
        Assert.Equal(SecureSocketOptions.StartTls, settings.SocketOptions);
        Assert.Equal("noreply@example.com", settings.From.Address);
    }

    [Fact]
    public void A_server_without_credentials_is_allowed_and_the_port_follows_the_tls_mode()
    {
        var settings = Settings(Without("Smtp:User", "Smtp:Password", "Smtp:Port"), ("Smtp:Tls", "implicit"));

        Assert.True(settings.IsConfigured);
        Assert.Equal((465, SecureSocketOptions.SslOnConnect, null), (settings.Port, settings.SocketOptions, settings.User));
        Assert.Equal(587, Settings(Without("Smtp:Port")).Port);
    }

    [Fact]
    public void Without_a_host_nothing_is_configured_and_nothing_is_said()
    {
        Assert.False(Settings(Without("Smtp:Host")).IsConfigured);
        Assert.False(Settings(new Dictionary<string, string?>()).IsConfigured);
    }

    [Theory]
    [InlineData("Smtp:From", "")]
    [InlineData("Smtp:From", "not an address")]
    [InlineData("Smtp:Port", "0")]
    [InlineData("Smtp:Port", "99999")]
    [InlineData("Smtp:Port", "smtp")]
    [InlineData("Smtp:Tls", "ssl3")]
    [InlineData("Smtp:Password", "")]
    [InlineData("Smtp:User", "")]
    [InlineData("App:PublicUrl", "")]
    public void A_missing_or_malformed_setting_leaves_the_channel_unavailable(string key, string value)
    {
        Assert.False(Settings(Complete, (key, value)).IsConfigured);
    }

    [Fact]
    public void Credentials_are_never_sent_without_encryption()
    {
        Assert.False(Settings(Complete, ("Smtp:Tls", "none")).IsConfigured);
        Assert.True(Settings(Complete, ("Smtp:Tls", "none"), ("Smtp:Host", "127.0.0.1")).IsConfigured);
        Assert.True(Settings(Complete, ("Smtp:Tls", "none"), ("Smtp:Host", "localhost")).IsConfigured);
        Assert.True(Settings(Without("Smtp:User", "Smtp:Password"), ("Smtp:Tls", "none")).IsConfigured);
    }

    private static Dictionary<string, string?> Without(params string[] keys)
    {
        var copy = new Dictionary<string, string?>(Complete);
        foreach (var key in keys)
        {
            copy.Remove(key);
        }

        return copy;
    }

    private static EmailSettings Settings(Dictionary<string, string?> values, params (string Key, string Value)[] changes)
    {
        var all = new Dictionary<string, string?>(values);
        foreach (var (key, value) in changes)
        {
            all[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(all).Build();

        return new EmailSettings(configuration, new AppLink(configuration), Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailSettings>.Instance);
    }
}
