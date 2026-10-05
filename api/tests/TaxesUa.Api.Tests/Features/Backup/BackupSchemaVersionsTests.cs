using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaxesUa.Api.Features.Backup;
using TaxesUa.Api.Tests.Features.Fx;

namespace TaxesUa.Api.Tests.Features.Backup;

// A build restores only the schema it writes (#254): the nightly encrypted dump is the disaster backup, and the
// owner downloads a fresh file after a release that raises the version. Fixtures/backup-vNN.json is one small
// owner's file as the current exporter writes it, renamed when the version is raised.
public sealed class BackupSchemaVersionsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public void The_fixture_is_of_the_current_schema_version()
    {
        Assert.Equal(BackupDocument.CurrentSchemaVersion, Fixture()["schemaVersion"]!.GetValue<int>());
    }

    // The fixture is the exporter's own output, so an exporter change without a version bump shows here.
    [Fact]
    public async Task The_current_fixture_restores_and_exports_back_unchanged()
    {
        await using var application = fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>()), Today);
        using var owner = await ApiFixture.SignIn(application, fixture.NewOwner());
        var file = Fixture();

        var response = await Post(owner, file);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var backup = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!;
        Assert.Equal(WithIdsByPosition(file), WithIdsByPosition(backup));
    }

    [Theory]
    [InlineData(1, "backup_too_old")]
    [InlineData(BackupDocument.CurrentSchemaVersion - 1, "backup_too_old")]
    [InlineData(BackupDocument.CurrentSchemaVersion + 1, "backup_version_unsupported")]
    public async Task A_file_of_another_schema_version_is_refused_by_its_version(int version, string code)
    {
        await using var application = fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>()), Today);
        using var owner = await ApiFixture.SignIn(application, fixture.NewOwner());
        var file = Fixture();
        file["schemaVersion"] = version;

        var response = await Post(owner, file);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Empty(JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!["clients"]!.AsArray());
    }

    // An optional member with a default is how a field is added without a bump: a file written before it
    // existed lacks it and still restores. The reserve jar is the document's one such member today.
    [Fact]
    public async Task A_current_file_without_an_optional_field_restores_with_its_default()
    {
        await using var application = fixture.CreateApplication(
            StubNbuHandler.ByDate(new Dictionary<string, string>()), Today);
        using var owner = await ApiFixture.SignIn(application, fixture.NewOwner());
        var file = Fixture();
        Assert.NotNull(file["reserveJar"]);
        file.Remove("reserveJar");

        var response = await Post(owner, file);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var backup = JsonNode.Parse(await owner.GetStringAsync("/api/backup"))!.AsObject();
        Assert.True(backup.ContainsKey("reserveJar"));
        Assert.Null(backup["reserveJar"]);
        Assert.Equal(2, backup["clients"]!.AsArray().Count);
    }

    // A restore takes fresh ids when another owner already holds the file's, so ids compare by where they first
    // appear and the links between rows still have to match.
    private static string WithIdsByPosition(JsonNode file)
    {
        var seen = new Dictionary<string, int>();
        return System.Text.RegularExpressions.Regex.Replace(
            file.ToJsonString(),
            "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
            match =>
            {
                seen.TryAdd(match.Value, seen.Count);
                return $"id-{seen[match.Value]}";
            });
    }

    private static Task<HttpResponseMessage> Post(HttpClient owner, JsonObject file) =>
        owner.PostAsync("/api/restore", new StringContent(file.ToJsonString(), Encoding.UTF8, "application/json"));

    private static JsonObject Fixture() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "BackupFixtures", $"backup-v{BackupDocument.CurrentSchemaVersion:00}.json")))!.AsObject();
}
