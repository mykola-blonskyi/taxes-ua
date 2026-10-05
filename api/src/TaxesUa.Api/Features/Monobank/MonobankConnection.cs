using TaxesUa.Api.Features.Banking;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// One owner's monobank personal API token. One connection (and one encrypted token) per owner, not
/// per account (knowledge/domain-model.md, #75). The token is encrypted with AES-256-GCM under a key
/// from the environment (ADR-011), never with the ASP.NET Core Data Protection ring, and is never
/// read back out through the API.
/// </summary>
internal sealed class MonobankConnection
{
    public string UserId { get; set; } = string.Empty;

    // Version byte + nonce (12 bytes) + ciphertext + authentication tag (16 bytes), sealed with UserId
    // as associated data; see TokenEncryptor.
    public byte[] EncryptedToken { get; set; } = [];

    // The monobank clientId the token resolved to at the moment it was saved, kept only to help the
    // owner recognise which token is connected; never a secret.
    public string MonobankClientId { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; }

    // When monobank answered 401 or 403 to a statement call with this token. Every sync of the owner
    // stops until a new token is saved, which clears it.
    public DateTimeOffset? RejectedAt { get; set; }

    // PathSecret.Hash of the path segment of this owner's webhook URL (ADR-012), unique. MonobankWebhooks
    // draws a new secret for every registration, and a token save clears it, so a URL registered for an
    // earlier token stops answering. Null until the first registration of this token.
    public string? WebhookSecretHash { get; set; }

    // The public base URL under which monobank accepted the URL whose secret WebhookSecretHash holds, or
    // null while that URL is not registered. Never the URL itself, which would carry the secret.
    public string? WebhookBaseUrl { get; set; }

    // The last failed registration, kept until one succeeds. Both set or both null.
    public DateTimeOffset? WebhookFailedAt { get; set; }

    public SyncFailure? WebhookFailure { get; set; }
}
