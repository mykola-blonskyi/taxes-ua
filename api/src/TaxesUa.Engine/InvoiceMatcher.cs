namespace TaxesUa.Engine;

/// <summary>An imported receipt as the matcher sees it: its currency, amount and the bank's free text.</summary>
public sealed record ReceiptToMatch(string Currency, long AmountMinor, IReadOnlyList<string?> Texts);

/// <summary>An issued, unpaid invoice that a receipt could pay; <c>OutstandingMinor</c> is what is still due.</summary>
public sealed record OpenInvoice(Guid Id, string Number, string ClientName, string Currency, long OutstandingMinor, DateOnly DueDate);

/// <summary>
/// Which open invoices an imported receipt may be paying (Rule 14): the same currency, an amount equal to
/// the invoice's outstanding amount, and the invoice number or the client's name in the bank's text. The
/// result is only an offer; the owner's confirmation is what links.
/// </summary>
public static class InvoiceMatcher
{
    /// <summary>A shorter client name would match inside ordinary words and offer unrelated invoices.</summary>
    public const int MinClientNameLength = 3;

    /// <summary>The ids of the matching invoices, closest due date first, then by number.</summary>
    public static IReadOnlyList<Guid> Suggest(ReceiptToMatch receipt, IEnumerable<OpenInvoice> invoices)
    {
        var text = Normalize(string.Join(' ', receipt.Texts.Where(part => !string.IsNullOrWhiteSpace(part))));
        if (text.Length == 0)
        {
            return [];
        }

        return [.. invoices
            .Where(invoice => invoice.Currency == receipt.Currency
                && invoice.OutstandingMinor == receipt.AmountMinor
                && (ContainsWord(text, Normalize(invoice.Number)) || NamesClient(text, invoice.ClientName)))
            .OrderBy(invoice => invoice.DueDate)
            .ThenBy(invoice => invoice.Number, StringComparer.Ordinal)
            .Select(invoice => invoice.Id)];
    }

    private static bool NamesClient(string text, string clientName)
    {
        var name = Normalize(clientName);

        return name.Length >= MinClientNameLength && ContainsWord(text, name);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    // A match inside a longer word or number ("2026-0031" for "2026-003") is another thing, not this one.
    private static bool ContainsWord(string text, string needle)
    {
        if (needle.Length == 0)
        {
            return false;
        }

        for (var from = 0; from <= text.Length - needle.Length;)
        {
            var at = text.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var end = at + needle.Length;
            if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }
}
