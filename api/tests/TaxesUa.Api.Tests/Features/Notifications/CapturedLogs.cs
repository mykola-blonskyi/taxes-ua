using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TaxesUa.Api.Tests.Features.Notifications;

// Every line the host logs, with its category, message and any exception in full, so a test can prove
// something never appears anywhere in the output.
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
    }
}
