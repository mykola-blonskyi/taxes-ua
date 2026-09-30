using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Transactions;

internal sealed class Client
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    /// <summary>ISO 3166-1 alpha-2, upper case.</summary>
    public string? Country { get; set; }

    public string? VatId { get; set; }

    public string? Email { get; set; }

    public Currency? DefaultCurrency { get; set; }

    public string? Notes { get; set; }
}
