using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace TaxesUa.Api.Features.Notifications;

internal enum ConfirmationCheck
{
    Valid,
    Invalid,
    Expired,
}

/// <summary>
/// The signed, expiring token in the confirmation link (ADR-022). It is the owner, the address and
/// the moment it stops working, authenticated and encrypted with the data-protection key ring, so a
/// changed character, another key ring or another purpose fails to open it. Expiry is compared with
/// the app's clock rather than the protector's, which keeps it testable. The token carries the
/// address, so asking for a link for a new address cannot be answered by an older link, and nothing
/// about it is stored.
/// </summary>
internal sealed class EmailConfirmation(IDataProtectionProvider protection, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private const string Purpose = "taxes-ua.email-confirmation.v1";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    public (string Token, DateTimeOffset ExpiresAt) Issue(string userId, string address)
    {
        var expiresAt = time.GetUtcNow() + Lifetime;
        var payload = string.Join('|', userId, address.ToLowerInvariant(), expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        return (_protector.Protect(payload).Replace('+', '-').Replace('/', '_').TrimEnd('='), expiresAt);
    }

    // The address inside is returned so the caller can tell a link for an address since replaced.
    public (ConfirmationCheck Check, string? Address) Read(string token, string userId)
    {
        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var parts = _protector.Unprotect(padded).Split('|');
            if (parts.Length != 3
                || parts[0] != userId
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
            {
                return (ConfirmationCheck.Invalid, null);
            }

            return DateTimeOffset.FromUnixTimeSeconds(expires) > time.GetUtcNow()
                ? (ConfirmationCheck.Valid, parts[1])
                : (ConfirmationCheck.Expired, parts[1]);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        {
            return (ConfirmationCheck.Invalid, null);
        }
    }
}
