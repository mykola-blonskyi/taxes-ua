using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

/// <summary>
/// Turns away a signed-in user who is not an admin (ADR-005). The tax-year parameters are shared by every
/// user, so only a write needs it; a read stays open to any signed-in user.
/// </summary>
internal sealed class AdminOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = await http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>().GetUserAsync(http.User);

        return http.RequestServices.GetRequiredService<EmailAllowlist>().IsAdmin(user?.Email)
            ? await next(context)
            : Problems.Create(
                StatusCodes.Status403Forbidden,
                ProblemCodes.AdminRequired,
                "Only an administrator can change the tax-year parameters.");
    }
}
