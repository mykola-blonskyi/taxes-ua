using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Backup;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SettingsBackup(
    DateOnly? FopRegistrationDate,
    PaymentMode PaymentMode,
    EsvRegistrationMonthPolicy EsvRegistrationMonthPolicy,
    bool EsvExempt,
    bool TaxPaymentCountsFromStatutoryDeclarationDate,
    bool ShiftTaxPaymentFromWeekend,
    DayOfWeek[] WeekendDays,
    string Locale,
    string Theme,
    string DefaultCurrency,
    YearQuarter? BackOnGroup3From,
    DateOnly? Group3Since,
    Group3ConfirmationBackup? Group3Confirmation,
    bool DpsFopRegistered,
    bool DpsEsvRegistered,
    bool DpsAccountsRegistered)
{
    public static SettingsBackup From(SettingsEntity settings) => new(
        settings.FopRegistrationDate,
        settings.PaymentMode,
        settings.EsvRegistrationMonthPolicy,
        settings.EsvExempt,
        settings.TaxPaymentCountsFromStatutoryDeclarationDate,
        settings.ShiftTaxPaymentFromWeekend,
        settings.WeekendDays,
        settings.Locale,
        settings.Theme,
        settings.DefaultCurrency,
        settings.BackOnGroup3From,
        settings.Group3Since,
        DpsStatusEndpoints.ConfirmationOf(settings) is { } confirmation
            ? new Group3ConfirmationBackup(confirmation.ConfirmedOn, confirmation.ReceiptNumber)
            : null,
        settings.DpsFopRegistered,
        settings.DpsEsvRegistered,
        settings.DpsAccountsRegistered);

    public DpsStatusRequest ToDpsStatusRequest() => DpsStatusEndpoints.Normalize(new(
        Group3Since,
        Group3Confirmation is { } confirmation
            ? new Group3ConfirmationDto(confirmation.ConfirmedOn, confirmation.ReceiptNumber)
            : null,
        DpsFopRegistered,
        DpsEsvRegistered,
        DpsAccountsRegistered));

    public SettingsRequest ToRequest() => new(
        FopRegistrationDate,
        PaymentMode,
        EsvRegistrationMonthPolicy,
        EsvExempt,
        TaxPaymentCountsFromStatutoryDeclarationDate,
        ShiftTaxPaymentFromWeekend,
        WeekendDays,
        Locale,
        Theme,
        DefaultCurrency,
        BackOnGroup3From);

    public SettingsEntity ToEntity(string userId)
    {
        var settings = new SettingsEntity { UserId = userId };
        SettingsEndpoints.Apply(settings, ToRequest());
        DpsStatusEndpoints.Apply(settings, ToDpsStatusRequest());
        return settings;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record Group3ConfirmationBackup(DateOnly ConfirmedOn, string ReceiptNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientBackup(
    Guid Id,
    string Name,
    string? Address,
    string? Country,
    string? VatId,
    string? Email,
    Currency? DefaultCurrency,
    string? Notes)
{
    public static ClientBackup From(Client client) => new(
        client.Id,
        client.Name,
        client.Address,
        client.Country,
        client.VatId,
        client.Email,
        client.DefaultCurrency,
        client.Notes);

    public ClientRequest ToRequest() =>
        new ClientRequest(Name, Address, Country, VatId, Email, DefaultCurrency, Notes).Normalized();

    public Client ToEntity(string userId, Func<Guid, Guid> id)
    {
        var client = new Client { Id = id(Id), UserId = userId };
        ToRequest().ApplyTo(client);

        return client;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TransactionBackup(
    Guid Id,
    DateOnly ValueDate,
    long AmountMinor,
    Currency Currency,
    int RateE4,
    DateOnly? RateDate,
    RateSource? RateSource,
    long AmountUahKop,
    TransactionKind Kind,
    string? NonIncomeReason,
    Guid? ClientId,
    Guid? RefundsTransactionId,
    Guid? InvoiceId,
    string? InvoiceNumber,
    string? Description,
    Guid? BankAccountId,
    string? ExternalId,
    DateTimeOffset? BankTime,
    string? Counterparty,
    Guid? ImportBatchId,
    ReviewStatus ReviewStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TransactionBackup From(Transaction row) => new(
        row.Id,
        row.ValueDate,
        row.AmountMinor,
        row.Currency,
        row.RateE4,
        row.RateDate,
        row.RateSource,
        row.AmountUahKop,
        row.Kind,
        row.NonIncomeReason,
        row.ClientId,
        row.RefundsTransactionId,
        row.InvoiceId,
        row.InvoiceNumber,
        row.Description,
        row.BankAccountId,
        row.ExternalId,
        row.BankTime,
        row.Counterparty,
        row.ImportBatchId,
        row.ReviewStatus,
        row.CreatedAt,
        row.UpdatedAt);

    // An imported row names its account and its bank operation together; a typed row names neither.
    public (string Key, Issue Issue)? ImportError(
        IReadOnlySet<Guid> accountIds, IReadOnlyDictionary<Guid, Guid> batchAccounts) => this switch
    {
        { BankAccountId: null, ExternalId: not null } or { BankAccountId: not null, ExternalId: null } =>
            ("externalId", new Issue(ProblemCodes.InconsistentFields, "bankAccountId and externalId are set together or not at all.")),
        // A dismissed row is a deleted import kept so a sync does not record it again; it counts nowhere,
        // so a refund link it held would slip past every refund check.
        { ReviewStatus: ReviewStatus.Dismissed, ExternalId: null } =>
            ("reviewStatus", new Issue(ProblemCodes.InconsistentFields, "reviewStatus must not be Dismissed on a transaction without an externalId.")),
        { ReviewStatus: ReviewStatus.Dismissed, RefundsTransactionId: not null } =>
            ("refundsTransactionId", new Issue(ProblemCodes.InconsistentFields, "refundsTransactionId must be null on a dismissed transaction.")),
        { BankAccountId: { } accountId } when !accountIds.Contains(accountId) =>
            ("bankAccountId", new Issue(ProblemCodes.UnknownReference, "bankAccountId must be the id of one of the bank accounts.")),
        { ExternalId: { } externalId } when BackupDocument.ExternalIdError(externalId) is { } error => ("externalId", error),
        { Counterparty: { Length: > Limits.MaxClientNameLength } } =>
            ("counterparty", new Issue(ProblemCodes.TooLong, $"counterparty must not exceed {Limits.MaxClientNameLength} characters.")),
        { Counterparty: { } counterparty } when TextRules.HasDisallowedControlChar(counterparty) =>
            ("counterparty", new Issue(ProblemCodes.ControlCharacter, "counterparty must not contain a control character.")),
        { ImportBatchId: { } batchId } when !batchAccounts.TryGetValue(batchId, out var batchAccount)
            || batchAccount != BankAccountId =>
            ("importBatchId", new Issue(ProblemCodes.UnknownReference, "importBatchId must be the id of an import batch of the same bank account.")),
        _ => null,
    };

    // What linking leaves on a receipt (Rule 14): a confirmed Income row in the currency of an issued
    // invoice, carrying its number. Linking confirms an imported receipt, and dismissing one unlinks it.
    public Issue? InvoiceError(IReadOnlyDictionary<Guid, InvoiceBackup> invoices)
    {
        if (InvoiceId is not { } invoiceId)
        {
            return null;
        }

        if (!invoices.TryGetValue(invoiceId, out var invoice))
        {
            return new Issue(ProblemCodes.UnknownReference, "invoiceId must be the id of one of the invoices.");
        }

        var number = invoice is { NumberYear: { } year, NumberSequence: { } sequence }
            ? InvoiceNumbers.Format(year, sequence)
            : null;

        return this switch
        {
            _ when Kind != TransactionKind.Income || ReviewStatus != ReviewStatus.Confirmed =>
                new Issue(ProblemCodes.InconsistentFields, "invoiceId is set only on a confirmed Income transaction."),
            _ when invoice.Status != InvoiceStatus.Issued =>
                new Issue(ProblemCodes.InconsistentFields, "invoiceId must be the id of an issued invoice."),
            _ when Currency != invoice.Currency =>
                new Issue(
                    ProblemCodes.CurrencyMismatch,
                    $"A receipt paying a {invoice.Currency} invoice must be in {invoice.Currency}."),
            _ when InvoiceNumber?.Trim() != number =>
                new Issue(ProblemCodes.InconsistentFields, "invoiceNumber must be the number of the invoice the receipt pays."),
            _ => null,
        };
    }

    // A foreign rate travels as the manual rate so the endpoint's own range check covers it, whichever
    // source fixed it.
    public TransactionRequest ToRequest(string? clientName) => new(
        ValueDate,
        AmountMinor,
        Currency,
        Currency == Currency.UAH ? null : RateE4,
        Kind,
        NonIncomeReason,
        clientName,
        InvoiceNumber,
        Description,
        RefundsTransactionId);

    // The endpoints never ask for these, since they derive the rate fields themselves (Rule 2); a file
    // hands them over, so they are checked against the combinations the endpoints can produce.
    public (string Key, Issue Issue)? RateError() => this switch
    {
        { Currency: Currency.UAH } when RateE4 != Money.RateScale || RateDate is not null || RateSource is not null =>
            ("rateE4", new Issue(ProblemCodes.InvalidValue, $"A UAH transaction has rateE4 {Money.RateScale}, no rateDate and no rateSource.")),
        { Currency: not Currency.UAH, RateSource: null } =>
            ("rateSource", new Issue(ProblemCodes.InconsistentFields, "A foreign-currency transaction needs a rateSource.")),
        { RateSource: Fx.RateSource.Nbu, RateDate: null } =>
            ("rateDate", new Issue(ProblemCodes.InvalidValue, "An NBU rate needs the rateDate NBU published it for.")),
        { RateSource: Fx.RateSource.Nbu, RateDate: { } rateDate } when rateDate > ValueDate
            || rateDate.Year < Limits.MinYear =>
            ("rateDate", new Issue(ProblemCodes.InvalidValue, "An NBU rateDate must be on or before valueDate.")),
        { RateSource: Fx.RateSource.Manual, RateDate: not null } =>
            ("rateDate", new Issue(ProblemCodes.InvalidValue, "A manual rate has no rateDate.")),
        _ when TransactionsEndpoints.ExceedsUahBound(AmountMinor, RateE4) =>
            ("amountMinor", new Issue(ProblemCodes.InvalidValue, "amountMinor at this rate exceeds the largest hryvnia amount.")),
        _ when AmountUahKop != Money.ToUahKop(AmountMinor, RateE4) =>
            ("amountUahKop", new Issue(ProblemCodes.InvalidValue, $"amountUahKop must be {Money.ToUahKop(AmountMinor, RateE4)}, amountMinor at rateE4.")),
        _ => null,
    };

    public Transaction ToEntity(string userId, Func<Guid, Guid> id, Func<Guid, Guid> accountId)
    {
        var text = TransactionsEndpoints.Normalize(ToRequest(clientName: null));

        return new Transaction
        {
            Id = id(Id),
            UserId = userId,
            ValueDate = ValueDate,
            AmountMinor = AmountMinor,
            Currency = Currency,
            RateE4 = RateE4,
            RateDate = RateDate,
            RateSource = RateSource,
            AmountUahKop = AmountUahKop,
            Kind = Kind,
            NonIncomeReason = text.NonIncomeReason,
            ClientId = ClientId is { } clientId ? id(clientId) : null,
            RefundsTransactionId = RefundsTransactionId is { } receiptId ? id(receiptId) : null,
            InvoiceId = InvoiceId is { } invoiceId ? id(invoiceId) : null,
            InvoiceNumber = text.InvoiceNumber,
            Description = text.Description,
            BankAccountId = BankAccountId is { } bankAccountId ? accountId(bankAccountId) : null,
            ExternalId = ExternalId,
            BankTime = BankTime?.ToUniversalTime(),
            Counterparty = Counterparty,
            ImportBatchId = ImportBatchId is { } batchId ? id(batchId) : null,
            ReviewStatus = ReviewStatus,
            CreatedAt = CreatedAt.ToUniversalTime(),
            UpdatedAt = UpdatedAt.ToUniversalTime(),
        };
    }
}
