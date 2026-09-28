using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class StubMonobankHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public IReadOnlyCollection<HttpRequestMessage> Requests => _requests;

    // Answers client-info for the one recognised token and 401 for every other one, the way a real
    // token check behaves for a typo.
    public static StubMonobankHandler ForToken(string validToken, string body) =>
        new(request =>
        {
            var token = request.Headers.TryGetValues("X-Token", out var values) ? values.FirstOrDefault() : null;
            return token == validToken
                ? Json(body)
                : new HttpResponseMessage(HttpStatusCode.Forbidden);
        });

    public static string ClientInfo(string clientId, params (string Id, string Type, int CurrencyCode, string Iban)[] accounts)
    {
        var accountsJson = string.Join(",", accounts.Select(account =>
            $$"""{"id":"{{account.Id}}","sendId":"x","balance":0,"creditLimit":0,"type":"{{account.Type}}","currencyCode":{{account.CurrencyCode}},"cashbackType":"None","maskedPan":[],"iban":"{{account.Iban}}"}"""));
        return $$"""{"clientId":"{{clientId}}","name":"Test FOP","webHookUrl":"","permissions":"psfj","accounts":[{{accountsJson}}],"jars":[]}""";
    }

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(respond(request));
    }
}
