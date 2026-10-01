using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Dashboard;

// The reserve jar's balance set against what the taxes need (#102). The jar row is written straight to the
// database here: how it gets there is the Monobank tests' business, and this class owns the figures.
public sealed partial class TaxReserveEndpointsTests
{
    private const long NeededKop = 60_000 + EsvQuarterKop + (2 * EsvMonthKop);

    [Fact]
    public async Task A_jar_that_covers_the_reserve_shows_the_surplus_and_when_its_balance_was_read()
    {
        const int year = 2097;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);
        await StoreJar(application, ApiFixture.SecondAllowedEmail, 1_500_000, now.AddHours(-3));

        var reserve = (await Get(client)).Reserve!;

        Assert.Equal(NeededKop, reserve.TotalKop);
        Assert.False(reserve.CanChooseJar);
        var jar = reserve.Jar!;
        Assert.Equal(("На податки", 1_500_000L, now.AddHours(-3), false), (jar.Title, jar.BalanceKop, jar.FetchedAt, jar.Stale));
        Assert.Equal((1_500_000 - NeededKop, 0L, (DateOnly?)null, 0L, (int?)null), (jar.SurplusKop, jar.ShortfallKop, jar.TopUpBy, jar.TopUpKop, jar.TopUpDaysLeft));
    }

    [Fact]
    public async Task A_jar_short_of_a_later_deadline_asks_for_the_top_up_by_that_deadline_and_the_whole_gap()
    {
        const int year = 2099;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);
        await StoreJar(application, ApiFixture.SecondAllowedEmail, 600_000, now);

        var jar = (await Get(client)).Reserve!.Jar!;

        // 6,000.00 pays Q1's ESV (5,707.02) and 292.98 of the 600.00 of tax and levy due on the tax deadline.
        var taxDue = Deadlines(year, 1).TaxPayment.Due;
        Assert.Equal(
            (0L, NeededKop - 600_000, (DateOnly?)taxDue, EsvQuarterKop + 60_000 - 600_000, (int?)DaysFrom(May1(year), taxDue)),
            (jar.SurplusKop, jar.ShortfallKop, jar.TopUpBy, jar.TopUpKop, jar.TopUpDaysLeft));
    }

    [Fact]
    public async Task A_jar_short_of_an_overdue_deadline_names_it_with_negative_days_left()
    {
        const int year = 2079;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await PostIncome(client, new DateOnly(year, 2, 10), 1_000_000);
        await StoreJar(application, ApiFixture.SecondAllowedEmail, 100_000, now);

        var jar = (await Get(client)).Reserve!.Jar!;

        var esvDue = Deadlines(year, 1).Esv.Due;
        Assert.Equal(
            ((DateOnly?)esvDue, EsvQuarterKop - 100_000, true),
            (jar.TopUpBy, jar.TopUpKop, jar.TopUpDaysLeft < 0));
    }

    [Fact]
    public async Task A_balance_over_a_day_old_is_marked_stale_and_keeps_its_time()
    {
        const int year = 2077;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await StoreJar(application, ApiFixture.SecondAllowedEmail, 0, now.AddHours(-25));

        var jar = (await Get(client)).Reserve!.Jar!;

        Assert.Equal((true, now.AddHours(-25)), (jar.Stale, jar.FetchedAt));
    }

    [Fact]
    public async Task Another_owner_sees_no_jar_and_none_of_its_figures()
    {
        const int year = 2075;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var owner = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        using var other = await ApiFixture.SignIn(application, ApiFixture.AllowedEmail);
        await SetUp(owner, year, PaymentMode.Quarterly);
        await SetUp(other, year, PaymentMode.Quarterly);
        await StoreJar(application, ApiFixture.SecondAllowedEmail, 1_500_000, now);

        var ownerReserve = (await Get(owner)).Reserve!;
        var otherReserve = (await Get(other)).Reserve!;

        Assert.NotNull(ownerReserve.Jar);
        Assert.Null(otherReserve.Jar);
        Assert.False(otherReserve.CanChooseJar);
    }

    [Fact]
    public async Task Without_a_jar_the_reserve_offers_the_choice_only_while_a_monobank_token_is_connected()
    {
        const int year = 2073;
        var now = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.Zero);
        await using var application = At(now);
        using var client = await ApiFixture.SignIn(application, ApiFixture.SecondAllowedEmail);
        await SetUp(client, year, PaymentMode.Quarterly);
        await using var scope = application.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(ApiFixture.SecondAllowedEmail))!;
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.MonobankConnections.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        await database.ReserveJars.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();

        var disconnected = (await Get(client)).Reserve!;
        database.MonobankConnections.Add(new MonobankConnection
        {
            UserId = user.Id,
            EncryptedToken = [1],
            WebhookSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            ConnectedAt = now,
        });
        await database.SaveChangesAsync();
        var connected = (await Get(client)).Reserve!;
        await database.MonobankConnections.Where(row => row.UserId == user.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.RejectedAt, now));
        var rejected = (await Get(client)).Reserve!;
        await database.MonobankConnections.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();

        Assert.Equal((false, false, false), (disconnected.CanChooseJar, rejected.CanChooseJar, disconnected.Jar is not null));
        Assert.True(connected.CanChooseJar);
        Assert.Null(connected.Jar);
    }

    private static async Task StoreJar(WebApplicationFactory<Program> application, string email, long balanceKop, DateTimeOffset fetchedAt)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.ReserveJars.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        database.ReserveJars.Add(new ReserveJar
        {
            UserId = user.Id,
            JarId = "jar-taxes",
            Title = "На податки",
            BalanceKop = balanceKop,
            FetchedAt = fetchedAt,
        });
        await database.SaveChangesAsync();
    }
}
