using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Settings;

// Every write goes to the first owner and the second is only read, so no test order can leave the
// second owner with stored details.
public sealed class DeclarationDetailsEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Url = "/api/settings/declaration";

    [Fact]
    public async Task An_owner_who_never_saved_is_missing_every_detail()
    {
        using var owner = await SignIn(ApiFixture.SecondAllowedEmail);

        var details = await owner.GetFromJsonAsync<DeclarationDetailsResponse>(Url, Json);

        Assert.Equal(
            ("", "", (int?)null, (int?)null, "", 0, ""),
            (details!.Name, details.Rnokpp, details.TaxOfficeRegion, details.TaxOfficeDistrict, details.TaxOfficeName,
                details.KvedCodes.Length, details.Address));
        Assert.Equal(
            [
                DeclarationDetailField.Name,
                DeclarationDetailField.Rnokpp,
                DeclarationDetailField.TaxOffice,
                DeclarationDetailField.Kved,
                DeclarationDetailField.Address,
            ],
            details.MissingDetails);
    }

    [Fact]
    public async Task Details_save_and_reload_with_the_name_and_rnokpp_read_from_the_invoicing_details()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/invoicing", Invoicing(), Json)).StatusCode);

        var put = await owner.PutAsJsonAsync(
            Url,
            new DeclarationDetailsRequest(26, 5, " ГУ ДПС у м. Києві  ", [" 62.01 ", "63.11"], "  Київ, вул. Тестова 1 "),
            Json);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var details = await owner.GetFromJsonAsync<DeclarationDetailsResponse>(Url, Json);
        Assert.Equal(
            ("ФОП Тест", "1234567890", (int?)26, (int?)5, "ГУ ДПС у м. Києві", "62.01 63.11", "Київ, вул. Тестова 1"),
            (details!.Name, details.Rnokpp, details.TaxOfficeRegion, details.TaxOfficeDistrict, details.TaxOfficeName,
                string.Join(' ', details.KvedCodes), details.Address));
        Assert.Empty(details.MissingDetails);
    }

    [Fact]
    public async Task An_incomplete_set_saves_and_names_what_is_missing()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/invoicing", Invoicing(), Json)).StatusCode);

        var put = await owner.PutAsJsonAsync(Url, new DeclarationDetailsRequest(null, null, "", [], "Київ"), Json);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var details = (await put.Content.ReadFromJsonAsync<DeclarationDetailsResponse>(Json))!;
        Assert.Equal([DeclarationDetailField.TaxOffice, DeclarationDetailField.Kved], details.MissingDetails);
    }

    [Fact]
    public async Task Codes_without_the_tax_offices_name_leave_the_tax_office_missing()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings/invoicing", Invoicing(), Json)).StatusCode);

        var put = await owner.PutAsJsonAsync(Url, new DeclarationDetailsRequest(26, 5, "   ", ["62.01"], "Київ"), Json);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var details = (await put.Content.ReadFromJsonAsync<DeclarationDetailsResponse>(Json))!;
        Assert.Equal("", details.TaxOfficeName);
        Assert.Equal([DeclarationDetailField.TaxOffice], details.MissingDetails);
    }

    public static TheoryData<string, string> InvalidRequests() => new()
    {
        { "region 0", "taxOfficeRegion" },
        { "region 100", "taxOfficeRegion" },
        { "district -1", "taxOfficeDistrict" },
        { "district 100", "taxOfficeDistrict" },
        { "region without a district", "taxOfficeDistrict" },
        { "district without a region", "taxOfficeRegion" },
        { "KVED without a dot", "kvedCodes[0]" },
        { "KVED with one digit after the dot", "kvedCodes[0]" },
        { "KVED in Arabic-Indic digits", "kvedCodes[0]" },
        { "blank KVED", "kvedCodes[0]" },
        { "repeated KVED", "kvedCodes[1]" },
        { "21 KVED codes", "kvedCodes" },
        { "address over 500 characters", "address" },
        { "NUL in the address", "address" },
        { "tax office name over 200 characters", "taxOfficeName" },
        { "NUL in the tax office name", "taxOfficeName" },
    };

    private static DeclarationDetailsRequest Invalid(string name)
    {
        var valid = new DeclarationDetailsRequest(26, 5, "ГУ ДПС у м. Києві", ["62.01"], "Київ");
        return name switch
        {
            "region 0" => valid with { TaxOfficeRegion = 0 },
            "region 100" => valid with { TaxOfficeRegion = 100 },
            "district -1" => valid with { TaxOfficeDistrict = -1 },
            "district 100" => valid with { TaxOfficeDistrict = 100 },
            "region without a district" => valid with { TaxOfficeDistrict = null },
            "district without a region" => valid with { TaxOfficeRegion = null },
            "KVED without a dot" => valid with { KvedCodes = ["6201"] },
            "KVED with one digit after the dot" => valid with { KvedCodes = ["62.1"] },
            "KVED in Arabic-Indic digits" => valid with { KvedCodes = ["٦٢.٠١"] },
            "blank KVED" => valid with { KvedCodes = [" "] },
            "repeated KVED" => valid with { KvedCodes = ["62.01", " 62.01"] },
            "21 KVED codes" => valid with { KvedCodes = [.. Enumerable.Range(10, 21).Select(n => $"{n}.01")] },
            "address over 500 characters" => valid with { Address = new string('а', 501) },
            "NUL in the address" => valid with { Address = "Київ\u0000" },
            "tax office name over 200 characters" => valid with { TaxOfficeName = new string('а', 201) },
            "NUL in the tax office name" => valid with { TaxOfficeName = "ГУ ДПС\u0000" },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task An_invalid_value_is_rejected_with_its_field_and_stores_nothing(string name, string field)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var before = await owner.GetStringAsync(Url);

        var response = await owner.PutAsJsonAsync(Url, Invalid(name), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"no error under {field}: {errors}");
        Assert.Equal(before, await owner.GetStringAsync(Url));
    }

    [Fact]
    public async Task A_null_kved_code_is_rejected_rather_than_failing()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var response = await owner.PutAsync(
            Url,
            new StringContent(
                """{"taxOfficeRegion":26,"taxOfficeDistrict":5,"taxOfficeName":"ГУ ДПС","kvedCodes":[null],"address":"Київ"}""",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_owner_sees_none_of_the_details_and_a_visitor_sees_nothing()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        using var visitor = fixture.CreateClient();
        await owner.PutAsJsonAsync(Url, new DeclarationDetailsRequest(26, 5, "ГУ ДПС у м. Києві", ["62.01"], "Київ"), Json);

        var seen = await other.GetFromJsonAsync<DeclarationDetailsResponse>(Url, Json);

        Assert.Equal(((int?)null, 0, ""), (seen!.TaxOfficeRegion, seen.KvedCodes.Length, seen.Address));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(Url)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await visitor.PutAsJsonAsync(Url, new DeclarationDetailsRequest(null, null, "", [], ""), Json)).StatusCode);
    }

    [Fact]
    public async Task A_change_is_logged_and_saving_the_same_details_again_is_not()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var request = new DeclarationDetailsRequest(14, 3, "ГУ ДПС у Львівській області", ["62.02"], "Львів");
        await owner.PutAsJsonAsync(Url, request, Json);
        var before = await Log(owner);

        await owner.PutAsJsonAsync(Url, request, Json);
        await owner.PutAsJsonAsync(Url, request with { KvedCodes = ["62.02", "62.09"] }, Json);

        var added = (await Log(owner)).ExceptBy(before.Select(entry => entry.Id), entry => entry.Id).ToArray();
        var entry = Assert.Single(added);
        Assert.Equal(AuditAction.Update, entry.Action);
        Assert.Equal(2, entry.After!["kvedCodes"].GetArrayLength());
        Assert.Equal("Львів", entry.After["address"].GetString());
    }

    private async Task<HttpClient> SignIn(string email)
    {
        var client = fixture.CreateClient();
        var login = await client.GetAsync($"/api/auth/login/development?email={email}");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = await client.GetAsync(login.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return client;
    }

    private static async Task<AuditEntryResponse[]> Log(HttpClient owner) =>
        (await owner.GetFromJsonAsync<AuditEntryResponse[]>("/api/audit?entity=DeclarationDetails", Json))!;

    private static InvoicingDetailsRequest Invoicing() => new(
        "ФОП Тест",
        "FOP Test",
        "1234567890",
        "Київ",
        "Kyiv",
        InvoicingDefaults.AcceptanceEn,
        InvoicingDefaults.AcceptanceUk,
        InvoicingDefaults.FeesEn,
        InvoicingDefaults.FeesUk,
        InvoicingDefaults.TaxStatusEn,
        InvoicingDefaults.TaxStatusUk,
        []);
}
