using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The deep link that ties a Telegram chat to an owner. The owner asks for a code in settings and
/// opens <c>https://t.me/&lt;bot&gt;?start=&lt;code&gt;</c>; pressing Start makes Telegram send the bot
/// <c>/start &lt;code&gt;</c>, which the poller matches here. A code works once, for
/// <see cref="Lifetime"/>, and is stored only as a hash.
/// </summary>
internal sealed class TelegramLinking(AppDbContext database, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    // 24 random bytes are 32 base64url characters, inside Telegram's 64 of [A-Za-z0-9_-] for a start parameter.
    private const int CodeBytes = 24;

    public async Task<(string Code, DateTimeOffset ExpiresAt)> IssueAsync(string userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // One live code per owner, and no dead ones piling up.
        await database.NotificationLinkCodes
            .Where(row => row.UserId == userId || row.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var code = Base64Url(RandomNumberGenerator.GetBytes(CodeBytes));
        var expiresAt = now + Lifetime;
        database.NotificationLinkCodes.Add(new NotificationLinkCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = NotificationChannelKind.Telegram,
            CodeHash = Hash(code),
            ExpiresAt = expiresAt,
        });
        await database.SaveChangesAsync(cancellationToken);

        return (code, expiresAt);
    }

    // Consumes the code whatever the outcome, so a code is never tried twice. Returns the owner it
    // belongs to, or null for an unknown, used or expired code. The caller saves.
    public async Task<string?> RedeemAsync(string code, CancellationToken cancellationToken)
    {
        var hash = Hash(code);
        var row = await database.NotificationLinkCodes.FirstOrDefaultAsync(found => found.CodeHash == hash, cancellationToken);
        if (row is null)
        {
            return null;
        }

        database.NotificationLinkCodes.Remove(row);

        return row.ExpiresAt > time.GetUtcNow() ? row.UserId : null;
    }

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
