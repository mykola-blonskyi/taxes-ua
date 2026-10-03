using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Tests.Features.Notifications;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class MonobankClientLoggingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string AccountId = "acct-private-1a2b3c";

    [Fact]
    public async Task The_statement_url_with_the_bank_account_id_never_reaches_a_log()
    {
        var logs = new CapturedLogs();
        var monobank = new StubMonobankHandler(_ => StubMonobankHandler.Json("[]"));
        await using var application = fixture.CreateApplication(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddFilter((_, _, _) => true);
                logging.AddProvider(logs);
            });
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => monobank));
        });
        await using var scope = application.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<MonobankClient>();

        var result = await client.GetStatementAsync(
            "token", AccountId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), CancellationToken.None);

        Assert.IsType<StatementResult.Found>(result);
        Assert.Contains(AccountId, Assert.Single(monobank.Requests).RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(AccountId, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.StartsWith("System.Net.Http.HttpClient.MonobankClient", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_framework_would_log_the_account_id_if_the_client_kept_its_request_logging()
    {
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddFilter((_, _, _) => true);
            logging.AddProvider(logs);
        });
        services.AddHttpClient("plain", client => client.BaseAddress = new Uri("https://monobank.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubMonobankHandler(_ => StubMonobankHandler.Json("[]")));
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IHttpClientFactory>().CreateClient("plain")
            .GetAsync($"personal/statement/{AccountId}/0/1");

        Assert.Contains(logs.Lines, line => line.Contains(AccountId, StringComparison.Ordinal));
    }
}
