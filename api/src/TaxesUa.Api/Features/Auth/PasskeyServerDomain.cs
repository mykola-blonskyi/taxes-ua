using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace TaxesUa.Api.Features.Auth;

internal static class PasskeyServerDomain
{
    public static IServiceCollection AddPasskeys(this IServiceCollection services, IConfiguration configuration)
    {
        var serverDomain = configuration["Auth:Passkey:ServerDomain"];

        if (string.IsNullOrWhiteSpace(serverDomain))
        {
            throw new InvalidOperationException(
                "PASSKEY_SERVER_DOMAIN must name the WebAuthn Relying Party ID, the bare domain with "
                + "no scheme and no port, which reaches this process as Auth__Passkey__ServerDomain. "
                + "A passkey is bound to the RP ID it was registered against and does not work "
                + "against another, so a missing value would silently break every passkey. Use "
                + "docker-compose.local.yml for a local run.");
        }

        services.Configure<IdentityPasskeyOptions>(options => options.ServerDomain = serverDomain);

        // The passkey ceremony state is the only thing riding IdentityConstants.TwoFactorUserIdScheme,
        // and AddTwoFactorUserIdCookie leaves it SameAsRequest, but Traefik forwards plain http to
        // this container, so the default policy would let that cookie ride an unencrypted hop. Same
        // defect ConfigureExternalCookie already fixes for the external cookie.
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.TwoFactorUserIdScheme,
            options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);

        return services;
    }
}
