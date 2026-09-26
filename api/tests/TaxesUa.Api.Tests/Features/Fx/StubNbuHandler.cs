using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace TaxesUa.Api.Tests.Features.Fx;

public sealed class StubNbuHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public IReadOnlyCollection<Uri> Requests => _requests;

    // Answers each requested yyyyMMdd date from the map, and `[]` for any date it does not list.
    public static StubNbuHandler ByDate(IReadOnlyDictionary<string, string> bodies) =>
        new(request =>
        {
            var date = request.RequestUri!.Query.Split('&').Single(part => part.StartsWith("date=")).Substring(5);
            return Json(bodies.GetValueOrDefault(date, "[]"));
        });

    public static string Row(string currency, DateOnly date, string rate) =>
        $$"""[{"r030":840,"txt":"x","rate":{{rate}},"cc":"{{currency}}","exchangedate":"{{date:dd.MM.yyyy}}","special":"N"}]""";

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

public sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
