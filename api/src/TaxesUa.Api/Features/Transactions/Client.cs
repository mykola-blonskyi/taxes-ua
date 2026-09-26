namespace TaxesUa.Api.Features.Transactions;

internal sealed class Client
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
