using System.Globalization;

namespace TaxesUa.Engine.Tests;

public class IncomeLedgerTests
{
    [Theory]
    [InlineData("2026-01-31", 1, 1)]
    [InlineData("2026-04-01", 4, 2)]
    [InlineData("2026-09-30", 9, 3)]
    [InlineData("2026-12-31", 12, 4)]
    public void Income_lands_in_the_month_and_the_quarter_of_its_value_date(
        string valueDate,
        int expectedMonth,
        int expectedQuarter)
    {
        var actual = IncomeLedger.ForYear(
            2026, [new TransactionInput.Income(Date(valueDate), 1_000_000)], RegisteredIn2025);

        Assert.Equal(1_000_000, actual.Months[expectedMonth - 1].IncomeKop);
        Assert.Equal(1_000_000, actual.Quarters[expectedQuarter - 1].IncomeKop);
        Assert.Equal(1_000_000, actual.TotalIncomeKop);
    }

    [Fact]
    public void A_refund_reduces_the_quarter_it_happened_in_and_not_the_one_the_money_arrived_in()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 1_000_000),
                new TransactionInput.RefundToClient(Date("2026-05-20"), 400_000),
            ],
            RegisteredIn2025);

        Assert.Equal(
            (1_000_000, 1_000_000, -400_000, 600_000),
            (actual.Quarters[0].IncomeKop,
                actual.Quarters[0].CumulativeIncomeKop,
                actual.Quarters[1].IncomeKop,
                actual.Quarters[1].CumulativeIncomeKop));
    }

    [Fact]
    public void A_refund_larger_than_the_month_income_takes_the_month_below_zero()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-01-15"), 500_000),
                new TransactionInput.RefundToClient(Date("2026-02-20"), 800_000),
            ],
            RegisteredIn2025);

        Assert.Equal(-800_000, actual.Months[1].IncomeKop);
        Assert.Equal(-300_000, actual.Quarters[0].IncomeKop);
        Assert.Equal(-300_000, actual.TotalIncomeKop);
    }

    [Theory]
    [InlineData(NonIncomeKind.OwnTransfer)]
    [InlineData(NonIncomeKind.FxSale)]
    [InlineData(NonIncomeKind.OwnDeposit)]
    [InlineData(NonIncomeKind.ErroneousReturn)]
    [InlineData(NonIncomeKind.Other)]
    public void A_non_income_operation_changes_nothing(NonIncomeKind kind)
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-03-01"), 1_000_000),
                new TransactionInput.NonIncome(Date("2026-03-02"), 999_999, kind, "own funds"),
            ],
            RegisteredIn2025);

        Assert.Equal(1_000_000, actual.TotalIncomeKop);
    }

    [Fact]
    public void An_operation_before_the_registration_date_is_excluded_and_warned_about()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 700_000),
                new TransactionInput.Income(Date("2026-03-20"), 300_000),
            ],
            Settings(Date("2026-03-15")));

        Assert.Equal(300_000, actual.TotalIncomeKop);
        Assert.Equal(
            [new EngineWarning.OperationBeforeRegistration(Date("2026-02-10"), Date("2026-03-15"))],
            actual.Warnings);
    }

    [Fact]
    public void An_operation_on_the_registration_date_itself_counts()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [new TransactionInput.Income(Date("2026-03-15"), 300_000)],
            Settings(Date("2026-03-15")));

        Assert.Equal(300_000, actual.TotalIncomeKop);
        Assert.Empty(actual.Warnings);
    }

    [Fact]
    public void An_operation_from_another_year_stays_out_of_this_year()
    {
        var transactions = new TransactionInput[]
        {
            new TransactionInput.Income(Date("2025-12-31"), 900_000),
            new TransactionInput.Income(Date("2026-01-05"), 100_000),
        };

        var actual = IncomeLedger.ForYear(2026, transactions, RegisteredIn2025);

        Assert.Equal(100_000, actual.TotalIncomeKop);
        Assert.Empty(actual.Warnings);
    }

    [Fact]
    public void A_refund_of_a_receipt_from_the_previous_year_reduces_the_month_it_falls_in()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.RefundToClient(Date("2026-01-20"), 200_000),
                new TransactionInput.Income(Date("2026-02-10"), 500_000),
            ],
            RegisteredIn2025);

        Assert.Equal(
            (-200_000, 500_000, 300_000),
            (actual.Months[0].IncomeKop, actual.Months[1].IncomeKop, actual.TotalIncomeKop));
    }

    [Fact]
    public void Without_a_registration_date_there_is_no_income_and_one_warning_says_why()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-02-10"), 700_000),
                new TransactionInput.Income(Date("2026-03-20"), 300_000),
            ],
            Settings(registrationDate: null));

        Assert.Equal(0, actual.TotalIncomeKop);
        Assert.Equal([new EngineWarning.FopRegistrationDateNotSet()], actual.Warnings);
    }

    [Fact]
    public void Every_month_and_quarter_of_the_year_is_present_and_in_order()
    {
        var actual = IncomeLedger.ForYear(2026, [], RegisteredIn2025);

        Assert.Equal(Enumerable.Range(1, 12), actual.Months.Select(month => month.Month));
        Assert.Equal(Enumerable.Range(1, 4), actual.Quarters.Select(quarter => quarter.Quarter));
    }

    [Fact]
    public void The_cumulative_income_of_a_quarter_is_the_running_sum_of_the_quarters()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-01-10"), 333_333),
                new TransactionInput.Income(Date("2026-05-10"), 777_777),
                new TransactionInput.RefundToClient(Date("2026-08-10"), 900_000),
                new TransactionInput.Income(Date("2026-12-10"), 111_111),
            ],
            RegisteredIn2025);

        Assert.Equal(
            actual.Quarters.Select((_, index) =>
                actual.Quarters.Take(index + 1).Sum(quarter => quarter.IncomeKop)),
            actual.Quarters.Select(quarter => quarter.CumulativeIncomeKop));
    }

    [Fact]
    public void A_quarter_is_the_sum_of_its_three_months()
    {
        var actual = IncomeLedger.ForYear(
            2026,
            [
                new TransactionInput.Income(Date("2026-07-10"), 400_000),
                new TransactionInput.Income(Date("2026-08-10"), 50_000),
                new TransactionInput.RefundToClient(Date("2026-09-10"), 30_000),
            ],
            RegisteredIn2025);

        Assert.Equal(420_000, actual.Quarters[2].IncomeKop);
    }

    [Fact]
    public void A_negative_amount_is_rejected_because_the_kind_carries_the_sign() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TransactionInput.Income(Date("2026-02-10"), -500_000));

    private static readonly FopSettingsInput RegisteredIn2025 = Settings(Date("2025-01-01"));

    private static FopSettingsInput Settings(DateOnly? registrationDate) => new(
        WeekendDays: [DayOfWeek.Saturday, DayOfWeek.Sunday],
        TaxPaymentCountsFromStatutoryDeclarationDate: true,
        ShiftTaxPaymentFromWeekend: true,
        FopRegistrationDate: registrationDate,
        EsvRegistrationMonthPolicy: EsvRegistrationMonthPolicy.FullMonth,
        EsvExempt: false);

    private static DateOnly Date(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
