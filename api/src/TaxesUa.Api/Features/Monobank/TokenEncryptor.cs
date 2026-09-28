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

    public byte[] Encrypt(string plaintext)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("TokenEncryptor is not configured.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[NonceSize + cipherBytes.Length + TagSize];
        nonce.CopyTo(result, 0);
        cipherBytes.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + cipherBytes.Length);
        return result;
    }

    public string Decrypt(byte[] stored)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("TokenEncryptor is not configured.");
        }

        if (stored.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Stored monobank token is too short to be valid.");
        }

        var nonce = stored.AsSpan(0, NonceSize);
        var cipherLength = stored.Length - NonceSize - TagSize;
        var cipherBytes = stored.AsSpan(NonceSize, cipherLength);
        var tag = stored.AsSpan(NonceSize + cipherLength, TagSize);

        var plainBytes = new byte[cipherLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
