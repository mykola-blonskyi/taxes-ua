using System.Data.Common;
using Npgsql;

namespace TaxesUa.Api.Data;

/// <summary>
/// The connection string with the limits one owner needs filled in. A key the operator set in
/// DATABASE_URL is left as it is, so any of these can be overridden there (docs/deploy.md).
/// </summary>
internal static class DatabaseConnection
{
    // The app and its few hosted services never need more than a handful of connections at once, and
    // Npgsql's default of 100 would let a stuck caller use up Postgres's own connection limit.
    public const int MaxPoolSize = 10;

    // A statement that runs longer than this is stuck, not slow: the data is one owner's.
    public const int CommandTimeoutSeconds = 60;

    // A statement waiting for a lock (the owner's advisory lock included) gives up with an error
    // instead of queueing behind a stuck holder for good.
    public const int LockTimeoutMilliseconds = 30_000;

    public static string WithDefaults(string? connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // The typed builder knows every key, set or not, so what the operator wrote is read from the
        // canonical string it serializes, which names only the keys that were set (synonyms included).
        var written = new DbConnectionStringBuilder { ConnectionString = builder.ConnectionString };
        if (!written.ContainsKey("Maximum Pool Size"))
        {
            builder.MaxPoolSize = MaxPoolSize;
        }

        if (!written.ContainsKey("Command Timeout"))
        {
            builder.CommandTimeout = CommandTimeoutSeconds;
        }

        if (!written.ContainsKey("Options"))
        {
            builder.Options = $"-c lock_timeout={LockTimeoutMilliseconds}";
        }

        return builder.ConnectionString;
    }
}
