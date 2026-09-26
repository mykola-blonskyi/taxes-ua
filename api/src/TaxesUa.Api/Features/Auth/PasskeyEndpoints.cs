using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

internal static class PasskeyEndpoints
{
    public static IEndpointRouteBuilder MapPasskey(this IEndpointRouteBuilder auth)
    {
        // Both options bodies are the WebAuthn documents navigator.credentials.create() and .get()
        // take verbatim, and Identity hands them over already serialized. `object` leaves the schema
        // open, so `pnpm gen:api` cannot mint a type for a shape this code never parses.
        auth.MapPost("/passkey/register/options", RegisterOptions)
            .RequireAuthorization()
            .Produces<object>(contentType: "application/json")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/passkey/register", Register)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        auth.MapPost("/passkey/login/options", LoginOptions)
            .Produces<object>(contentType: "application/json");

        auth.MapPost("/passkey/login", Login)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return auth;
    }

    private static async Task<IResult> RegisterOptions(
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var name = user.Email ?? user.Id;
        var creationOptions = await signInManager.MakePasskeyCreationOptionsAsync(new()
        {
            Id = user.Id,
            Name = name,
            DisplayName = user.DisplayName ?? name,
        });

        return Results.Content(creationOptions, "application/json");
    }

    private static async Task<IResult> Register(
        PasskeyCredentialSubmission body,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(body.CredentialJson))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No passkey credential was submitted.");
        }

        if ((await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme)).Properties is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No passkey registration is underway. Start again from /api/auth/passkey/register/options.");
        }

        var attestation = await signInManager.PerformPasskeyAttestationAsync(body.CredentialJson);
        if (!attestation.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The passkey could not be registered.",
                detail: attestation.Failure.Message);
        }

        var added = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        if (!added.Succeeded)
        {
            return AuthEndpoints.Failed("The passkey could not be saved.", added);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> LoginOptions(SignInManager<ApplicationUser> signInManager)
    {
        var requestOptions = await signInManager.MakePasskeyRequestOptionsAsync((ApplicationUser?)null);
        return Results.Content(requestOptions, "application/json");
    }

    private static async Task<IResult> Login(
        PasskeyCredentialSubmission body,
        HttpContext http,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        EmailAllowlist allowlist)
    {
        if (string.IsNullOrWhiteSpace(body.CredentialJson))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No passkey credential was submitted.");
        }

        // SignInManager throws instead of returning a failure when the ceremony-state cookie is
        // absent, and that cookie expires after five minutes, so a slow biometric prompt reaches
        // this path.
        if ((await http.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme)).Properties is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "No passkey sign-in is underway. Start again from /api/auth/passkey/login/options.");
        }

        var assertion = await signInManager.PerformPasskeyAssertionAsync(body.CredentialJson);
        if (!assertion.Succeeded)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "The passkey could not be verified.",
                detail: assertion.Failure.Message);
        }

        // SignInManager.PasskeySignInAsync would sign the user in here without consulting the
        // allowlist, which is why this endpoint drives PerformPasskeyAssertionAsync itself. The
        // allowlist is live configuration and nothing revokes a stored credential (ADR-009), so a
        // passkey outlives its email's removal from the list and this is the last gate left.
        if (!allowlist.Permits(assertion.User.Email))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This account is not allowed to sign in to this application.");
        }

        var updated = await userManager.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        if (!updated.Succeeded)
        {
            return AuthEndpoints.Failed("The passkey could not be updated.", updated);
        }

        await signInManager.SignInAsync(assertion.User, isPersistent: true);
        return Results.NoContent();
    }
}

internal sealed record PasskeyCredentialSubmission(string? CredentialJson);
