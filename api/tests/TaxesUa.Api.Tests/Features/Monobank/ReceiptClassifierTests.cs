using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class ReceiptClassifierTests
{
    private const string OwnUah = "UA213223130000026007233566001";
    private const string OwnUsd = "UA213223130000026007233566002";
    private const string Stranger = "UA903052992990004149123456789";

    private const int Rate = 41_0000;

    private const long Sold = 1_000_00;

    private const long AtNbu = 41_000_00;

    private static readonly DateTimeOffset Noon = new(2030, 5, 14, 12, 0, 0, TimeSpan.Zero);

    // Credit: account currency, counterparty IBAN, seconds from noon. Debit: account currency and
    // seconds from noon, or none. The credit is the debit's worth at the NBU rate.
    [Theory]
    [InlineData("UAH", Stranger, 0, null, 0, "Income")]
    [InlineData("USD", null, 0, null, 0, "Income")]
    [InlineData("UAH", OwnUsd, 0, null, 0, "OwnTransfer")]
    [InlineData("USD", OwnUah, 0, null, 0, "OwnTransfer")]
    [InlineData("UAH", Stranger, 0, "USD", 0, "FxSale")]
    [InlineData("UAH", Stranger, 0, "EUR", -60, "FxSale")]
    [InlineData("UAH", Stranger, 0, "USD", 60, "FxSale")]
    [InlineData("UAH", OwnUsd, 3, "USD", 0, "FxSale")]
    [InlineData("UAH", Stranger, 0, "USD", 61, "Income")]
    [InlineData("UAH", Stranger, 0, "UAH", 0, "Income")]
    [InlineData("USD", Stranger, 0, "EUR", 0, "Income")]
    public void Suggests_a_kind_for_one_credit(
        string creditCurrency, string? counterIban, int creditSecond, string? debitCurrency, int debitSecond, string expected)
    {
        var credit = new IncomingCredit(
            "c", Enum.Parse<Currency>(creditCurrency), Noon.AddSeconds(creditSecond), AtNbu, counterIban);
        OutgoingDebit[] debits = debitCurrency is null
            ? []
            : [new OutgoingDebit("d", Enum.Parse<Currency>(debitCurrency), Noon.AddSeconds(debitSecond), Sold, Rate)];

        var kinds = ReceiptClassifier.Classify([credit], debits, new HashSet<string> { OwnUah, OwnUsd });

        Assert.Equal(Enum.Parse<TransactionKind>(expected), kinds["c"]);
    }

    // The bank converts at its own rate, which sits within a few percent of the NBU rate.
    [Theory]
    [InlineData(41_000_00L, Rate, "FxSale")]
    [InlineData(38_950_00L, Rate, "FxSale")]
    [InlineData(43_050_00L, Rate, "FxSale")]
    [InlineData(38_949_99L, Rate, "Income")]
    [InlineData(43_050_01L, Rate, "Income")]
    [InlineData(300_00L, Rate, "Income")]
    [InlineData(41_000_00L, null, "Income")]
    public void A_sale_leg_is_worth_the_sold_amount_at_about_the_nbu_rate(long credit, int? rate, string expected)
    {
        var kinds = ReceiptClassifier.Classify(
            [new IncomingCredit("c", Currency.UAH, Noon, credit, Stranger)],
            [new OutgoingDebit("d", Currency.USD, Noon.AddSeconds(2), Sold, rate)],
            new HashSet<string>());

        Assert.Equal(Enum.Parse<TransactionKind>(expected), kinds["c"]);
    }

    [Fact]
    public void A_client_payment_next_to_an_unrelated_card_debit_stays_income()
    {
        IncomingCredit[] credits =
        [
            new("client", Currency.UAH, Noon.AddSeconds(1), 12_345_00, Stranger),
            new("sale", Currency.UAH, Noon.AddSeconds(20), AtNbu, Stranger),
        ];

        var kinds = ReceiptClassifier.Classify(
            credits, [new OutgoingDebit("d", Currency.USD, Noon, Sold, Rate)], new HashSet<string>());

        Assert.Equal(TransactionKind.FxSale, kinds["sale"]);
        Assert.Equal(TransactionKind.Income, kinds["client"]);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-50)]
    public void One_debit_pairs_with_the_closer_of_two_credits(int otherSecond)
    {
        IncomingCredit[] credits =
        [
            new("other", Currency.UAH, Noon.AddSeconds(otherSecond), AtNbu, Stranger),
            new("close", Currency.UAH, Noon.AddSeconds(2), AtNbu, Stranger),
        ];

        var kinds = ReceiptClassifier.Classify(
            credits, [new OutgoingDebit("d", Currency.USD, Noon, Sold, Rate)], new HashSet<string>());

        Assert.Equal(TransactionKind.FxSale, kinds["close"]);
        Assert.Equal(TransactionKind.Income, kinds["other"]);
    }

    // Closest first would take the 25 s pair (0 s, +25 s) and leave the +50 s credit, whose only
    // debit it is, with nothing: one sale instead of two.
    [Fact]
    public void Overlapping_candidates_pair_as_many_sales_as_they_can()
    {
        IncomingCredit[] credits =
        [
            new("at-0", Currency.UAH, Noon, AtNbu, Stranger),
            new("at-50", Currency.UAH, Noon.AddSeconds(50), AtNbu, Stranger),
        ];
        OutgoingDebit[] debits =
        [
            new("at+25", Currency.USD, Noon.AddSeconds(25), Sold, Rate),
            new("at-40", Currency.USD, Noon.AddSeconds(-40), Sold, Rate),
        ];

        var kinds = ReceiptClassifier.Classify(credits, debits, new HashSet<string>());

        Assert.All(kinds.Values, kind => Assert.Equal(TransactionKind.FxSale, kind));
    }

    // The order credits and debits arrive in must not decide who wins, since a window reads whatever
    // order the database gives it.
    [Fact]
    public void The_pairs_do_not_depend_on_the_order_the_legs_arrive_in()
    {
        IncomingCredit[] credits =
        [
            new("at-0", Currency.UAH, Noon, AtNbu, Stranger),
            new("at-50", Currency.UAH, Noon.AddSeconds(50), AtNbu, Stranger),
            new("at+30", Currency.UAH, Noon.AddSeconds(30), AtNbu, Stranger),
        ];
        OutgoingDebit[] debits =
        [
            new("at+25", Currency.USD, Noon.AddSeconds(25), Sold, Rate),
            new("at-40", Currency.USD, Noon.AddSeconds(-40), Sold, Rate),
        ];

        var forward = ReceiptClassifier.Classify(credits, debits, new HashSet<string>());
        var reversed = ReceiptClassifier.Classify([.. credits.Reverse()], [.. debits.Reverse()], new HashSet<string>());

        Assert.Equal(2, forward.Values.Count(kind => kind == TransactionKind.FxSale));
        Assert.Equal(forward.OrderBy(pair => pair.Key), reversed.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Two_sales_in_the_same_minute_pair_one_to_one()
    {
        IncomingCredit[] credits =
        [
            new("usd-leg", Currency.UAH, Noon, AtNbu, Stranger),
            new("eur-leg", Currency.UAH, Noon.AddSeconds(30), 45_000_00, Stranger),
        ];
        OutgoingDebit[] debits =
        [
            new("usd", Currency.USD, Noon.AddSeconds(1), Sold, Rate),
            new("eur", Currency.EUR, Noon.AddSeconds(31), Sold, 45_0000),
        ];

        var kinds = ReceiptClassifier.Classify(credits, debits, new HashSet<string>());

        Assert.All(kinds.Values, kind => Assert.Equal(TransactionKind.FxSale, kind));
    }

    [Theory]
    [InlineData("Income", null)]
    [InlineData("OwnTransfer", "monobank: own transfer")]
    [InlineData("FxSale", "monobank: currency sale")]
    public void A_non_income_suggestion_carries_a_reason(string kind, string? reason) =>
        Assert.Equal(reason, ReceiptClassifier.ReasonFor(Enum.Parse<TransactionKind>(kind)));
}
