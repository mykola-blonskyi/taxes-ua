using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Tests.Features.Monobank;

// TokenEncryptor.Decrypt has no caller yet (#76 reads the token back to call monobank), but the
// round trip is the whole point of storing an encrypted token at all, so it is proven directly here
// rather than through the HTTP seam.
public sealed class TokenEncryptorTests
{
    private static TokenEncryptor Create() => new(new ConfigurationBuilder()
        .AddInMemoryCollection([new("Monobank:TokenEncryptionKeyBase64", ApiFixture.MonobankTestKeyBase64)])
        .Build());

    [Fact]
    public void Decrypt_recovers_what_encrypt_stored()
    {
        var encryptor = Create();

        var stored = encryptor.Encrypt("a-real-looking-monobank-token");

        Assert.Equal("a-real-looking-monobank-token", encryptor.Decrypt(stored));
    }

    [Fact]
    public void Encrypting_the_same_plaintext_twice_never_produces_the_same_ciphertext()
    {
        var encryptor = Create();

        var first = encryptor.Encrypt("same-token");
        var second = encryptor.Encrypt("same-token");

        Assert.NotEqual(first, second);
        // The nonce is the first 12 bytes; a fresh random one each call is what makes the ciphertexts
        // differ even though the plaintext and key are identical.
        Assert.NotEqual(first[..12], second[..12]);
    }

    [Fact]
    public void A_tampered_ciphertext_is_rejected_instead_of_decrypting_to_garbage()
    {
        var encryptor = Create();
        var stored = encryptor.Encrypt("a-real-looking-monobank-token");
        stored[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() => encryptor.Decrypt(stored));
    }
}
