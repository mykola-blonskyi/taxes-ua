using System.Collections.Frozen;

namespace TaxesUa.Api.Features.Auth;

internal sealed class EmailAllowlist
{
    private readonly FrozenSet<string> _allowed;

    public EmailAllowlist(IConfiguration configuration) =>
        _allowed = (configuration["Auth:AllowedEmails"] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public bool Permits(string? email) => email is not null && _allowed.Contains(email);
}
