using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Settings;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;

namespace TaxesUa.Api.Tests.Features.Notifications;

// What an owner does to get a Telegram channel into a state, through the same endpoints and poller
// the app uses.
internal static class TelegramSteps
{
    public const string Channels = "/api/notifications/channels";

    public const long OwnerChat = 4242;

    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Each test has an owner of its own, with nothing linked. The poll offset is the bot's, not an owner's,
    // so every test in the class shares it and starts from none.
    public static async Task ForgetPollOffset(WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        await ForgetPollOffset(scope.ServiceProvider);
    }

    public static Task<int> ForgetPollOffset(IServiceProvider services) =>
        services.GetRequiredService<AppDbContext>().TelegramPollStates.ExecuteDeleteAsync();

    public static async Task<string> Connect(HttpClient owner)
    {
        var response = await owner.PostAsync(Channels + "/telegram/connect", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["url"]!.GetValue<string>();
    }

    public static string CodeOf(string url) => url[(url.IndexOf("start=", StringComparison.Ordinal) + "start=".Length)..];

    public static Task Poll(WebApplicationFactory<Program> application) =>
        application.Services.GetRequiredService<TelegramPoller>().PollOnceAsync(TimeSpan.Zero, CancellationToken.None);

    public static async Task Link(WebApplicationFactory<Program> application, HttpClient owner, StubTelegramHandler telegram)
    {
        var next = telegram.Updates.Count == 0 ? 10 : telegram.Updates.Max(update => update["update_id"]!.GetValue<long>()) + 1;
        telegram.Updates.Add(StubTelegramHandler.Update(next, OwnerChat, $"/start {CodeOf(await Connect(owner))}"));
        await Poll(application);
        Assert.True((await Channel(owner))["linked"]!.GetValue<bool>());
    }

    public static Task<JsonObject> Channel(HttpClient client) => Channel(client, "Telegram");

    public static async Task<JsonObject> Channel(HttpClient client, string kind)
    {
        var channels = await client.GetFromJsonAsync<JsonArray>(Channels);
        return channels!.Single(channel => channel!["kind"]!.GetValue<string>() == kind)!.AsObject();
    }

    public static async Task<JsonObject> Toggle(HttpClient client, bool enabled)
    {
        var response = await client.PutAsJsonAsync(Channels + "/telegram", new { enabled });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public static Task SetLocale(HttpClient owner, string locale) => SetSettings(owner, new DateOnly(2031, 1, 1), locale);

    public static async Task SetSettings(
        HttpClient owner, DateOnly registered, string locale, PaymentMode mode = PaymentMode.Quarterly, bool esvExempt = false)
    {
        var request = new SettingsRequest(
            registered, mode, EsvRegistrationMonthPolicy.FullMonth, esvExempt, true, true,
            [DayOfWeek.Saturday, DayOfWeek.Sunday], locale, "system", "UAH");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/settings", request, Json)).StatusCode);
    }
}
