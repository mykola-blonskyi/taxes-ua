using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
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
            DeclarationType.Reporting, "26051234567890F0103309100000000120320812605.xml", null, generatedAt);
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
        Assert.DoesNotContain("LINKED_DOCS", text);
        Assert.Equal([expected], (await Get(owner, year, 1)).Files);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/declarations/{year}/1/files/Reporting/annex")).StatusCode);
    }

    [Fact]
    public async Task The_annual_declaration_stores_its_esv_annex_beside_it_and_both_download()
    {
        const int year = 2089;
        var today = new DateOnly(year + 1, 2, 2);
        await using var application = At(today);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 11, 5), 10_000_000);

        var created = await Generate(owner, year, 4, DeclarationType.Reporting);
        var declaration = await owner.GetAsync($"/api/declarations/{year}/4/files/Reporting");
        var annex = await owner.GetAsync($"/api/declarations/{year}/4/files/Reporting/annex");

        var expected = new DeclarationFileResponse(
            DeclarationType.Reporting,
            "26051234567890F0103309100000000151220892605.xml",
            "26051234567890F0133109100000000151220892605.xml",
            new DateTimeOffset(today, new TimeOnly(9, 0), TimeSpan.Zero));
        Assert.Equal(expected, created);
        Assert.Equal([expected], (await Get(owner, year, 4)).Files);
        Assert.Equal(HttpStatusCode.OK, annex.StatusCode);
        Assert.Equal("application/xml", annex.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected.AnnexFileName, annex.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("no-store", annex.Headers.CacheControl?.ToString());
        var declarationBytes = await declaration.Content.ReadAsByteArrayAsync();
        var annexBytes = await annex.Content.ReadAsByteArrayAsync();
        Assert.Empty(F0103309.SchemaErrors(declarationBytes));
        Assert.Empty(F0133109.SchemaErrors(annexBytes));
        var declarationText = Windows1251.GetString(declarationBytes);
        var annexText = Windows1251.GetString(annexBytes);
        Assert.Contains($"<FILENAME>{expected.AnnexFileName}</FILENAME>", declarationText);
        Assert.Contains($"<FILENAME>{expected.FileName}</FILENAME>", annexText);
        Assert.Contains("<R021G3>22828.08</R021G3>", declarationText);
        Assert.Contains("<HD1>1</HD1>", declarationText);
        Assert.Contains("<R08G1D>01012089</R08G1D><R08G2D>31122089</R08G2D>", annexText);
        Assert.Contains("<R09G2>103764.00</R09G2><R09G4>22828.08</R09G4>", annexText);
        Assert.Equal(2_282_808L, (await Get(owner, year, 4)).Figures!.EsvKop);
    }

    [Fact]
    public async Task The_cabinet_view_holds_the_values_and_the_order_of_the_xml_of_the_same_quarter_annex_included()
    {
        const int year = 2080;
        await using var application = At(new DateOnly(year + 1, 2, 2));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PostIncome(owner, new DateOnly(year, 1, 20), 12_345_678);
        await PostIncome(owner, new DateOnly(year, 11, 5), 10_000_000);

        foreach (var quarter in new[] { 1, 2, 4 })
        {
            await Generate(owner, year, quarter, DeclarationType.Reporting);
            var cabinet = (await Get(owner, year, quarter)).Cabinet;
            var declaration = Windows1251.GetString(await owner.GetByteArrayAsync($"/api/declarations/{year}/{quarter}/files/Reporting"));
            var annex = quarter == 4
                ? Windows1251.GetString(await owner.GetByteArrayAsync($"/api/declarations/{year}/{quarter}/files/Reporting/annex"))
                : null;

            AssertCabinetMatchesXml(cabinet, declaration, annex);
            Assert.Equal(annex is not null, cabinet.Any(field => field.Part == CabinetPart.Annex));
            Assert.DoesNotContain(cabinet, field => field.Part == CabinetPart.None);
        }

        var annual = (await Get(owner, year, 4)).Cabinet;
        Assert.Equal("22828.08", annual.Single(field => field.Element == "R021G3").Value);
        Assert.Equal("22828.08", annual.Single(field => field.Element == "R09G4").Value);
        Assert.Equal("01.01.2080", annual.Single(field => field.Element == "R08G1D").Value);
        Assert.Equal("31.12.2080", annual.Single(field => field.Element == "R08G2D").Value);
        Assert.Equal("22.00", annual.Single(field => field.Element == "R0912G3").Value);
        Assert.Equal("1902.34", annual.Single(field => field.Element == "R0912G4").Value);
        var first = (await Get(owner, year, 1)).Cabinet;
        Assert.Equal("123456.78", first.Single(field => field.Element == "R006G3").Value);
        Assert.Equal("0.00", first.Single(field => field.Element == "R013G3").Value);
        Assert.Null(first.Single(field => field.Element == "R007G3").Value);
        Assert.Null(first.Single(field => field.Element == "R021G3").Value);
    }

    [Fact]
    public async Task The_cabinet_view_of_a_crossing_quarter_fills_line_07_and_ticks_the_move_to_other_taxes()
    {
        const int year = 2078;
        await using var application = At(new DateOnly(year, 10, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, incomeLimitMinWages: 1);
        await PostIncome(owner, new DateOnly(year, 2, 10), 500_000);
        await PostIncome(owner, new DateOnly(year, 5, 10), 500_000);
        await Generate(owner, year, 2, DeclarationType.Reporting);

        var cabinet = (await Get(owner, year, 2)).Cabinet;

        var declaration = Windows1251.GetString(await owner.GetByteArrayAsync($"/api/declarations/{year}/2/files/Reporting"));
        var annex = Windows1251.GetString(await owner.GetByteArrayAsync($"/api/declarations/{year}/2/files/Reporting/annex"));
        AssertCabinetMatchesXml(cabinet, declaration, annex);
        Assert.NotNull(cabinet.Single(field => field.Element == "R007G3").Value);
        Assert.NotNull(cabinet.Single(field => field.Element == "R009G3").Value);
        Assert.Equal(CabinetKind.Mark, cabinet.Single(field => field.Element == "H03").Kind);
    }

    [Fact]
    public async Task The_cabinet_view_normalises_a_text_the_way_the_xml_does()
    {
        const int year = 2077;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        // The details endpoint refuses a line break, so a row that holds one came in some other way.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeclarationDetails
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Address, "вул. З\u02BCїзду,\n1"));
        }

        await Generate(owner, year, 1, DeclarationType.Reporting);
        var cabinet = (await Get(owner, year, 1)).Cabinet;
        var declaration = Windows1251.GetString(await owner.GetByteArrayAsync($"/api/declarations/{year}/1/files/Reporting"));

        Assert.Equal("вул. З'їзду, 1", cabinet.Single(field => field.Element == "HLOC").Value);
        AssertCabinetMatchesXml(cabinet, declaration, null);
    }

    [Fact]
    public async Task The_cabinet_view_leaves_the_header_out_until_the_details_are_complete()
    {
        const int year = 2079;
        await using var application = At(new DateOnly(year, 4, 20));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year);
        await PutDetails(owner, CompleteDetails with { Address = "" });

        var declaration = await Get(owner, year, 1);

        Assert.DoesNotContain(declaration.Cabinet, field => field.Part == CabinetPart.Header);
        Assert.Contains(declaration.Cabinet, field => field.Element == "R006G3");
        Assert.Contains(declaration.Cabinet, field => field.Element == "HZY");
    }

    // The view is built from the list the writers read, so this pins that nobody reintroduces a second
    // source: every field is in the file with the same text and in the same order, or in neither.
    private static void AssertCabinetMatchesXml(CabinetFieldResponse[] cabinet, string declarationXml, string? annexXml)
    {
        var declaration = XDocument.Parse(declarationXml).Root!.Element("DECLARBODY")!;
        var annex = annexXml is null ? null : XDocument.Parse(annexXml).Root!.Element("DECLARBODY")!;
        var seen = new List<string>();
        foreach (var field in cabinet)
        {
            var body = field.Element is "HD1" || field.Part != CabinetPart.Annex ? declaration : annex!;
            var elements = body.Elements(field.Element).ToArray();
            var element = field.Row > 0 ? elements.SingleOrDefault(e => (string?)e.Attribute("ROWNUM") == field.Row.ToString()) : elements.SingleOrDefault();
            if (field.Kind == CabinetKind.Mark)
            {
                Assert.Equal("1", element?.Value);
            }
            else if (field.Value is null)
            {
                Assert.Null(element);
            }
            else if (field.Kind == CabinetKind.Date)
            {
                Assert.Equal(element?.Value, field.Value.Replace(".", string.Empty, StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(element?.Value, field.Value);
            }

            if (element is not null && body == declaration)
            {
                seen.Add(field.Row > 0 ? $"{field.Element}#{field.Row}" : field.Element);
            }
        }

        var inFile = declaration.Elements()
            .Select(e => e.Attribute("ROWNUM") is { } row ? $"{e.Name.LocalName}#{row.Value}" : e.Name.LocalName)
            .Where(seen.Contains);
        Assert.Equal(seen, inFile);

        // Nothing the file carries as a line is missing from the view.
        var lines = declaration.Elements().Select(e => e.Name.LocalName).Where(name => name.StartsWith('R') && name.EndsWith("G3", StringComparison.Ordinal));
        Assert.All(lines, name => Assert.Contains(cabinet, field => field.Element == name));
        if (annex is not null)
        {
            var cells = annex.Elements().Select(e => e.Name.LocalName).Where(name => name.StartsWith("R0", StringComparison.Ordinal));
            Assert.All(cells, name => Assert.Contains(cabinet, field => field.Element == name));
        }
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

        var crossing = await Generate(owner, year, 2, DeclarationType.Reporting);
        var after = await Post(owner, year, 3, DeclarationType.Reporting);
        var annex = await owner.GetAsync($"/api/declarations/{year}/2/files/Reporting/annex");

        Assert.Equal("26051234567890F0133109100000000130620832605.xml", crossing.AnnexFileName);
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
        var annexText = Windows1251.GetString(await annex.Content.ReadAsByteArrayAsync());
        Assert.Contains("<HHY>1</HHY><HZY>2083</HZY><H03>1</H03>", annexText);
        Assert.Contains("<R08G2D>30062083</R08G2D>", annexText);
        Assert.Contains("<R09G4>11414.04</R09G4>", annexText);
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
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        ProblemAssert.Rejects(problem.RootElement, "file");
        Assert.All(ProblemAssert.CodesOf(problem.RootElement, "file"), code => Assert.Equal("declaration_schema_invalid", code));
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
        ProblemAssert.Rejects(problem.RootElement, "file");
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
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/declarations/{year}/1/files/Reporting/annex")).StatusCode);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/declarations/2088/4/files/Reporting/annex")).StatusCode);
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
