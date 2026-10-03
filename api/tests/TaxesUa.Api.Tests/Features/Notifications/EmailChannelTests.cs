using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TaxesUa.Api.Features.Notifications;
using static TaxesUa.Api.Tests.Features.Notifications.TelegramSteps;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Tests.Features.Notifications;

// The email channel through the endpoints the settings screen uses, with the SMTP sender in memory.
public sealed partial class EmailChannelTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly DateTimeOffset Start = new(2031, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private const string Address = "owner@mail.test";

    // Owners no other test shares (see ApiFixture.NewOwner).
    private readonly string _ownerEmail = fixture.NewOwner();

    private readonly string _otherEmail = fixture.NewOwner();

    private const string Email = "Email";

    [Fact]
    public async Task Without_smtp_settings_the_channel_is_unavailable_and_nothing_can_be_added()
    {
        await using var application = fixture.CreateApplication(_ => { });
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);

        var channel = await Channel(owner, Email);
        var added = await Add(owner, Address);

        Assert.False(channel["available"]!.GetValue<bool>());
        Assert.False(channel["linked"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, added.StatusCode);
        await ProblemAssert.CodeIsAsync(added, "email_not_configured");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.PostAsync(Channels + "/email/test", null)).StatusCode);
    }

    [Fact]
    public async Task Adding_an_address_sends_only_the_confirmation_and_leaves_the_channel_off()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);

        var added = await Add(owner, "  " + Address + " ");
        var channel = (await added.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.True(channel["available"]!.GetValue<bool>());
        Assert.True(channel["linked"]!.GetValue<bool>());
        Assert.False(channel["confirmed"]!.GetValue<bool>());
        Assert.False(channel["enabled"]!.GetValue<bool>());
        Assert.Equal(Address, channel["address"]!.GetValue<string>());
        var message = Assert.Single(email.Delivered);
        Assert.Equal(Address, message.To);
        Assert.Equal("Підтвердьте адресу для нагадувань про податки", message.Subject);
        var link = LinkOf(message);
        Assert.StartsWith("https://taxes.test/settings?tab=notifications&confirmEmail=", link);
        var encoded = link.Replace("&", "&amp;", StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{encoded}\">{encoded}</a>", message.Html);
        Assert.Contains("<p>", message.Html);

        Assert.Equal(HttpStatusCode.Conflict, (await SwitchEmail(owner, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(Channels + "/email/test", null)).StatusCode);
        Assert.Single(email.Delivered);
    }

    [Fact]
    public async Task The_confirmation_is_in_russian_when_the_owner_reads_the_app_in_russian()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await SetLocale(owner, "ru");

        await Add(owner, Address);

        Assert.Equal("Подтвердите адрес для напоминаний о налогах", Assert.Single(email.Delivered).Subject);
        await SetLocale(owner, "uk");
    }

    [Fact]
    public async Task A_valid_link_confirms_the_address_and_switches_the_channel_on()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);

        var confirmed = await Confirm(owner, TokenOf(Assert.Single(email.Delivered)));

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var channel = await Channel(owner, Email);
        Assert.True(channel["confirmed"]!.GetValue<bool>());
        Assert.True(channel["enabled"]!.GetValue<bool>());
        Assert.Equal(Address, channel["address"]!.GetValue<string>());
        Assert.Single(email.Delivered);
    }

    [Fact]
    public async Task Opening_the_link_twice_is_harmless()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);
        var token = TokenOf(Assert.Single(email.Delivered));

        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, token)).StatusCode);
        await SwitchEmail(owner, false);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, token)).StatusCode);

        Assert.False((await Channel(owner, Email))["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task An_expired_link_confirms_nothing_and_a_resent_one_does()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Start);
        await using var application = fixture.CreateApplication(email, clock);
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);
        var token = TokenOf(Assert.Single(email.Delivered));

        clock.Advance(EmailConfirmation.Lifetime - TimeSpan.FromSeconds(1));
        var justInTime = await Confirm(owner, token);
        Assert.Equal(HttpStatusCode.OK, justInTime.StatusCode);
        await Remove(owner);
        await Add(owner, Address);
        var second = TokenOf(email.Delivered[^1]);

        clock.Advance(EmailConfirmation.Lifetime + TimeSpan.FromSeconds(1));
        var expired = await Confirm(owner, second);

        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        await ProblemAssert.CodeIsAsync(expired, "email_link_expired");
        Assert.False((await Channel(owner, Email))["confirmed"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(Channels + "/email/resend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, TokenOf(email.Delivered[^1]))).StatusCode);
        Assert.True((await Channel(owner, Email))["confirmed"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("truncated")]
    [InlineData("garbage")]
    [InlineData("empty")]
    public async Task A_tampered_link_is_refused(string how)
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);
        var token = TokenOf(Assert.Single(email.Delivered));
        var middle = token.Length / 2;
        var tampered = how switch
        {
            "changed" => token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..],
            "truncated" => token[..^8],
            "garbage" => "not-a-token",
            _ => string.Empty,
        };

        var response = await Confirm(owner, tampered);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ProblemAssert.CodeIsAsync(response, "email_link_invalid");
        Assert.False((await Channel(owner, Email))["confirmed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_link_does_not_work_for_another_owner()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        using var other = await ApiFixture.SignIn(application, _otherEmail);
        await Add(owner, Address);
        await Add(other, "other@mail.test");

        var response = await Confirm(other, TokenOf(email.Delivered[0]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await Channel(owner, Email))["confirmed"]!.GetValue<bool>());
        Assert.False((await Channel(other, Email))["confirmed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Confirming_needs_the_owners_session()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);
        using var stranger = ApiFixture.CreateClient(application);

        var response = await Confirm(stranger, TokenOf(Assert.Single(email.Delivered)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_link_for_an_address_since_replaced_or_removed_opens_nothing()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, "typo@mail.test");
        var typo = TokenOf(email.Delivered[0]);
        await Add(owner, Address);
        var right = TokenOf(email.Delivered[1]);

        Assert.Equal(HttpStatusCode.Gone, (await Confirm(owner, typo)).StatusCode);
        Assert.False((await Channel(owner, Email))["confirmed"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, right)).StatusCode);

        await Remove(owner);
        Assert.Equal(HttpStatusCode.Gone, (await Confirm(owner, right)).StatusCode);
        Assert.False((await Channel(owner, Email))["linked"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Changing_the_address_switches_the_channel_off_until_the_new_one_is_confirmed()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Add(owner, Address);
        await Confirm(owner, TokenOf(email.Delivered[0]));

        await Add(owner, "new@mail.test");

        var channel = await Channel(owner, Email);
        Assert.Equal("new@mail.test", channel["address"]!.GetValue<string>());
        Assert.False(channel["confirmed"]!.GetValue<bool>());
        Assert.False(channel["enabled"]!.GetValue<bool>());
        Assert.Equal(["owner@mail.test", "new@mail.test"], email.Delivered.Select(message => message.To));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("a@b")]
    [InlineData("Name <a@mail.test>")]
    [InlineData("a@mail.test, b@mail.test")]
    [InlineData("a b@mail.test")]
    [InlineData("a@mail.test\r\nBcc: b@mail.test")]
    public async Task Something_that_is_not_a_plain_address_is_refused(string input)
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);

        var response = await Add(owner, input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(email.Attempts);
        Assert.False((await Channel(owner, Email))["linked"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_test_message_is_plain_text_and_simple_html_and_goes_even_when_switched_off()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Connected(owner, email);
        await SwitchEmail(owner, false);

        var response = await owner.PostAsync(Channels + "/email/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = Assert.Single(email.Delivered);
        Assert.Equal(Address, message.To);
        Assert.Equal("Тестове повідомлення", message.Subject);
        Assert.Equal("Тестове повідомлення: нагадування надходитимуть на цю адресу.", message.Text);
        Assert.Equal(
            "<!DOCTYPE html><html><body style=\"font-family:sans-serif;line-height:1.5\">"
            + "<p>Тестове повідомлення: нагадування надходитимуть на цю адресу.</p></body></html>",
            message.Html);
        var channel = await Channel(owner, Email);
        Assert.False(channel["enabled"]!.GetValue<bool>());
        Assert.NotNull(channel["lastDeliveryAt"]);
        Assert.Null(channel["lastFailure"]);
    }

    [Fact]
    public async Task The_test_button_tries_once_so_the_owner_is_not_left_waiting_and_the_next_press_can_deliver()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Connected(owner, email);
        email.Clear();
        email.Answer = (_, _) => new DeliveryAttempt(DeliveryFailure.Unreachable);

        var response = await owner.PostAsync(Channels + "/email/test", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Single(email.Attempts);
        var channel = await Channel(owner, Email);
        Assert.Equal("Unreachable", channel["lastFailure"]!.GetValue<string>());
        Assert.NotNull(channel["lastFailureAt"]);
        Assert.True(channel["enabled"]!.GetValue<bool>());

        email.Answer = null;
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(Channels + "/email/test", null)).StatusCode);
        Assert.Null((await Channel(owner, Email))["lastFailure"]);
    }

    [Fact]
    public async Task A_confirmation_is_tried_once_even_when_the_failure_could_pass()
    {
        var email = new InMemoryEmailTransport { Answer = (_, _) => new DeliveryAttempt(DeliveryFailure.Unreachable) };
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);

        var added = await Add(owner, Address);

        Assert.Equal(HttpStatusCode.BadGateway, added.StatusCode);
        Assert.Single(email.Attempts);
        Assert.Equal("Unreachable", (await Channel(owner, Email))["lastFailure"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_confirmation_that_cannot_be_sent_shows_the_failure_and_can_be_sent_again()
    {
        var email = new InMemoryEmailTransport { Answer = (_, _) => new DeliveryAttempt(DeliveryFailure.Authentication) };
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);

        var added = await Add(owner, Address);

        Assert.Equal(HttpStatusCode.BadGateway, added.StatusCode);
        var channel = await Channel(owner, Email);
        Assert.True(channel["linked"]!.GetValue<bool>());
        Assert.False(channel["confirmed"]!.GetValue<bool>());
        Assert.Equal("Authentication", channel["lastFailure"]!.GetValue<string>());

        email.Answer = null;
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(Channels + "/email/resend", null)).StatusCode);
        Assert.Null((await Channel(owner, Email))["lastFailure"]);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, TokenOf(Assert.Single(email.Delivered)))).StatusCode);
    }

    [Fact]
    public async Task Removing_the_address_forgets_it()
    {
        var email = new InMemoryEmailTransport();
        await using var application = fixture.CreateApplication(email, new FakeTimeProvider(Start));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        await Connected(owner, email);

        Assert.Equal(HttpStatusCode.NoContent, (await Remove(owner)).StatusCode);

        var channel = await Channel(owner, Email);
        Assert.False(channel["linked"]!.GetValue<bool>());
        Assert.Null(channel["address"]);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync(Channels + "/email/test", null)).StatusCode);
    }

    [Fact]
    public async Task The_password_never_reaches_a_log_or_a_response()
    {
        var email = new InMemoryEmailTransport();
        var clock = new FakeTimeProvider(Start);
        var logs = new CapturedLogs();
        await using var application = fixture.CreateApplication(
            email, clock, configure: builder => builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddFilter((_, _, _) => true);
                logging.AddProvider(logs);
            }));
        using var owner = await ApiFixture.SignIn(application, _ownerEmail);
        var bodies = new List<string>();

        bodies.Add(await (await Add(owner, Address)).Content.ReadAsStringAsync());
        await Confirm(owner, TokenOf(Assert.Single(email.Delivered)));
        email.Answer = (_, _) => new DeliveryAttempt(DeliveryFailure.Authentication);
        bodies.Add(await (await owner.PostAsync(Channels + "/email/test", null)).Content.ReadAsStringAsync());
        bodies.Add(await owner.GetStringAsync(Channels));

        Assert.Contains(logs.Lines, line => line.StartsWith("TaxesUa.Api.Features.Notifications", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ApiFixture.SmtpTestPassword, StringComparison.Ordinal));
        Assert.All(bodies, body => Assert.DoesNotContain(ApiFixture.SmtpTestPassword, body));
        Assert.All(bodies, body => Assert.DoesNotContain("mailer", body));
    }

    [GeneratedRegex(@"https://\S*confirmEmail=[A-Za-z0-9_-]+")]
    private static partial Regex ConfirmationLink();

    internal static string LinkOf(EmailMessage message) => ConfirmationLink().Match(message.Text).Value;

    internal static string TokenOf(EmailMessage message) => LinkOf(message)[(LinkOf(message).IndexOf("confirmEmail=", StringComparison.Ordinal) + "confirmEmail=".Length)..];

    public static Task<HttpResponseMessage> Add(HttpClient owner, string address) =>
        owner.PostAsJsonAsync(Channels + "/email", new { address });

    public static Task<HttpResponseMessage> Confirm(HttpClient owner, string token) =>
        owner.PostAsJsonAsync(Channels + "/email/confirm", new { token });

    public static Task<HttpResponseMessage> SwitchEmail(HttpClient owner, bool enabled) =>
        owner.PutAsJsonAsync(Channels + "/email", new { enabled });

    public static Task<HttpResponseMessage> Remove(HttpClient owner) => owner.DeleteAsync(Channels + "/email");

    // An address added and confirmed through the owner's own calls, with the confirmation mail cleared.
    internal static async Task Connected(HttpClient owner, InMemoryEmailTransport email, string address = Address)
    {
        Assert.Equal(HttpStatusCode.OK, (await Add(owner, address)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(owner, TokenOf(email.Delivered[^1]))).StatusCode);
        email.Clear();
    }
}
