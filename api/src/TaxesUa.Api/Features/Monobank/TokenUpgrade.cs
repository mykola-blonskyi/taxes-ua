using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Re-encrypts every token still stored as v1 (no associated data) as v2, bound to its owner (#256).
/// Program.cs runs it at startup, after the migrations and before any worker reads a token, so the
/// ciphertext a worker compares rows by never changes under it. A v2 row is left alone, so a second run
/// changes nothing.
/// </summary>
internal static class TokenUpgrade
{
    public static async Task<int> RunAsync(
        AppDbContext database, TokenEncryptor encryptor, ILogger logger, CancellationToken cancellationToken)
    {
        if (!encryptor.IsConfigured)
        {
            return 0;
        }

        var connections = await database.MonobankConnections.AsNoTracking()
            .Select(row => new { row.UserId, row.EncryptedToken })
            .ToListAsync(cancellationToken);
        var upgraded = 0;
        foreach (var connection in connections)
        {
            byte[]? sealedToken;
            try
            {
                sealedToken = encryptor.Upgrade(connection.EncryptedToken, connection.UserId);
            }
            catch (CryptographicException exception)
            {
                logger.LogError(exception, "The monobank token of owner {OwnerId} could not be decrypted.", connection.UserId);
                continue;
            }

            if (sealedToken is null)
            {
                continue;
            }

            // A token saved since the read is already v2 and stays.
            upgraded += await database.MonobankConnections
                .Where(row => row.UserId == connection.UserId && row.EncryptedToken == connection.EncryptedToken)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EncryptedToken, sealedToken), cancellationToken);
        }

        return upgraded;
    }
}
