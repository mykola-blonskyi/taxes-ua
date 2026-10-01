using System.Globalization;
using MailKit.Security;
using MimeKit;

namespace TaxesUa.Api.Features.Notifications;

internal enum SmtpTls
{
    StartTls,
    Implicit,
    None,
}

/// <summary>
/// The SMTP server reminders go through, read once from configuration (SMTP_HOST, SMTP_PORT,
/// SMTP_TLS, SMTP_USER, SMTP_PASSWORD, SMTP_FROM). The channel is available only when the server and
/// sender are valid and the confirmation link has an address to point at (<see cref="AppLink"/>).
/// A half-set or malformed configuration leaves it unavailable with a warning rather than failing
/// startup, like the Telegram token. This is a class and not a record so nothing prints the password,
/// and the settings are never logged.
/// </summary>
internal sealed class EmailSettings
{
    public EmailSettings(IConfiguration configuration, AppLink link, ILogger<EmailSettings> logger)
    {
        var host = Read(configuration, "Host");
        if (host is null)
        {
            return;
        }

        var tls = SmtpTls.StartTls;
        if (Read(configuration, "Tls") is { } tlsText && !TryParseTls(tlsText, out tls))
        {
            logger.LogWarning("SMTP_TLS must be starttls, implicit or none; email stays unavailable.");
            return;
        }

        var port = tls == SmtpTls.Implicit ? 465 : 587;
        if (Read(configuration, "Port") is { } portText
            && !(int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535))
        {
            logger.LogWarning("SMTP_PORT must be a port number; email stays unavailable.");
            return;
        }

        if (Read(configuration, "From") is not { } from || !MailboxAddress.TryParse(from, out var sender))
        {
            logger.LogWarning("SMTP_FROM is missing or is not an address; email stays unavailable.");
            return;
        }

        var user = Read(configuration, "User");
        var password = configuration["Smtp:Password"];
        if ((user is null) != string.IsNullOrEmpty(password))
        {
            logger.LogWarning("SMTP_USER and SMTP_PASSWORD must be set together; email stays unavailable.");
            return;
        }

        if (user is not null && tls == SmtpTls.None && !IsLoopback(host))
        {
            logger.LogWarning("SMTP credentials are set but SMTP_TLS is none and the host is not this machine; they would travel unencrypted, so email stays unavailable.");
            return;
        }

        if (link.Url is null)
        {
            logger.LogWarning("Email needs APP_PUBLIC_URL (or ALLOWED_HOSTS) for the confirmation link; email stays unavailable.");
            return;
        }

        Host = host;
        Port = port;
        Tls = tls;
        User = user;
        Password = password;
        From = sender;
        IsConfigured = true;
    }

    public bool IsConfigured { get; }

    public string Host { get; } = string.Empty;

    public int Port { get; }

    public SmtpTls Tls { get; }

    public string? User { get; }

    internal string? Password { get; }

    public MailboxAddress From { get; } = new(string.Empty, "unconfigured@invalid");

    public SecureSocketOptions SocketOptions => Tls switch
    {
        SmtpTls.Implicit => SecureSocketOptions.SslOnConnect,
        SmtpTls.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };

    // Credentials in the clear are tolerated only to a server on this machine, which is not a network.
    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));

    private static string? Read(IConfiguration configuration, string key) =>
        configuration[$"Smtp:{key}"] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool TryParseTls(string text, out SmtpTls tls) =>
        Enum.TryParse(text, ignoreCase: true, out tls) && Enum.IsDefined(tls);
}
