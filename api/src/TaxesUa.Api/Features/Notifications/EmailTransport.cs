using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>An email as the transport sends it: plain text and a simple HTML rendering of the same words.</summary>
internal sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>
/// Hands one email to the SMTP server. Tests replace it with an in-memory sender, so everything above
/// it (the confirmation, retries, the failure record) is exercised without a network.
/// </summary>
internal interface IEmailTransport
{
    Task<DeliveryAttempt> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// MailKit over the configured server (ADR-022). It connects and signs in first, then sends, because
/// that split decides how a timeout reads: before the message is handed over nothing can have been
/// delivered, so it is plain unreachability and worth a retry; after it, the server may have accepted
/// the message before the answer was lost, so it is a Timeout, which is never retried (ADR-019).
/// Nothing from an exception or from the server's answer is logged: they can quote the address, and
/// the credentials are never anywhere near a log call.
/// </summary>
internal sealed class SmtpEmailTransport(EmailSettings settings, ILogger<SmtpEmailTransport> logger) : IEmailTransport
{
    private const int TimeoutMilliseconds = 20_000;

    // SmtpClient.Timeout applies to each operation, so connecting, signing in and sending could each
    // take it in turn. The owner's test button and confirmation wait on one attempt behind a proxy that
    // gives up at 30 seconds, so the whole attempt has this one deadline.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(25);

    public async Task<DeliveryAttempt> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);
        var handedOver = false;
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, deadline.Token);
            if (settings.User is not null)
            {
                await client.AuthenticateAsync(settings.User, settings.Password!, deadline.Token);
            }

            handedOver = true;
            await client.SendAsync(Build(message), deadline.Token);
            await QuitAsync(client, deadline.Token);

            return new DeliveryAttempt();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = Classify(exception, handedOver);
            logger.LogWarning("SMTP delivery did not complete: {Failure} ({Kind}).", failure, exception.GetType().Name);

            return new DeliveryAttempt(failure);
        }
    }

    // The server has accepted the message by now; a goodbye that fails or hangs must not turn that into a
    // failure the owner would see and a reminder would not retry.
    private static async Task QuitAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The client is disposed by the caller, which closes the connection.
        }
    }

    // A closed set the delivery code branches on, like the Telegram client's. `handedOver` is whether
    // the failure came after the message started to travel.
    internal static DeliveryFailure Classify(Exception exception, bool handedOver) => exception switch
    {
        MailKit.Security.AuthenticationException or ServiceNotAuthenticatedException => DeliveryFailure.Authentication,
        MailKit.Security.SslHandshakeException => DeliveryFailure.Unreachable,
        SmtpCommandException command => ClassifyCommand(command),
        SmtpProtocolException => handedOver ? DeliveryFailure.Timeout : DeliveryFailure.Unreachable,
        TimeoutException or OperationCanceledException or IOException when handedOver => DeliveryFailure.Timeout,
        TimeoutException or OperationCanceledException => DeliveryFailure.Unreachable,
        SocketException or IOException or ServiceNotConnectedException => DeliveryFailure.Unreachable,
        _ => DeliveryFailure.Unreadable,
    };

    // 4xx replies are the server asking to try later and mean the message was not accepted. 5xx are
    // final: a bad mailbox, a refused sender, a policy block.
    private static DeliveryFailure ClassifyCommand(SmtpCommandException exception) =>
        (int)exception.StatusCode is >= 400 and < 500 ? DeliveryFailure.ServerError : DeliveryFailure.Rejected;

    private MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(settings.From);
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        return mime;
    }
}
