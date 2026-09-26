using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Tests.Features.Auth;

public sealed class GoogleClaimMappingTests
{
    [Theory]
    [InlineData("""{"email":"owner@example.com","email_verified":true}""", true)]
    [InlineData("""{"email":"owner@example.com","email_verified":false}""", false)]
    [InlineData("""{"email":"owner@example.com"}""", false)]
    public void Email_verified_follows_the_google_user_info_document(string userInfo, bool expected)
    {
        var identity = MapGoogleClaims(userInfo);

        Assert.Equal(
            expected,
            AuthEndpoints.EmailVerified(new ClaimsPrincipal(identity)));
    }

    // The mapping in Program.cs and the check in AuthEndpoints agree only because the claim action
    // renders a JSON boolean as a string bool.TryParse accepts. Pin that here, since a live Google
    // sign-in is out of reach of the test host.
    private static ClaimsIdentity MapGoogleClaims(string userInfo)
    {
        var options = new GoogleOptions();
        options.ClaimActions.MapJsonKey(AuthEndpoints.EmailVerifiedClaim, "email_verified");

        var identity = new ClaimsIdentity();
        using var document = JsonDocument.Parse(userInfo);
        foreach (var action in options.ClaimActions)
        {
            action.Run(document.RootElement, identity, GoogleDefaults.AuthenticationScheme);
        }

        return identity;
    }
}
