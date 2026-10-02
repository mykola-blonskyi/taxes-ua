using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api;

/// <summary>
/// Refuses an unsafe request a browser sent from another origin (ADR-024). SameSite=Lax stops other
/// sites, not sibling subdomains of the same site, and several POSTs take no body and so need no CORS
/// preflight. The browser reports where a request came from in headers a page cannot forge:
/// <c>Sec-Fetch-Site</c> when present decides alone, otherwise <c>Origin</c> must be this app's own origin.
/// A request with neither header is let through: every browser that can ride the owner's cookie
/// sends one of them on an unsafe method, so such a request comes from a script or tool, which a web page
/// cannot steer. Must run after UseForwardedHeaders so the scheme and host are the public ones.
/// </summary>
internal static class CrossSiteGuard
{
    public const string ProblemType = "https://taxes-ua/problems/cross-site-request";

    public const string Code = "cross_site_request";

    public static IApplicationBuilder UseCrossSiteGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (IsRefused(context.Request))
            {
                await Results.Problem(
                        title: "Cross-site request refused.",
                        detail: "State-changing requests are accepted only from this app's own pages.",
                        statusCode: StatusCodes.Status403Forbidden,
                        type: ProblemType,
                        extensions: new Dictionary<string, object?> { ["code"] = Code })
                    .ExecuteAsync(context);
                return;
            }

            await next();
        });

    internal static bool IsRefused(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            return false;
        }

        // monobank's server posts here with no browser involved and no cookie: the path secret is the
        // credential (ADR-012). It is the only machine-to-machine route that takes an unsafe method; the
        // calendar feed and the Google callback are GETs, and a passkey sign-in is a page's own POST.
        if (request.Path.StartsWithSegments(MonobankWebhooks.PathPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var site = request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0)
        {
            return !string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase);
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            return !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
