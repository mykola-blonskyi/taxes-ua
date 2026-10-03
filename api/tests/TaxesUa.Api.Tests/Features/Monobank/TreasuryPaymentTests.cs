using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Monobank;

public sealed class TreasuryPaymentTests
{
    private const string BudgetAccount = "UA358999980333159998000026011";

    // A Kyiv military levy account of the set issued from 1 July 2026.
    private const string LevyAccount2026 = "UA148999980313181000026007233";

    private const string EsvAccount = "UA538999980000355689990000001";

    private const string ClientAccount = "UA753220010000026001234567891";

    [Theory]
    [InlineData(BudgetAccount, true)]
    [InlineData(EsvAccount, true)]
    [InlineData("ua35 8999 9803 3315 9998 0000 2601 1", true)]
    [InlineData(ClientAccount, false)]
    [InlineData("UA35899998033315999800002601", false)]
    [InlineData("PL35899998033315999800002601", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_Treasury_account_carries_bank_id_899998(string? iban, bool treasury) =>
        Assert.Equal(treasury, TreasuryPayment.IsTreasury(iban));

    public static TheoryData<string, string?, PaymentKind?, PaymentKind?> Suggestions => new()
    {
        { BudgetAccount, "*;101;1234567890;Єдиний податок з фізичних осіб;;;", null, PaymentKind.SingleTax },
        { BudgetAccount, "*;101;1234567890;військовий збір;;;", null, PaymentKind.MilitaryLevy },
        { BudgetAccount, "101;ЄП за 3 квартал 2026", null, PaymentKind.SingleTax },
        { LevyAccount2026, "Сплата військового збору за III квартал 2026 року", null, PaymentKind.MilitaryLevy },
        { LevyAccount2026, "ВЗ ІІІ кв", null, PaymentKind.MilitaryLevy },
        { LevyAccount2026, "*;101;1234567890;11011800;;;", null, PaymentKind.MilitaryLevy },
        { BudgetAccount, "Єдиний внесок на загальнообов'язкове державне соціальне страхування", null, PaymentKind.Esv },
        { BudgetAccount, "єдиний соціальний внесок", null, PaymentKind.Esv },
        { BudgetAccount, "ЄСВ за вересень", null, PaymentKind.Esv },
        { EsvAccount, "Сплата внеску", null, PaymentKind.Esv },
        { EsvAccount, null, null, PaymentKind.Esv },
        { EsvAccount, "військовий збір", null, PaymentKind.MilitaryLevy },
        { BudgetAccount, "Єдиний податок та військовий збір", null, null },
        { EsvAccount, "Єдиний податок та військовий збір", null, null },
        { EsvAccount, "ЄСВ і ВЗ", null, null },
        { EsvAccount, "ЄСВ і ВЗ", PaymentKind.SingleTax, PaymentKind.SingleTax },
        { BudgetAccount, "Оплата за рахунком 17", null, null },
        { BudgetAccount, null, null, null },
        { BudgetAccount, "Єдиний податок", PaymentKind.Esv, PaymentKind.Esv },
        { BudgetAccount, "вз", null, PaymentKind.MilitaryLevy },
        { BudgetAccount, "звз 2026", null, null },
    };

    [Theory]
    [MemberData(nameof(Suggestions))]
    public void The_owners_last_choice_then_the_purpose_then_the_ESV_account_suggest_the_kind(
        string iban, string? purpose, PaymentKind? learned, PaymentKind? expected) =>
        Assert.Equal(expected, TreasuryPayment.Suggest(iban, purpose, learned));
}
