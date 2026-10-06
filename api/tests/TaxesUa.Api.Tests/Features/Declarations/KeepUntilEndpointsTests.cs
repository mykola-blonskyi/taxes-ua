using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.TaxYears;

namespace TaxesUa.Api.Tests.Features.Declarations;

// ApiFixture is an IClassFixture, so this class owns its database and its suspension row. xUnit runs
// one class's tests in sequence, so the one test that changes the suspension puts the seed back.
public sealed class KeepUntilEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly LimitationSuspensionRequest Seed = new(
        new DateOnly(2022, 3, 17),
        null,
        "ПКУ п. 102.9 (ЗУ № 2120-IX, з 17.03.2022 по 31.07.2023), підп. 69.9 та 69.36 п. 69 підрозд. 10 розд. XX (ЗУ № 3219-IX, № 3453-IX)");

    [Fact]
    public async Task The_seeded_open_suspension_extends_2026_from_its_last_deadline()
    {
        await using var application = fixture.CreateApplication(_ => { });
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);

        var suspension = await owner.GetFromJsonAsync<LimitationSuspensionResponse>("/api/limitation-suspension", Json);
        var keep = await owner.GetFromJsonAsync<YearKeepUntilResponse>("/api/declarations/2026/keep-until", Json);

        Assert.Equal(new LimitationSuspensionResponse(Seed.Start, Seed.End, Seed.Source), suspension);
        Assert.Equal(
            new KeepUntilResponse(KeepUntilState.ExtendedWhileSuspended, new DateOnly(2030, 2, 8), 1095),
            keep!.KeepUntil);
    }

    [Fact]
    public async Task A_filed_mark_replaces_the_deadline_the_count_starts_from()
    {
        const int year = 2015;
        await using var application = fixture.CreateApplication(_ => { });
        using var owner = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/tax-years/{year}", YearRequest(), Json)).StatusCode);

        // Q4 2015 is due Tuesday 2016-02-09; 1095 days on is 2019-02-08.
        Assert.Equal(
            new KeepUntilResponse(KeepUntilState.Fixed, new DateOnly(2019, 2, 8), null),
            (await owner.GetFromJsonAsync<YearKeepUntilResponse>($"/api/declarations/{year}/keep-until", Json))!.KeepUntil);

        var mark = await owner.PutAsJsonAsync(
            $"/api/declarations/{year}/4/filing",
            new DeclarationFilingRequest(new DateOnly(2016, 2, 1), DeclarationType.Reporting),
            Json);
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        Assert.Equal(
            new KeepUntilResponse(KeepUntilState.Fixed, new DateOnly(2019, 1, 31), null),
            (await owner.GetFromJsonAsync<YearKeepUntilResponse>($"/api/declarations/{year}/keep-until", Json))!.KeepUntil);
    }

    [Fact]
    public async Task Only_an_admin_closes_the_suspension_and_a_closed_one_fixes_the_date()
    {
        await using var application = fixture.CreateApplication(_ => { });
        using var admin = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        using var member = await ApiFixture.SignIn(application, ApiFixture.NonAdminEmail);
        var closed = Seed with { End = new DateOnly(2027, 6, 30) };
        try
        {
            var refused = await member.PutAsJsonAsync("/api/limitation-suspension", closed, Json);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

            var backwards = await admin.PutAsJsonAsync(
                "/api/limitation-suspension", Seed with { End = new DateOnly(2022, 3, 16) }, Json);
            Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
            ProblemAssert.FieldIs(
                await ProblemAssert.CodeIsAsync(backwards, ProblemCodes.ValidationFailed),
                "end",
                ProblemCodes.SuspensionEndBeforeStart);

            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/limitation-suspension", closed, Json)).StatusCode);
            var logged = Assert.Single(
                (await admin.GetFromJsonAsync<AuditEntryResponse[]>("/api/audit?entity=LimitationSuspension", Json))!);
            Assert.Equal((AuditAction.Update, "2027-06-30"), (logged.Action, logged.After!["end"].GetString()));

            // Every 2026 declaration falls inside the suspension, so each count starts on 2027-07-01.
            Assert.Equal(
                new KeepUntilResponse(KeepUntilState.Fixed, new DateOnly(2030, 6, 29), null),
                (await member.GetFromJsonAsync<YearKeepUntilResponse>("/api/declarations/2026/keep-until", Json))!.KeepUntil);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/limitation-suspension", Seed, Json)).StatusCode);
        }
    }

    private static TaxYearConfigRequest YearRequest() => new(
        MinWageKop: 137_800,
        SingleTaxRateBp: 500,
        MilitaryLevyRateBp: 0,
        EsvRateBp: 2200,
        ExcessRateBp: 1500,
        IncomeLimitMinWages: 1167,
        LimitWarnThresholdsPct: [85],
        EsvDeadlineDay: 19,
        DeclarationDays: 40,
        TaxPaymentDaysAfterDeclaration: 10,
        AdvanceRecommendedDay: 15,
        Group3ApplicationDays: 10,
        Holidays: [],
        Source: "a test source");
}
