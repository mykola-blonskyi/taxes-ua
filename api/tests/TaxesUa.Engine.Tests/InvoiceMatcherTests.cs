namespace TaxesUa.Engine.Tests;

public class InvoiceMatcherTests
{
    private static readonly Guid Acme = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly OpenInvoice AcmeInvoice =
        new(Acme, "2026-003", "Acme Inc", "USD", 1_500_00, new DateOnly(2026, 7, 10));

    private static ReceiptToMatch Receipt(string currency, long amountMinor, params string?[] texts) =>
        new(currency, amountMinor, texts);

    [Theory]
    [InlineData("Payment for invoice 2026-003", null)]
    [InlineData("INV 2026-003.", "Wire")]
    [InlineData("invoice #2026-003", null)]
    [InlineData("Wire", "ACME   INC")]
    [InlineData("acme inc payment", null)]
    public void The_number_in_the_text_or_the_client_in_the_name_matches(string first, string? second)
    {
        Assert.Equal([Acme], InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, first, second), [AcmeInvoice]));
    }

    [Fact]
    public void The_client_in_the_counterparty_matches_without_the_number()
    {
        Assert.Equal([Acme], InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "Acme Inc", "Transfer"), [AcmeInvoice]));
    }

    [Theory]
    [InlineData("USD", 1_499_99, "2026-003")]
    [InlineData("USD", 1_500_01, "2026-003")]
    [InlineData("EUR", 1_500_00, "2026-003")]
    [InlineData("USD", 1_500_00, "Some other payer")]
    [InlineData("USD", 1_500_00, "2026-0031")]
    [InlineData("USD", 1_500_00, "12026-003")]
    [InlineData("USD", 1_500_00, "")]
    public void A_mismatch_of_amount_currency_or_text_offers_nothing(string currency, long amountMinor, string text)
    {
        Assert.Empty(InvoiceMatcher.Suggest(Receipt(currency, amountMinor, text), [AcmeInvoice]));
    }

    [Fact]
    public void No_text_offers_nothing()
    {
        Assert.Empty(InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, null, "  "), [AcmeInvoice]));
    }

    [Fact]
    public void The_amount_is_what_is_still_due_so_a_partly_paid_invoice_matches_its_remainder()
    {
        var partlyPaid = AcmeInvoice with { OutstandingMinor = 600_00 };

        Assert.Equal([Acme], InvoiceMatcher.Suggest(Receipt("USD", 600_00, "Acme Inc"), [partlyPaid]));
        Assert.Empty(InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "Acme Inc"), [partlyPaid]));
    }

    [Fact]
    public void A_very_short_client_name_does_not_match_inside_words()
    {
        var short1 = AcmeInvoice with { ClientName = "A" };
        var short2 = AcmeInvoice with { ClientName = "Ab" };

        Assert.Empty(InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "A payment from Ab"), [short1, short2]));
    }

    [Fact]
    public void A_client_name_inside_a_longer_word_does_not_match()
    {
        Assert.Empty(InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "Acme Incorporated"), [AcmeInvoice]));
    }

    [Fact]
    public void Cyrillic_names_match_regardless_of_case()
    {
        var invoice = AcmeInvoice with { ClientName = "ТОВ Ромашка" };

        Assert.Equal([Acme], InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "тов РОМАШКА"), [invoice]));
    }

    [Fact]
    public void Several_candidates_are_all_offered_closest_due_date_first()
    {
        var later = new OpenInvoice(Beta, "2026-004", "Acme Inc", "USD", 1_500_00, new DateOnly(2026, 8, 1));
        var sameDay = new OpenInvoice(Guid.Parse("00000000-0000-0000-0000-00000000000c"), "2026-002", "Acme Inc", "USD", 1_500_00, new DateOnly(2026, 7, 10));
        var otherAmount = new OpenInvoice(Guid.NewGuid(), "2026-005", "Acme Inc", "USD", 1_000_00, new DateOnly(2026, 6, 1));

        var offered = InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "Acme Inc"), [later, AcmeInvoice, otherAmount, sameDay]);

        Assert.Equal([sameDay.Id, Acme, Beta], offered);
    }

    [Fact]
    public void An_invoice_matched_by_number_alone_is_offered_beside_one_matched_by_client()
    {
        var byNumber = new OpenInvoice(Beta, "2026-009", "Beta LLC", "USD", 1_500_00, new DateOnly(2026, 7, 1));

        var offered = InvoiceMatcher.Suggest(Receipt("USD", 1_500_00, "Acme Inc", "paying 2026-009"), [AcmeInvoice, byNumber]);

        Assert.Equal([Beta, Acme], offered);
    }
}
