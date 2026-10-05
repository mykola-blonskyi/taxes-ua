using System.Security.Cryptography;
using System.Text;

namespace TaxesUa.Api;

/// <summary>
/// A secret carried in a URL path: the calendar feed's (ADR-017) and the monobank webhook's (ADR-012).
/// It is 32 random bytes as 64 lowercase hex characters, and only its SHA-256 is stored (#256), so a
/// database dump yields no working URL. A request is matched by hashing its path segment and looking
/// the hash up.
/// </summary>
internal static class PathSecret
{
    public const int HashLength = 64;

    public static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
