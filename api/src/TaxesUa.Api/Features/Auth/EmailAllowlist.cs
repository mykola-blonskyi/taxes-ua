using System.Collections.Frozen;

namespace TaxesUa.Api.Features.Auth;

internal sealed class EmailAllowlist
{
    private readonly FrozenSet<string> _allowed;

    private readonly FrozenSet<string> _admins;

    public EmailAllowlist(IConfiguration configuration)
    {
        var allowed = Split(configuration["Auth:AllowedEmails"]);
        _allowed = allowed.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        // Unset means the first allowlisted address: a single-owner deployment needs no second variable
        // (ADR-005, 2026-10-05).
        var admins = Split(configuration["Auth:AdminEmails"]);
        _admins = (admins.Length > 0 ? admins : allowed.Take(1)).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool Permits(string? email) => email is not null && _allowed.Contains(email);

    // An admin is also on the allowlist: an address removed from it loses its admin rights with its access.
    public bool IsAdmin(string? email) => Permits(email) && _admins.Contains(email!);

    private static string[] Split(string? configured) => (configured ?? string.Empty)
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
