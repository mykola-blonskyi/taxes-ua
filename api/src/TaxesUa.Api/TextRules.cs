using System.Text.RegularExpressions;
using MimeKit;

namespace TaxesUa.Api;

/// <summary>
/// The one place every free-text field is checked for characters that have no legitimate place in
/// one, and an email address for its form, so a request and a restored backup enforce the same rule
/// instead of each reimplementing it.
/// </summary>
internal static partial class TextRules
{
    /// <summary>
    /// True if <paramref name="value"/> contains a NUL or another disallowed control character (C0,
    /// DEL or C1).
    /// PostgreSQL's <c>text</c> columns reject an embedded NUL outright, which without this check
    /// surfaces as a 500 once the row is saved rather than a 400 the caller can act on. The rest of
    /// C0 (U+0001-U+001F, e.g. backspace, form feed, escape) is rejected too: none of them has a
    /// legitimate reason to appear in a name, note or description, and letting them through risks
    /// corrupting the CSV/XLSX export and PDF rendering that read these fields as plain text. Tab,
    /// LF and CR are kept, since a pasted spreadsheet cell or a multi-line description is legitimate
    /// input.
    /// </summary>
    public static bool HasDisallowedControlChar(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) && c is not '\t' and not '\n' and not '\r')
            {
                return true;
            }
        }

        return false;
    }

    // A mailbox that is only an address: no name, no angle brackets, no list, no control characters.
    public static bool TryNormalizeEmail(string? input, out string address)
    {
        address = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 254 || !PlainAddress().IsMatch(trimmed) || !MailboxAddress.TryParse(trimmed, out var parsed)
            || parsed is not { } mailbox || mailbox.Address != trimmed)
        {
            return false;
        }

        address = trimmed;

        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$")]
    private static partial Regex PlainAddress();
}
