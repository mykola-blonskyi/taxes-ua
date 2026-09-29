using System.Text.RegularExpressions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// Recognises a payment to the State Treasury and suggests its kind (Rule 12). The kind is not in the
/// IBAN, and regional accounts are re-issued, so there is no national table: the owner's own last
/// confirmation for the account wins, then the purpose text, then the ESV account hint.
/// </summary>
internal static partial class TreasuryPayment
{
    // The NBU bank id every Treasury account carries at IBAN positions 5 to 10.
    private const string TreasuryBankId = "899998";

    // ESV is paid to non-budget balance account 3556 of the regional tax office, budget taxes to budget
    // accounts; seen in a few published examples only, so it is the last resort.
    private const string EsvAccountPrefix = "00003556";

    private const int UkrainianIbanLength = 29;

    // Checked in full against the purpose text; a text naming more than one kind suggests none.
    private static readonly (Regex Pattern, PaymentKind Kind)[] Keywords =
    [
        (Esv(), PaymentKind.Esv),
        (MilitaryLevy(), PaymentKind.MilitaryLevy),
        (SingleTax(), PaymentKind.SingleTax),
    ];

    /// <summary>The IBAN in the form it is stored and compared in, or null when there is none.</summary>
    public static string? Normalize(string? iban)
    {
        var compact = iban?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return string.IsNullOrEmpty(compact) ? null : compact;
    }

    public static bool IsTreasury(string? iban) =>
        Normalize(iban) is { Length: UkrainianIbanLength } normalized
        && normalized.StartsWith("UA", StringComparison.Ordinal)
        && normalized.AsSpan(4, TreasuryBankId.Length).SequenceEqual(TreasuryBankId);

    public static PaymentKind? Suggest(string iban, string? purpose, PaymentKind? learned) =>
        learned ?? FromPurpose(purpose) ?? FromAccount(iban);

    private static PaymentKind? FromPurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return null;
        }

        var named = Keywords.Where(keyword => keyword.Pattern.IsMatch(purpose)).Select(keyword => keyword.Kind).ToArray();
        return named is [var kind] ? kind : null;
    }

    private static PaymentKind? FromAccount(string iban) =>
        Normalize(iban) is { Length: UkrainianIbanLength } normalized
        && normalized.AsSpan(10).StartsWith(EsvAccountPrefix)
            ? PaymentKind.Esv
            : null;

    // Budget classification code 71040000 is ESV.
    [GeneratedRegex(@"\bЄСВ\b|єдин\w*\s+(соціальн\w*\s+)?внес|\b71040000\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Esv();

    // 11011000, and 11011700 and 11011800 since the accounts changed on 1 July 2026.
    [GeneratedRegex(@"\bВЗ\b|військов\w*\s+зб[іо]р|\b1101(1000|1700|1800)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MilitaryLevy();

    // 18050400 is the single tax of individual entrepreneurs.
    [GeneratedRegex(@"\bЄП\b|єдин\w*\s+подат|\b18050400\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SingleTax();
}
