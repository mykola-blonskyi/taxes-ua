using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Notifications;

// The real MailKit transport against a small SMTP server on the loopback interface, so the bytes that
// would leave for a provider are read back, and the way each kind of failure is classified is proved on
// the wire rather than assumed.
public sealed class SmtpEmailTransportTests
{
    private static readonly EmailMessage Message = new(
        "owner@mail.test",
        "Підтвердьте адресу",
        "Перший рядок\nhttps://taxes.test/settings?x=1",
        "<p>Перший рядок</p><p><a href=\"https://taxes.test/settings?x=1\">https://taxes.test/settings?x=1</a></p>");

    [Fact]
    public async Task A_message_goes_out_as_plain_text_and_html_from_the_configured_sender()
    {
        await using var server = new FakeSmtpServer();

        var result = await Transport(server.Port).SendAsync(Message, CancellationToken.None);

        Assert.True(result.IsOk);
        var received = Assert.Single(server.Messages);
        var mime = MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(received)));
        var from = mime.From.Mailboxes.Single();
        Assert.Equal(("Taxes", "noreply@taxes.test"), (from.Name, from.Address));
        Assert.Equal("owner@mail.test", mime.To.Mailboxes.Single().Address);
        Assert.Equal(Message.Subject, mime.Subject);
        var alternative = Assert.IsType<MultipartAlternative>(mime.Body);
        Assert.Equal(Message.Text, alternative.OfType<TextPart>().Single(part => part.IsPlain).GetText(out _).ReplaceLineEndings("\n").TrimEnd());
        Assert.Equal(Message.Html, alternative.OfType<TextPart>().Single(part => part.IsHtml).GetText(out _).TrimEnd());
        Assert.Equal("<noreply@taxes.test>", server.MailFrom);
        Assert.Equal("<owner@mail.test>", server.RcptTo);
    }

    [Theory]
    [InlineData("450 4.2.1 mailbox busy", "ServerError")]
    [InlineData("451 4.3.0 try later", "ServerError")]
    [InlineData("550 5.1.1 no such user", "Rejected")]
    [InlineData("554 5.7.1 refused", "Rejected")]
    public async Task The_servers_answer_to_the_recipient_decides_whether_it_is_worth_retrying(string reply, string name)
    {
        var expected = Enum.Parse<DeliveryFailure>(name);
        await using var server = new FakeSmtpServer { RecipientReply = reply };

        var result = await Transport(server.Port).SendAsync(Message, CancellationToken.None);

        Assert.Equal(expected, result.Failure);
        Assert.Empty(server.Messages);
        Assert.True(expected == DeliveryFailure.ServerError ? expected.IsTransient() : !expected.IsTransient());
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_unreachable_and_worth_a_retry()
    {
        var port = FreePort();

        var result = await Transport(port).SendAsync(Message, CancellationToken.None);

        Assert.Equal(DeliveryFailure.Unreachable, result.Failure);
        Assert.True(result.Failure!.Value.IsTransient());
    }

    [Fact]
    public async Task A_connection_lost_after_the_message_was_handed_over_may_have_delivered_so_it_is_a_timeout()
    {
        await using var server = new FakeSmtpServer { DropAfterData = true };

        var result = await Transport(server.Port).SendAsync(Message, CancellationToken.None);

        Assert.Equal(DeliveryFailure.Timeout, result.Failure);
        Assert.False(result.Failure!.Value.IsTransient());
    }

    [Fact]
    public async Task Nothing_the_transport_logs_names_the_address_or_the_servers_words()
    {
        var logs = new CapturedLogs();
        await using var server = new FakeSmtpServer { RecipientReply = "550 5.1.1 owner@mail.test secret words" };
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        await Transport(server.Port, factory.CreateLogger<SmtpEmailTransport>()).SendAsync(Message, CancellationToken.None);

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, line => line.Contains("owner@mail.test", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("secret words", StringComparison.Ordinal));
    }

    [Fact]
    public void Failures_are_classified_by_whether_the_message_can_have_been_delivered()
    {
        Assert.Equal(DeliveryFailure.Authentication, SmtpEmailTransport.Classify(new MailKit.Security.AuthenticationException("no"), handedOver: false));
        Assert.Equal(DeliveryFailure.Authentication, SmtpEmailTransport.Classify(new ServiceNotAuthenticatedException("530 authentication required"), handedOver: true));
        Assert.Equal(DeliveryFailure.Unreachable, SmtpEmailTransport.Classify(new SocketException(), handedOver: false));
        Assert.Equal(DeliveryFailure.Unreachable, SmtpEmailTransport.Classify(new TimeoutException(), handedOver: false));
        Assert.Equal(DeliveryFailure.Timeout, SmtpEmailTransport.Classify(new TimeoutException(), handedOver: true));
        Assert.Equal(DeliveryFailure.Timeout, SmtpEmailTransport.Classify(new IOException(), handedOver: true));
        Assert.Equal(DeliveryFailure.Timeout, SmtpEmailTransport.Classify(new OperationCanceledException(), handedOver: true));
        Assert.Equal(DeliveryFailure.Unreadable, SmtpEmailTransport.Classify(new InvalidOperationException(), handedOver: true));
        Assert.Equal(
            DeliveryFailure.ServerError,
            SmtpEmailTransport.Classify(
                new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "x"), handedOver: true));
        Assert.Equal(
            DeliveryFailure.Rejected,
            SmtpEmailTransport.Classify(
                new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "x"), handedOver: false));
    }

    private const string Password = "w1re-t3st-p4ssw0rd";

    [Theory]
    [InlineData("235 2.7.0 welcome", true)]
    [InlineData("535 5.7.8 bad credentials", false)]
    public async Task The_password_is_sent_to_the_server_and_to_no_log_whether_or_not_it_is_accepted(string reply, bool accepted)
    {
        var logs = new CapturedLogs();
        await using var server = new FakeSmtpServer { AuthReply = reply };
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));

        var result = await Transport(server.Port, factory.CreateLogger<SmtpEmailTransport>(), credentials: true)
            .SendAsync(Message, CancellationToken.None);

        Assert.Equal(("mailer", Password), server.Credentials);
        Assert.Equal(accepted, result.IsOk);
        Assert.Equal(accepted ? null : DeliveryFailure.Authentication, result.Failure);
        Assert.Equal(accepted ? 1 : 0, server.Messages.Count);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Password, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("mailer", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("bad credentials", StringComparison.Ordinal));
    }

    private static SmtpEmailTransport Transport(int port, ILogger<SmtpEmailTransport>? logger = null, bool credentials = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Smtp:Tls"] = "none",
            ["Smtp:From"] = "Taxes <noreply@taxes.test>",
            ["App:PublicUrl"] = "https://taxes.test",
        };
        if (credentials)
        {
            values["Smtp:User"] = "mailer";
            values["Smtp:Password"] = Password;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var settings = new EmailSettings(configuration, new AppLink(configuration), NullLogger<EmailSettings>.Instance);
        Assert.True(settings.IsConfigured);

        return new SmtpEmailTransport(settings, logger ?? NullLogger<SmtpEmailTransport>.Instance);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    // Enough SMTP to receive one message at a time, in the clear.
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly List<string> _messages = [];

        public FakeSmtpServer()
        {
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string RecipientReply { get; init; } = "250 OK";

        public bool DropAfterData { get; init; }

        // When set, the server advertises AUTH PLAIN and answers the sign-in with this reply.
        public string? AuthReply { get; init; }

        public (string User, string Password)? Credentials { get; private set; }

        public string? MailFrom { get; private set; }

        public string? RcptTo { get; private set; }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                try
                {
                    await TalkAsync(client);
                }
                catch (IOException)
                {
                }
            }
        }

        private async Task TalkAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync(AuthReply is null ? "250 fake" : "250-fake\r\n250 AUTH PLAIN");
                        break;
                    case "AUTH":
                        var parts = line.Split(' ');
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parts[^1])).Split('\0');
                        Credentials = (decoded[1], decoded[2]);
                        await writer.WriteLineAsync(AuthReply ?? "504 no");
                        break;
                    case "MAIL":
                        MailFrom = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Split(' ')[0];
                        await writer.WriteLineAsync("250 OK");
                        break;
                    case "RCPT":
                        RcptTo = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Split(' ')[0];
                        await writer.WriteLineAsync(RecipientReply);
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go on");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync(_stop.Token) is { } body && body != ".")
                        {
                            data.Append(body.StartsWith("..", StringComparison.Ordinal) ? body[1..] : body).Append("\r\n");
                        }

                        if (DropAfterData)
                        {
                            return;
                        }

                        lock (_messages)
                        {
                            _messages.Add(data.ToString());
                        }

                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 OK");
                        break;
                }
            }
        }
    }
}
