using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Data;

public sealed class DatabaseConnectionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const string Base = "Host=db;Database=taxes_ua;Username=u;Password=p";

    [Fact]
    public void A_bare_connection_string_gets_the_pool_size_and_the_timeouts()
    {
        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.WithDefaults(Base));

        Assert.Equal(DatabaseConnection.MaxPoolSize, connection.MaxPoolSize);
        Assert.Equal(DatabaseConnection.CommandTimeoutSeconds, connection.CommandTimeout);
        Assert.Equal("-c lock_timeout=30000", connection.Options);
        Assert.Equal(("db", "p"), (connection.Host, connection.Password));
    }

    [Fact]
    public void A_value_the_operator_set_is_kept()
    {
        var connection = new NpgsqlConnectionStringBuilder(DatabaseConnection.WithDefaults(
            Base + ";MaxPoolSize=100;CommandTimeout=5;Options=-c lock_timeout=1000"));

        Assert.Equal((100, 5, "-c lock_timeout=1000"), (connection.MaxPoolSize, connection.CommandTimeout, connection.Options));
    }

    [Fact]
    public async Task The_running_app_talks_to_a_server_that_applies_the_lock_timeout()
    {
        await using var application = fixture.CreateApplication(_ => { });
        await using var scope = application.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var timeout = await database.Database
            .SqlQueryRaw<string>("SELECT current_setting('lock_timeout') AS \"Value\"")
            .SingleAsync();

        Assert.Equal("30s", timeout);
    }
}
