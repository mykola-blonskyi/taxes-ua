using System.Buffers.Text;
using System.Text;

namespace TaxesUa.Engine;

/// <summary>
/// The content of an NBU credit-transfer QR code, format 003 (NBU Resolution No. 97 of 19.08.2025 as
/// amended by No. 128, Appendix 4): the start code followed by the Base64URL of seventeen LF-separated
/// fields in UTF-8. Rule 16.
/// </summary>
public static class NbuQr
{
    public const string StartCode = "https://qr.bank.gov.ua/";

    /// <summary>
    /// ISO 20022 category and purpose, both "Tax Payment". The resolution's only example is SUPP/SUPP;
    /// which code banks expect for a budget transfer is unconfirmed until the owner scans one (#100).
    /// </summary>
    public const string CategoryPurpose = "TAXS/TAXS";

    /// <summary>Every field locked except the amount, the resolution's own example of this mask.</summary>
    public const string OnlyAmountEditable = "FEFF";

    public const int MaxBytes = 507;

    /// <summary>Appendix 4, table 1: the Base64URL element on its own is capped at 475 bytes.</summary>
    public const int MaxEncodedBytes = 475;

    public const long MaxAmountKop = 99_999_999_999;

    private const int MaxRecipientNameChars = 140;

    private const int MaxRecipientCodeChars = 10;

    private const int MaxPurposeChars = 420;

    /// <summary>
    /// The QR content for a credit transfer, or null when the details cannot be carried by format 003:
    /// a field over its length, a control character, an amount outside 0.01..999 999 999.99, or an
    /// encoded block over 475 bytes (which also keeps the whole link under 507).
    /// </summary>
    public static string? Content(string recipientName, string iban, string recipientCode, long amountKop, string purpose)
    {
        if (recipientName.Length is 0 or > MaxRecipientNameChars
            || iban.Length != 29
            || recipientCode.Length is 0 or > MaxRecipientCodeChars
            || purpose.Length is 0 or > MaxPurposeChars
            || amountKop is <= 0 or > MaxAmountKop
            || new[] { recipientName, iban, recipientCode, purpose }.Any(field => field.Any(char.IsControl)))
        {
            return null;
        }

        string[] fields =
        [
            "BCD",
            "003",
            "1",
            "UCT",
            "",
            recipientName,
            iban,
            Amount(amountKop),
            recipientCode,
            CategoryPurpose,
            "",
            purpose,
            "",
            OnlyAmountEditable,
            "",
            "",
            "",
        ];
        var encoded = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join('\n', fields)));
        return encoded.Length <= MaxEncodedBytes ? StartCode + encoded : null;
    }

    /// <summary>
    /// "UAH" and the shortest form the resolution allows: no fraction for whole hryvnias, otherwise
    /// exactly two digits after the point.
    /// </summary>
    public static string Amount(long amountKop)
    {
        var (hryvnias, kopecks) = Math.DivRem(amountKop, 100);
        return kopecks == 0 ? $"UAH{hryvnias}" : $"UAH{hryvnias}.{kopecks:00}";
    }
}
