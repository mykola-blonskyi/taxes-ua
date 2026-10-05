using System.Security.Cryptography;
using System.Text;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// AES-256-GCM encryption for the monobank personal API token, under a 32-byte key read once from
/// configuration (ADR-011). Deliberately not the ASP.NET Core Data Protection key ring: ADR-009
/// rotates that ring on purpose to end every session, and that rotation must not also destroy a
/// stored bank token.
/// </summary>
internal sealed class TokenEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const byte Version2 = 2;

    private readonly byte[]? _key;

    public TokenEncryptor(IConfiguration configuration)
    {
        var configured = configuration["Monobank:TokenEncryptionKeyBase64"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            // Absent on purpose (a local run, or a deployment that has not set it up yet): the app
            // still starts, and IsConfigured tells the endpoints to answer "not configured" instead.
            _key = null;
            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Monobank:TokenEncryptionKeyBase64 (MONOBANK_TOKEN_ENCRYPTION_KEY) is set but is not "
                + "valid base64.",
                exception);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                "Monobank:TokenEncryptionKeyBase64 (MONOBANK_TOKEN_ENCRYPTION_KEY) must decode to "
                + $"exactly 32 bytes for AES-256, got {key.Length}.");
        }

        _key = key;
    }

    public bool IsConfigured => _key is not null;

    // Every write is v2: the version byte, then nonce, ciphertext and tag, with the owner's user id as
    // associated data, so a ciphertext copied onto another owner's row fails to decrypt (#256).
    public byte[] Encrypt(string plaintext, string userId)
    {
        var key = Key();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[1 + NonceSize + plainBytes.Length + TagSize];
        result[0] = Version2;
        nonce.CopyTo(result, 1);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            nonce,
            plainBytes,
            result.AsSpan(1 + NonceSize, plainBytes.Length),
            result.AsSpan(1 + NonceSize + plainBytes.Length, TagSize),
            Encoding.UTF8.GetBytes(userId));
        return result;
    }

    public string Decrypt(byte[] stored, string userId) => Open(stored, userId).Token;

    /// <summary>The v2 ciphertext of a token stored as v1, or null when it already is v2.</summary>
    public byte[]? Upgrade(byte[] stored, string userId)
    {
        var (token, legacy) = Open(stored, userId);
        return legacy ? Encrypt(token, userId) : null;
    }

    private (string Token, bool Legacy) Open(byte[] stored, string userId)
    {
        var key = Key();
        if (stored.Length >= 1 + NonceSize + TagSize && stored[0] == Version2)
        {
            try
            {
                return (Open(key, stored.AsSpan(1), Encoding.UTF8.GetBytes(userId)), false);
            }
            catch (CryptographicException)
            {
                // A v1 ciphertext whose random nonce happens to start with the version byte.
            }
        }

        if (stored.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Stored monobank token is too short to be valid.");
        }

        // v1, written before #256: nonce, ciphertext and tag with no associated data.
        return (Open(key, stored, []), true);
    }

    private static string Open(byte[] key, ReadOnlySpan<byte> sealedToken, ReadOnlySpan<byte> associatedData)
    {
        var cipherLength = sealedToken.Length - NonceSize - TagSize;
        var plainBytes = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            sealedToken[..NonceSize],
            sealedToken.Slice(NonceSize, cipherLength),
            sealedToken.Slice(NonceSize + cipherLength, TagSize),
            plainBytes,
            associatedData);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private byte[] Key() => _key ?? throw new InvalidOperationException("TokenEncryptor is not configured.");
}
