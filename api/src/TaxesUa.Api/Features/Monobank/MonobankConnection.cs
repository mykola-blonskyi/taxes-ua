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

    // Nonce (12 bytes) + ciphertext + authentication tag (16 bytes), see TokenEncryptor.
    public byte[] EncryptedToken { get; set; } = [];

    // The monobank clientId the token resolved to at the moment it was saved, kept only to help the
    // owner recognise which token is connected; never a secret.
    public string MonobankClientId { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; }

    // When monobank answered 401 or 403 to a statement call with this token. Every sync of the owner
    // stops until a new token is saved, which clears it.
    public DateTimeOffset? RejectedAt { get; set; }

    // The random path segment of this owner's webhook URL (ADR-012), 64 hex characters. A new one is
    // drawn on every token save, so a URL registered for an earlier token stops answering.
    public string WebhookSecret { get; set; } = string.Empty;

    // The URL monobank last accepted for this token, or null when none is registered. It differs from
    // the one MonobankWebhooks wants while a registration is pending or has failed.
    public string? WebhookUrl { get; set; }

    // The last failed registration, kept until one succeeds. Both set or both null.
    public DateTimeOffset? WebhookFailedAt { get; set; }

    public SyncFailure? WebhookFailure { get; set; }
}
