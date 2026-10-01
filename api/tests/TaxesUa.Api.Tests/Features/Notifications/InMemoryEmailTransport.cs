using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Notifications;

// The SMTP sender, in memory: every attempt is recorded with the fake clock's time, and Answer decides
// what the "server" says to attempt number n (counted from 1 across the transport's life).
internal sealed class InMemoryEmailTransport : IEmailTransport
{
    private readonly object _gate = new();
    private readonly List<(EmailMessage Message, DateTimeOffset At, bool Accepted)> _attempts = [];

    public FakeTimeProvider? Clock { get; set; }

    public Func<EmailMessage, int, DeliveryAttempt>? Answer { get; set; }

    public IReadOnlyList<(EmailMessage Message, DateTimeOffset At, bool Accepted)> Attempts
    {
        get
        {
            lock (_gate)
            {
                return [.. _attempts];
            }
        }
    }

    public IReadOnlyList<EmailMessage> Delivered => [.. Attempts.Where(attempt => attempt.Accepted).Select(attempt => attempt.Message)];

    public void Clear()
    {
        lock (_gate)
        {
            _attempts.Clear();
        }
    }

    public Task<DeliveryAttempt> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = Answer?.Invoke(message, _attempts.Count + 1) ?? new DeliveryAttempt();
            _attempts.Add((message, Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow, result.IsOk));

            return Task.FromResult(result);
        }
    }
}
