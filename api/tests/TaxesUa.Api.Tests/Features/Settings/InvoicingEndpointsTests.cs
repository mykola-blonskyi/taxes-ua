using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Audit;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Tests.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Settings;

public static class InvoicingTestData
{
    public const string ValidIban = "UA573220010000026007233566001";

    public const string OtherValidIban = "UA523052990000026003123456789";

    public static readonly byte[] Png =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    // A 240 by 60 RGBA stroke PDFsharp embeds; the 1 by 1 Png above is one its importer refuses.
    public static readonly byte[] SignaturePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAPAAAAA8CAYAAABYfzddAAACuUlEQVR4nO3YXU5rMQxFYQbBIBgiswfl4YijqrTOj+NtZ30Sj+2xF0nLvR8fAAAAAAAAAAAAAAAAgJfPr++fVz/R86mhlR1ny9m7wIT+Y21FL1q56wlM7LFe0TNH4Ww5Gw18amha2XG2nM0GPi00rew4WxuMBDs19MjetJo/WzvmTWkm1mmhZ/c9qRetNlgV6ITQqw7UKQeTs+VsdZjqoVfuR6vY90vPK0jV0B570Wr8fVe9Z1qeh6da6F2tKvbyet8qrYZ4h6gW2nsXWo29f4Ve3XYFqBJ61w7VWu06W17PkLVz+eyhd34IVfjA42w5i1g6c+jds9Nq7HkZew3hAttFzZ2xV9RlythqWOSy2UJHfrpna9VwtpwpLKkwg1X0rNHP7xE9a/Tzt1BYUmEGC5U5VeZ4R2HOJTOohlYIrDjLM0r/MaI0yzNq803PorLInXJkhXnuFGdTm+dObbbp35/SMo3igWwyzBU9yyVDqzJzqS2kNs+d2myqB7JRnE1tnsuyCxy9mNIsz6jNpzTLM0rzqf3uHpX4FlaZ4xWVg6Awg4XCnAozWCz5t3DUktHP7xE9a/Tzeyh84EU/v0fKb+FMgS/0sqNVn3SX+KjISZ87K2JuhW//Ean+lM4Y+BLZKluviNmztmpSfAtnPpCXqF7ez/JAK7upu7Fj+QqXt9m1R4VWDWfLbskF9gpQIfBlZ6vsvThbfSQvcaUD2dCqz65eK983yrI/pVcFqXggG1r18diraq/hvR5fOBulauCLZ6tqvVbvVrlVE36Jqwe+eLSq2mvVjie22nqJTzmQDa360Mtu+SV+9wYjr6lgdO8TWzWrWp3Qa2rf/6L1/DjtJYdWditandpr6sUEfo1WdlzePlN7E9iOVnZc3j5TuxPYjlZ96LURce04jHa0AgAAAAAAAAAAAACc6Bc9E/d6kt/EkgAAAABJRU5ErkJggg==");

    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];
}

