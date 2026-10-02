using System.Net.Mail;
using System.Text.Json;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Transactions;

/// <summary>
/// What a client's details must satisfy, checked by the Clients endpoints and by a restored backup
/// alike, so a file cannot store what the screen would refuse.
/// </summary>
internal sealed record ClientRequest(
    string Name,
    string? Address,
    string? Country,
    string? VatId,
    string? Email,
    Currency? DefaultCurrency,
    string? Notes)
{
    /// <summary>
    /// Trimmed, with an empty optional field stored as null and the country in upper case. Names are
    /// compared exactly after the trim, as a receipt's client name always has been.
    /// </summary>
    public ClientRequest Normalized() => new(
        Name?.Trim() ?? string.Empty,
        Optional(Address),
        Optional(Country)?.ToUpperInvariant(),
        Optional(VatId),
        Optional(Email),
        DefaultCurrency,
        Optional(Notes));

    public void ApplyTo(Client client)
    {
        client.Name = Name;
        client.Address = Address;
        client.Country = Country;
        client.VatId = VatId;
        client.Email = Email;
        client.DefaultCurrency = DefaultCurrency;
        client.Notes = Notes;
    }

    private static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

internal static class ClientRules
{
    public const int MaxNameLength = TransactionsEndpoints.MaxClientNameLength;

    public const int MaxAddressLength = 500;

    public const int MaxVatIdLength = 50;

    public const int MaxEmailLength = 254;

    public const int MaxNotesLength = 2000;

    /// <summary>The errors of an already <see cref="ClientRequest.Normalized"/> request, keyed by camelCase field.</summary>
    public static FieldErrors? Validate(ClientRequest client)
    {
        var errors = new FieldErrors();

        Text(errors, nameof(ClientRequest.Name), client.Name, MaxNameLength, required: true);
        Text(errors, nameof(ClientRequest.Address), client.Address, MaxAddressLength);
        Text(errors, nameof(ClientRequest.VatId), client.VatId, MaxVatIdLength);
        Text(errors, nameof(ClientRequest.Notes), client.Notes, MaxNotesLength);

        if (client.Country is { } country && !Iso3166Alpha2.Contains(country))
        {
            errors.Set(
                Field(nameof(ClientRequest.Country)),
                ProblemCodes.CountryInvalid,
                "country must be an ISO 3166-1 alpha-2 code such as UA, US or DE.");
        }

        if (client.Email is { } email)
        {
            if (email.Length > MaxEmailLength)
            {
                errors.Set(
                    Field(nameof(ClientRequest.Email)),
                    ProblemCodes.TooLong,
                    $"email must not exceed {MaxEmailLength} characters.");
            }
            else if (!IsEmail(email))
            {
                errors.Set(
                    Field(nameof(ClientRequest.Email)),
                    ProblemCodes.EmailInvalid,
                    "email must be an address such as name@example.com.");
            }
        }

        return errors.OrNull();
    }

    private static void Text(
        FieldErrors errors, string member, string? value, int maxLength, bool required = false)
    {
        var field = Field(member);
        if (string.IsNullOrEmpty(value))
        {
            if (required)
            {
                errors.Set(field, ProblemCodes.Required, $"{field} must be 1 to {maxLength} characters.");
            }
        }
        else if (value.Length > maxLength)
        {
            errors.Set(
                field,
                ProblemCodes.TooLong,
                required
                    ? $"{field} must be 1 to {maxLength} characters."
                    : $"{field} must not exceed {maxLength} characters.");
        }
        else if (TextRules.HasDisallowedControlChar(value))
        {
            errors.Set(
                field,
                ProblemCodes.ControlCharacter,
                $"{field} must not contain a NUL or other control character (tab, line feed and carriage return are allowed).");
        }
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value
        && parsed.DisplayName.Length == 0
        && parsed.Host.Contains('.', StringComparison.Ordinal);

    private static string Field(string member) => JsonNamingPolicy.CamelCase.ConvertName(member);

    private static readonly HashSet<string> Iso3166Alpha2 = new(StringComparer.Ordinal)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS",
        "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN",
        "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE",
        "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF",
        "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM",
        "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM",
        "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC",
        "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK",
        "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA",
        "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG",
        "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS",
        "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO",
        "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
        "VN", "VU", "WF", "WS", "YE", "YT", "ZA", "ZM", "ZW",
    };
}
