using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class TokenEncryptorTests
{
    private const string Owner = "owner-id";

    private static TokenEncryptor Create() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new("Monobank:TokenEncryptionKeyBase64", ApiFixture.MonobankTestKeyBase64)])
        .Build());

    [Fact]
    public void Decrypt_recovers_what_encrypt_stored()
    {
        var encryptor = Create();

        var stored = encryptor.Encrypt("a-real-looking-monobank-token", Owner);

        Assert.Equal("a-real-looking-monobank-token", encryptor.Decrypt(stored, Owner));
    }

    [Fact]
    public void Encrypting_the_same_plaintext_twice_never_produces_the_same_ciphertext()
    {
        var encryptor = Create();

        var first = encryptor.Encrypt("same-token", Owner);
        var second = encryptor.Encrypt("same-token", Owner);

        Assert.NotEqual(first, second);
        // The nonce follows the version byte; a fresh random one each call is what makes the ciphertexts
        // differ even though the plaintext and key are identical.
        Assert.NotEqual(first[1..13], second[1..13]);
    }

    [Fact]
    public void A_tampered_ciphertext_is_rejected_instead_of_decrypting_to_garbage()
    {
        var encryptor = Create();
        var stored = encryptor.Encrypt("a-real-looking-monobank-token", Owner);
        stored[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(stored, Owner));
    }

    [Fact]
    public void A_ciphertext_moved_to_another_owner_does_not_decrypt()
    {
        var encryptor = Create();
        var stored = encryptor.Encrypt("a-real-looking-monobank-token", Owner);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(stored, "another-owner-id"));
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Upgrade(stored, "another-owner-id"));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x02)]
    public void A_token_stored_before_owner_binding_still_decrypts_and_upgrades_to_one_bound_to_its_owner(byte firstNonceByte)
    {
        var encryptor = Create();
        var legacy = LegacyEncrypt("a-real-looking-monobank-token", firstNonceByte);

        Assert.Equal("a-real-looking-monobank-token", encryptor.Decrypt(legacy, Owner));
        var upgraded = encryptor.Upgrade(legacy, Owner)!;

        Assert.Equal("a-real-looking-monobank-token", encryptor.Decrypt(upgraded, Owner));
        Assert.Null(encryptor.Upgrade(upgraded, Owner));
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(upgraded, "another-owner-id"));
    }

    // The format written before #256: nonce, ciphertext and tag, with no version byte and no associated data.
    private static byte[] LegacyEncrypt(string plaintext, byte firstNonceByte)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        nonce[0] = firstNonceByte;
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var stored = new byte[12 + plain.Length + 16];
        nonce.CopyTo(stored, 0);
        using var aes = new AesGcm(Convert.FromBase64String(ApiFixture.MonobankTestKeyBase64), 16);
        aes.Encrypt(nonce, plain, stored.AsSpan(12, plain.Length), stored.AsSpan(12 + plain.Length, 16));
        return stored;
    }
}
