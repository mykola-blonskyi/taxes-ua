using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

public static class AuthEndpoints
{
    internal const string EmailVerifiedClaim = "email_verified";

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/login/google", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
                await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null
                    ? Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Google sign-in is not configured on this deployment.")
                    : Results.Challenge(
                        new AuthenticationProperties { RedirectUri = CallbackUrl(returnUrl) },
                        [GoogleDefaults.AuthenticationScheme]))
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        auth.MapGet("/callback", CompleteSignIn)
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Deleting the cookies is the whole of sign-out. The session ticket is self-contained, so a
        // copy taken earlier stays valid until it expires. That is deliberate, see ADR-009.
        auth.MapPost("/logout", async (HttpContext http) =>
            {
                await http.SignOutAsync(IdentityConstants.ApplicationScheme);
                await http.SignOutAsync(IdentityConstants.ExternalScheme);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent);

        auth.MapGet("/me", async (UserManager<ApplicationUser> users, HttpContext http) =>
            {
                var user = await users.GetUserAsync(http.User);
                return user is null
                    ? Results.Unauthorized()
                    : Results.Ok(new MeResponse(user.Id, user.Email ?? string.Empty, user.DisplayName, user.CreatedAt));
            })
            .RequireAuthorization()
            .Produces<MeResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static async Task<IResult> CompleteSignIn(
        string? returnUrl,
        HttpContext http,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        EmailAllowlist allowlist)
    {
        var login = await signInManager.GetExternalLoginInfoAsync();
        if (login is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "External sign-in did not complete. Start again from /api/auth/login/google.");
        }

        var email = login.Principal.FindFirstValue(ClaimTypes.Email);
        if (email is null || !allowlist.Permits(email) || !EmailVerified(login.Principal))
        {
            await http.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This Google account is not allowed to sign in to this application.");
        }

        var user = await userManager.FindByLoginAsync(login.LoginProvider, login.ProviderKey);
        if (user is null)
        {
            user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = login.Principal.FindFirstValue(ClaimTypes.Name),
                    CreatedAt = DateTimeOffset.UtcNow,
                };

                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    return Failed("The account could not be created.", created);
                }
            }

            var linked = await userManager.AddLoginAsync(user, login);
            if (!linked.Succeeded)
            {
                return Failed("The Google login could not be linked to the account.", linked);
            }
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        await http.SignOutAsync(IdentityConstants.ExternalScheme);
        return Results.LocalRedirect(LocalPath(returnUrl));
    }

    // Without this an allowlisted address on a custom domain admits whoever controls a Google
    // Workspace for that domain.
    internal static bool EmailVerified(ClaimsPrincipal principal) =>
        bool.TryParse(principal.FindFirstValue(EmailVerifiedClaim), out var verified) && verified;

    private static IResult Failed(string title, IdentityResult result) => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: title,
        detail: string.Join(" ", result.Errors.Select(error => error.Description)));

    private static string CallbackUrl(string? returnUrl) =>
        $"/api/auth/callback?returnUrl={Uri.EscapeDataString(LocalPath(returnUrl))}";

    private static string LocalPath(string? returnUrl) =>
        returnUrl is ['/', var second, ..]
        && second is not ('/' or '\\')
        // Browsers strip tabs and newlines before parsing a URL, so "/\t/evil.example" reaches the
        // address bar as the protocol-relative "//evil.example". Path shape alone is not enough.
        && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";
}

internal sealed record MeResponse(string Id, string Email, string? DisplayName, DateTimeOffset CreatedAt);