// Every write goes to the first owner and the second is only read, so no test order can leave the
// second owner with stored details.
public sealed class InvoicingEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Url = "/api/settings/invoicing";

    [Fact]
    public async Task An_owner_who_never_saved_gets_empty_details_and_the_default_clauses()
    {
        using var owner = await SignIn(ApiFixture.SecondAllowedEmail);

        var details = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);

        Assert.NotNull(details);
        Assert.Equal(string.Empty, details.SellerNameUk);
        Assert.Equal(string.Empty, details.Rnokpp);
        Assert.Empty(details.PaymentDetails);
        Assert.False(details.HasSignature);
        Assert.Equal(
            "Payment of this invoice constitutes acceptance of the services, which are deemed rendered in full and without claims.",
            details.AcceptanceClauseEn);
        Assert.Equal("The seller is a single tax payer and is not registered for VAT.", details.TaxStatusClauseEn);
        Assert.False(string.IsNullOrWhiteSpace(details.AcceptanceClauseUk));
        Assert.False(string.IsNullOrWhiteSpace(details.FeesClauseUk));
        Assert.False(string.IsNullOrWhiteSpace(details.TaxStatusClauseUk));
        Assert.Equal(details.AcceptanceClauseUk, details.Defaults.AcceptanceClauseUk);
    }

    [Fact]
    public async Task Details_save_and_reload_and_a_second_save_replaces_the_payment_details()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var request = Valid() with
        {
            SellerNameUk = "  ФОП Тест Тестович ",
            Rnokpp = "1234567890",
            FeesClauseEn = "Fees are paid by the payer.",
            PaymentDetails =
            [
                Payment(Currency.EUR),
                Payment(Currency.USD) with
                {
                    Iban = "ua57 3220 0100 0002 6007 2335 66001",
                    Swift = "unjsuaukxxx",
                    IntermediaryBank = "Intermediary Bank",
                    IntermediarySwift = "IRVTUS3N",
                    IntermediaryAccount = "0011223344",
                },
                Payment(Currency.UAH) with { Iban = string.Empty, BeneficiaryBank = string.Empty, Swift = string.Empty },
            ],
        };

        var put = await owner.PutAsJsonAsync(Url, request, Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var reloaded = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);
        Assert.NotNull(reloaded);
        Assert.Equal("ФОП Тест Тестович", reloaded.SellerNameUk);
        Assert.Equal("1234567890", reloaded.Rnokpp);
        Assert.Equal("Fees are paid by the payer.", reloaded.FeesClauseEn);
        Assert.Equal([Currency.USD, Currency.EUR], reloaded.PaymentDetails.Select(row => row.Currency));
        var usd = reloaded.PaymentDetails[0];
        Assert.Equal(InvoicingTestData.ValidIban, usd.Iban);
        Assert.Equal("UNJSUAUKXXX", usd.Swift);
        Assert.Equal("Intermediary Bank", usd.IntermediaryBank);
        Assert.Equal("0011223344", usd.IntermediaryAccount);

        await owner.PutAsJsonAsync(Url, request with { PaymentDetails = [Payment(Currency.EUR)] }, Json);

        var replaced = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);
        Assert.Equal([Currency.EUR], replaced!.PaymentDetails.Select(row => row.Currency));
    }

    [Theory]
    [InlineData("iban", "UA00000000000000000000000000")]
    [InlineData("iban", "UA57322001000002600723356600")]
    [InlineData("iban", "DE89370400440532013000")]
    [InlineData("iban", "not an iban")]
    [InlineData("swift", "UNJSUA")]
    [InlineData("swift", "UNJSUAUKXX")]
    [InlineData("swift", "UNJSUAUKXXXX")]
    [InlineData("swift", "UNJS-AUK")]
    [InlineData("rnokpp", "123456789")]
    [InlineData("rnokpp", "12345678901")]
    [InlineData("rnokpp", "12345abcde")]
    [InlineData("beneficiaryBank", "")]
    [InlineData("acceptanceClauseEn", "   ")]
    [InlineData("taxStatusClauseUk", "")]
    public async Task An_invalid_value_is_rejected_with_its_field_and_stores_nothing(string field, string value)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var before = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);
        var request = field switch
        {
            "iban" => Valid() with { PaymentDetails = [Payment(Currency.USD) with { Iban = value }] },
            "swift" => Valid() with { PaymentDetails = [Payment(Currency.USD) with { Swift = value }] },
            "beneficiaryBank" => Valid() with { PaymentDetails = [Payment(Currency.USD) with { BeneficiaryBank = value }] },
            "rnokpp" => Valid() with { Rnokpp = value },
            "acceptanceClauseEn" => Valid() with { AcceptanceClauseEn = value },
            "taxStatusClauseUk" => Valid() with { TaxStatusClauseUk = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var response = await owner.PutAsJsonAsync(Url, request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains(field, problem, StringComparison.Ordinal);
        var after = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);
        Assert.Equal(before, after, new JsonEquality());
    }

    [Theory]
    [InlineData("UNJSUAUK")]
    [InlineData("UNJSUAUKXXX")]
    public async Task A_swift_of_8_or_11_characters_is_accepted(string swift)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync(
            Url, Valid() with { PaymentDetails = [Payment(Currency.USD) with { Swift = swift }] }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task One_currency_cannot_carry_two_sets_of_payment_details()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync(
            Url, Valid() with { PaymentDetails = [Payment(Currency.USD), Payment(Currency.USD)] }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paymentDetails[1].currency", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signature_uploads_is_served_back_with_private_caching_and_can_be_removed()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        Assert.Equal(HttpStatusCode.NoContent, (await Upload(owner, InvoicingTestData.Png, "image/png")).StatusCode);

        var served = await owner.GetAsync($"{Url}/signature");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(InvoicingTestData.Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Contains("private", served.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.True((await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json))!.HasSignature);

        Assert.Equal(HttpStatusCode.NoContent, (await Upload(owner, InvoicingTestData.Jpeg, "image/jpeg")).StatusCode);
        Assert.Equal("image/jpeg", (await owner.GetAsync($"{Url}/signature")).Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Url}/signature")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Url}/signature")).StatusCode);
        Assert.False((await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json))!.HasSignature);
    }

    [Fact]
    public async Task Saving_the_details_leaves_the_signature_alone()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await Upload(owner, InvoicingTestData.Png, "image/png");

        await owner.PutAsJsonAsync(Url, Valid(), Json);

        Assert.True((await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json))!.HasSignature);
        await owner.DeleteAsync($"{Url}/signature");
    }

    [Fact]
    public async Task A_signature_of_another_type_or_content_or_size_is_rejected()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await owner.DeleteAsync($"{Url}/signature");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(owner, [0x47, 0x49, 0x46, 0x38], "image/gif")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(owner, InvoicingTestData.Png, "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(owner, "<svg/>"u8.ToArray(), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(owner, InvoicingTestData.Png, "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Upload(owner, [], "image/png")).StatusCode);

        var oversize = new byte[(512 * 1024) + 1];
        InvoicingTestData.Png.CopyTo(oversize, 0);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(owner, oversize, "image/png")).StatusCode);

        var atLimit = new byte[512 * 1024];
        InvoicingTestData.Png.CopyTo(atLimit, 0);
        Assert.Equal(HttpStatusCode.NoContent, (await Upload(owner, atLimit, "image/png")).StatusCode);

        await owner.DeleteAsync($"{Url}/signature");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Url}/signature")).StatusCode);
    }

    [Fact]
    public async Task Another_owner_sees_neither_the_details_nor_the_signature_and_a_visitor_sees_nothing()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        using var other = await SignIn(ApiFixture.SecondAllowedEmail);
        using var visitor = fixture.CreateClient();
        await owner.PutAsJsonAsync(Url, Valid() with { SellerNameUk = "Тільки власник", PaymentDetails = [Payment(Currency.USD)] }, Json);
        await Upload(owner, InvoicingTestData.Png, "image/png");

        var seen = await other.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);

        Assert.Equal(string.Empty, seen!.SellerNameUk);
        Assert.Empty(seen.PaymentDetails);
        Assert.False(seen.HasSignature);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Url}/signature")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"{Url}/signature")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsJsonAsync(Url, Valid(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync($"{Url}/prefill-from-monobank", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(visitor, InvoicingTestData.Png, "image/png")).StatusCode);

        await owner.DeleteAsync($"{Url}/signature");
    }

    [Fact]
    public async Task Changes_are_logged_but_the_log_holds_the_image_size_and_never_its_bytes()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync(Url, Valid() with { PaymentDetails = [Payment(Currency.USD)] }, Json);
        await Upload(owner, InvoicingTestData.Png, "image/png");

        var raw = await owner.GetStringAsync("/api/audit?entity=InvoicingDetails");
        var log = JsonSerializer.Deserialize<AuditEntryResponse[]>(raw, Json)!;

        Assert.NotEmpty(log);
        Assert.Contains(log, entry => entry.After is { } after
            && after.TryGetValue("signatureImageBytes", out var size)
            && size.ValueKind == JsonValueKind.Number
            && size.GetInt32() == InvoicingTestData.Png.Length);
        Assert.Contains(log, entry => entry.After is { } after
            && after.TryGetValue("iban", out var iban)
            && iban.GetString() == InvoicingTestData.ValidIban);
        Assert.DoesNotContain(Convert.ToBase64String(InvoicingTestData.Png), raw, StringComparison.Ordinal);

        await owner.DeleteAsync($"{Url}/signature");
    }

    [Fact]
    public async Task Prefill_suggests_the_stored_accounts_and_the_fresh_name_and_saves_nothing()
    {
        var clientInfo = StubMonobankHandler.ClientInfo(
            "client-prefill",
            ("fop-uah", "fop", 980, InvoicingTestData.OtherValidIban),
            ("fop-usd", "fop", 840, InvoicingTestData.ValidIban),
            ("black", "black", 980, "UA000000000000000000000000001"),
            ("fop-pln", "fop", 985, "UA000000000000000000000000002"));
        var stub = StubMonobankHandler.ForToken("prefill-token", clientInfo);
        // A fake clock, because the token save spends the client-info slot and the prefill answers 429 until a
        // minute has passed.
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2095, 7, 1, 10, 0, 0, TimeSpan.Zero));
        await using var application = fixture.CreateApplication(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddHttpClient<MonobankClient>().ConfigurePrimaryHttpMessageHandler(() => stub);
        }));
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await owner.PutAsJsonAsync(Url, Valid() with { SellerNameUk = "Збережене ім'я", PaymentDetails = [Payment(Currency.EUR)] }, Json);
        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.PutAsJsonAsync("/api/monobank/connection", new { token = "prefill-token" })).StatusCode);
        clock.Advance(MonobankRateGate.Interval + TimeSpan.FromSeconds(1));

        var response = await owner.PostAsync($"{Url}/prefill-from-monobank", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prefill = await response.Content.ReadFromJsonAsync<MonobankPrefillResponse>(Json);
        Assert.NotNull(prefill);
        Assert.Equal("Test FOP", prefill.SellerNameUk);
        Assert.Equal([Currency.UAH, Currency.USD], prefill.PaymentDetails.Select(row => row.Currency));
        Assert.All(prefill.PaymentDetails, row =>
        {
            Assert.Equal("JSC Universal Bank, Kyiv", row.BeneficiaryBank);
            Assert.Equal("UNJSUAUKXXX", row.Swift);
            Assert.Equal(string.Empty, row.IntermediaryBank);
        });
        Assert.Equal(InvoicingTestData.OtherValidIban, prefill.PaymentDetails[0].Iban);
        Assert.Equal(InvoicingTestData.ValidIban, prefill.PaymentDetails[1].Iban);

        var saved = await owner.GetFromJsonAsync<InvoicingDetailsResponse>(Url, Json);
        Assert.Equal("Збережене ім'я", saved!.SellerNameUk);
        Assert.Equal([Currency.EUR], saved.PaymentDetails.Select(row => row.Currency));
    }

    [Theory]
    [InlineData("UA23060070000082704", "iban has 19 characters, 29 expected.")]
    [InlineData("DE573220010000026007233566001", "iban must start with UA.")]
    [InlineData("UA573220010000026007233566002", "iban checksum is wrong.")]
    public async Task A_rejected_iban_says_what_is_wrong_with_it(string iban, string message)
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);

        var response = await owner.PutAsJsonAsync(Url, Valid() with { PaymentDetails = [Payment(Currency.USD) with { Iban = iban }] }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(message, Assert.Single(problem.GetProperty("errors").GetProperty("paymentDetails[0].iban").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_null_payment_details_element_is_a_validation_error_not_a_server_error()
    {
        using var owner = await SignIn(ApiFixture.AllowedEmail);
        var body = JsonSerializer.SerializeToNode(Valid(), Json)!.AsObject();
        body["paymentDetails"] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);

        var response = await owner.PutAsJsonAsync(Url, body, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paymentDetails", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prefill_without_a_monobank_connection_is_a_conflict()
    {
        using var owner = await SignIn(ApiFixture.SecondAllowedEmail);

        var response = await owner.PostAsync($"{Url}/prefill-from-monobank", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static InvoicingDetailsRequest Valid() => new(
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

    private static PaymentDetailsInput Payment(Currency currency) => new(
        currency, InvoicingTestData.ValidIban, "JSC Universal Bank, Kyiv", "UNJSUAUKXXX", string.Empty, string.Empty, string.Empty);

    private static Task<HttpResponseMessage> Upload(HttpClient client, byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return client.PutAsync($"{Url}/signature", content);
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

    private sealed class JsonEquality : IEqualityComparer<InvoicingDetailsResponse?>
    {
        public bool Equals(InvoicingDetailsResponse? x, InvoicingDetailsResponse? y) =>
            JsonSerializer.Serialize(x, Json) == JsonSerializer.Serialize(y, Json);

        public int GetHashCode(InvoicingDetailsResponse? obj) => JsonSerializer.Serialize(obj, Json).GetHashCode(StringComparison.Ordinal);
    }
}
