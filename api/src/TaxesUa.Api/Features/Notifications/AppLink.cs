namespace TaxesUa.Api.Features.Notifications;

/// <summary>
/// The app's home page as a reminder links to it: <c>App:PublicUrl</c> when set, otherwise the first
/// domain ALLOWED_HOSTS pins, otherwise nothing and the reminder goes without a link. A malformed
/// <c>App:PublicUrl</c> fails startup (Program.cs resolves this eagerly) rather than every reminder.
/// </summary>
internal sealed class AppLink
{
    public AppLink(IConfiguration configuration)
    {
        var configured = configuration["App:PublicUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Url = Parse(configured.Trim())
                ?? throw new InvalidOperationException("App:PublicUrl (APP_PUBLIC_URL) must be an absolute http or https URL.");
            return;
        }

        var host = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        // A wildcard names no address to send the owner to.
        if (host is not null && !host.Contains('*'))
        {
            Url = Parse($"https://{host}/");
        }
    }

    public string? Url { get; }

    private static string? Parse(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/"
            : null;
}
