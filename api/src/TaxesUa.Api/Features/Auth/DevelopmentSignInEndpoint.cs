using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

internal static class DevelopmentSignInEndpoint
{
    // SignInManager.GetExternalLoginInfoAsync reads the provider name back out of the
    // AuthenticationProperties item below, and does not require a registered scheme, so this can
    // label the AspNetUserLogins row honestly instead of pretending to be Google.
    private const string LoginProvider = "Development";

    public static IEndpointRouteBuilder MapDevelopmentSignIn(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/auth/login/development", async (string email, string? returnUrl, HttpContext http) =>
            {
                var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, $"development-key-for-{email}"),
                        new Claim(ClaimTypes.Email, email),
                        new Claim(ClaimTypes.Name, email),
                        new Claim(AuthEndpoints.EmailVerifiedClaim, "True"),
                    ],
                    LoginProvider);

                var properties = new AuthenticationProperties();
                properties.Items["LoginProvider"] = LoginProvider;

                await http.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);

                return Results.LocalRedirect(AuthEndpoints.CallbackUrl(returnUrl));
            })
            // `pnpm gen:api` reads the Development document, so describing this route would commit
            // a bypass the web must never call into the generated contract.
            .ExcludeFromDescription();

        return routes;
    }
}
