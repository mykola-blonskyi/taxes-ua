using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Tests.Features.Fx;
using static TaxesUa.Api.Tests.Features.Declarations.DeclarationsEndpointsTests;

namespace TaxesUa.Api.Tests.Features.Declarations;

// Same conventions as DeclarationsEndpointsTests: this class owns its database, each test a year of its
// own, and the clock sits in the 2080s so the test client keeps its session cookie.
public sealed class DeclarationFilesEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Encoding Windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    [Fact]
    public async Task A_ready_quarter_downloads_the_file_it_stored_and_lists_it_with_its_time()
    {
        const int year = 2081;
        var today = new DateOnly(year, 4, 20);
        await using var application = At(today);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 1, 20), 12_345_678);

        var created = await Generate(owner, year, 1, DeclarationType.Reporting);
        var download = await owner.GetAsync($"/api/declarations/{year}/1/files/Reporting");

        var generatedAt = new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero);
        var expected = new DeclarationFileResponse(
            DeclarationType.Reporting, "26051234567890F0103309100000000120320812605.xml", generatedAt);
        Assert.Equal(expected, created);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/xml", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(expected.FileName, download.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("no-store", download.Headers.CacheControl?.ToString());
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(await StoredContent(application, year, 1, DeclarationType.Reporting), bytes);
        Assert.Empty(F0103309.SchemaErrors(bytes));
        var text = Windows1251.GetString(bytes);
        Assert.Contains("<R006G3>123456.78</R006G3>", text);
        Assert.Contains("<HNAME>Тест</HNAME>", text);
        Assert.Contains("<HSTI>ГУ ДПС у м. Києві</HSTI><HNAME>", text);
        Assert.Contains("<D_FILL>20042081</D_FILL>", text);
        Assert.Equal([expected], (await Get(owner, year, 1)).Files);
    }

    [Fact]
    public async Task Without_the_tax_offices_name_the_quarter_is_not_ready_and_gets_no_file()
    {
        const int year = 2082;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutDetails(owner, CompleteDetails with { TaxOfficeName = "" });

        var response = await Post(owner, year, 1, DeclarationType.Reporting);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await Get(owner, year, 1)).Files);
    }

    [Fact]
    public async Task A_quarter_outside_group_3_gets_no_file()
    {
        const int year = 2083;
        await using var application = At(new DateOnly(year, 10, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, incomeLimitMinWages: 1);
        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);

        var crossing = await Post(owner, year, 2, DeclarationType.Reporting);
        var after = await Post(owner, year, 3, DeclarationType.Reporting);

        Assert.Equal(HttpStatusCode.OK, crossing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
    }

    [Fact]
    public async Task A_tax_office_code_the_schema_refuses_answers_with_the_errors_and_stores_nothing()
    {
        const int year = 2084;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutDetails(owner, CompleteDetails with { TaxOfficeRegion = 29, TaxOfficeDistrict = 0 });

        var response = await Post(owner, year, 1, DeclarationType.Reporting);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors").GetProperty("file");
        Assert.Contains(errors.EnumerateArray(), error => error.GetString()!.Contains("C_STI_ORIG", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/declarations/{year}/1/files/Reporting")).StatusCode);
        Assert.Empty((await Get(owner, year, 1)).Files);
    }

    [Fact]
    public async Task A_control_character_in_a_text_answers_with_the_field_and_stores_nothing()
    {
        const int year = 2083;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutDetails(owner, CompleteDetails);
        // The details endpoint refuses a control character, so a row that holds one came in some other way.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeclarationDetails
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Address, "вул. Тестова\u0001 1"));
        }

        var response = await Post(owner, year, 1, DeclarationType.Reporting);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors").GetProperty("file");
        Assert.Contains(errors.EnumerateArray(), error => error.GetString()!.StartsWith("address ", StringComparison.Ordinal));
        Assert.Empty((await Get(owner, year, 1)).Files);
    }

    [Fact]
    public async Task Preparing_the_file_again_replaces_it_and_each_type_keeps_its_own()
    {
        const int year = 2085;
        await using (var first = At(new DateOnly(year, 4, 20)))
        {
            using var owner = await ApiFixture.SignIn(first, ApiFixture.AllowedEmail);
            await SetUp(owner, year);
            await PostIncome(owner, new DateOnly(year, 1, 20), 1_000_000);
            await Generate(owner, year, 1, DeclarationType.Reporting);
        }

        await using var later = At(new DateOnly(year, 5, 2));
        using var client = await ApiFixture.SignIn(later, ApiFixture.AllowedEmail);

        var replaced = await Generate(client, year, 1, DeclarationType.Reporting);
        var clarifying = await Generate(client, year, 1, DeclarationType.Clarifying);

        var laterTime = new DateTimeOffset(new DateOnly(year, 5, 2), new TimeOnly(9, 0), TimeSpan.Zero);
        Assert.Equal([replaced, clarifying], (await Get(client, year, 1)).Files);
        Assert.Equal((laterTime, laterTime), (replaced.GeneratedAt, clarifying.GeneratedAt));
        Assert.EndsWith("F0103309300000000120320852605.xml", clarifying.FileName);
        var stored = Windows1251.GetString(await StoredContent(later, year, 1, DeclarationType.Reporting));
        Assert.Contains("<D_FILL>02052085</D_FILL>", stored);
    }

    [Fact]
    public async Task Another_owner_sees_no_file_and_cannot_download_one()
    {
        const int year = 2086;
        await using var application = At(new DateOnly(year, 4, 20));
        using (var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail))
        {
            await SetUp(owner, year);
            await Generate(owner, year, 1, DeclarationType.Reporting);
        }

        using var other = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/declarations/{year}/1/files/Reporting")).StatusCode);
        Assert.Empty((await Get(other, year, 1)).Files);
    }

    [Fact]
    public async Task A_number_that_is_no_declaration_type_is_not_found()
    {
        await using var application = At(new DateOnly(2087, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/declarations/2087/1/files/7")).StatusCode);
    }

    [Fact]
    public async Task Without_a_session_the_routes_are_unauthorized()
    {
        using var visitor = fixture.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await visitor.PostAsJsonAsync("/api/declarations/2088/1/files", new DeclarationFileRequest(DeclarationType.Reporting), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/declarations/2088/1/files/Reporting")).StatusCode);
    }

    private WebApplicationFactory<Program> At(DateOnly today) =>
        fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(
                new FakeTime(new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero)))));

    private static Task<HttpResponseMessage> Post(HttpClient owner, int year, int quarter, DeclarationType type) =>
        owner.PostAsJsonAsync($"/api/declarations/{year}/{quarter}/files", new DeclarationFileRequest(type), Json);

    private static async Task<DeclarationFileResponse> Generate(HttpClient owner, int year, int quarter, DeclarationType type)
    {
        var response = await Post(owner, year, quarter, type);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationFileResponse>(Json))!;
    }

    private static async Task<DeclarationResponse> Get(HttpClient owner, int year, int quarter)
    {
        var response = await owner.GetAsync($"/api/declarations/{year}/{quarter}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DeclarationResponse>(Json))!;
    }

    private static async Task<byte[]> StoredContent(
        WebApplicationFactory<Program> application, int year, int quarter, DeclarationType type)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.DeclarationFiles
            .Where(row => row.Year == year && row.Quarter == quarter && row.Type == type)
            .Select(row => row.Content)
            .SingleAsync();
    }
}
