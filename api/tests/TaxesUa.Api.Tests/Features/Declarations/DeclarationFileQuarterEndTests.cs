using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Data;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Features.Declarations;
using static TaxesUa.Api.Tests.Features.Declarations.DeclarationsEndpointsTests;

namespace TaxesUa.Api.Tests.Features.Declarations;

// Rule 15: a declaration file is built only once the quarter's last day has passed in Kyiv. The clock sits
// in the 2090s, a year of its own, so the test client keeps its session cookie.
public sealed class DeclarationFileQuarterEndTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task The_quarter_still_running_gets_409_and_the_last_ended_quarter_still_works()
    {
        const int year = 2090;
        var clock = new FakeTimeProvider(new DateTimeOffset(year, 8, 15, 9, 0, 0, TimeSpan.Zero));
        await using var application = At(clock);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);

        var running = await Post(owner, year, 3);
        var ended = await Post(owner, year, 2);

        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode);
        using var problem = JsonDocument.Parse(await running.Content.ReadAsStringAsync());
        Assert.Equal("QuarterNotEnded", problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal("2090-10-01", problem.RootElement.GetProperty("availableFrom").GetString());
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);

        var preview = await Get(owner, year, 3);
        Assert.False(preview.FileAvailable);
        Assert.Equal(new DateOnly(year, 10, 1), preview.FileAvailableFrom);
        Assert.NotNull(preview.Figures);
        Assert.True((await Get(owner, year, 2)).FileAvailable);
    }

    [Fact]
    public async Task The_quarter_opens_on_the_day_after_its_last_day_by_the_date_in_Kyiv()
    {
        const int year = 2091;
        // 22:30 UTC on 30 September is 01:30 on 1 October in Kyiv (UTC+3 in summer time).
        var clock = new FakeTimeProvider(new DateTimeOffset(year, 9, 30, 12, 0, 0, TimeSpan.Zero));
        await using var application = At(clock);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);

        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, year, 3)).StatusCode);

        clock.SetUtcNow(new DateTimeOffset(year, 9, 30, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(HttpStatusCode.OK, (await Post(owner, year, 3)).StatusCode);
    }

    [Fact]
    public async Task A_file_stored_for_a_quarter_that_has_not_ended_is_kept_but_neither_listed_nor_served()
    {
        const int year = 2092;
        await using (var ended = At(new FakeTimeProvider(new DateTimeOffset(year, 10, 5, 9, 0, 0, TimeSpan.Zero))))
        {
            using var owner = await ApiFixture.SignIn(ended, ApiFixture.AllowedEmail);
            await SetUp(owner, year);
            Assert.Equal(HttpStatusCode.OK, (await Post(owner, year, 3)).StatusCode);
        }

        // The same database seen by a clock that is earlier, as when a file was stored before this rule.
        await using var running = At(new FakeTimeProvider(new DateTimeOffset(year, 9, 10, 9, 0, 0, TimeSpan.Zero)));
        using var client = await ApiFixture.SignIn(running, ApiFixture.AllowedEmail);

        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/declarations/{year}/3/files/Reporting")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/declarations/{year}/3/files/Reporting/annex")).StatusCode);
        Assert.Empty((await Get(client, year, 3)).Files);
        await using var scope = running.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeclarationFiles
            .CountAsync(row => row.Year == year && row.Quarter == 3);
        Assert.Equal(1, stored);
    }

    private WebApplicationFactory<Program> At(FakeTimeProvider clock) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(clock)));

    private static Task<HttpResponseMessage> Post(HttpClient owner, int year, int quarter) =>
        owner.PostAsJsonAsync($"/api/declarations/{year}/{quarter}/files", new DeclarationFileRequest(DeclarationType.Reporting), Json);

    private static async Task<DeclarationResponse> Get(HttpClient owner, int year, int quarter)
    {
        var response = await owner.GetAsync($"/api/declarations/{year}/{quarter}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationResponse>(Json))!;
    }
}
