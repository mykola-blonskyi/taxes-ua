using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

internal sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
