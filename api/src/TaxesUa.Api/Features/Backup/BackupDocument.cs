using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;
using EsvRegistrationMonthPolicy = TaxesUa.Api.Features.Settings.EsvRegistrationMonthPolicy;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Backup;

/// <summary>
/// Everything one owner stored, as the file the owner downloads and restores. The shape is its own
/// versioned contract rather than the API's request records, so a request gaining a field cannot
/// silently change what schema version 1 means. TaxYearConfig and FxRates are not here: both are
/// shared by every owner, and one owner's file must not rewrite another owner's tax parameters.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BackupDocument(
    int SchemaVersion,
    SettingsBackup? Settings,
    ClientBackup[] Clients,
    TransactionBackup[] Transactions,
    BudgetPaymentBackup[] BudgetPayments,
    BankAccountBackup[] BankAccounts,
    ImportBatchBackup[] ImportBatches)
{
    // 2 added bankAccounts, importBatches and the transactions' import fields (#76). A version 1 file
    // is upgraded to this shape before it is read, see UpgradeFromVersion1.
    public const int CurrentSchemaVersion = 2;

    public static BackupDocument From(
        SettingsEntity? settings,
        IEnumerable<Client> clients,
        IEnumerable<Transaction> transactions,
        IEnumerable<BudgetPayment> payments,
        IEnumerable<BankAccount> bankAccounts,
        IEnumerable<ImportBatch> importBatches) => new(
        CurrentSchemaVersion,
        settings is null ? null : SettingsBackup.From(settings),
        [.. clients.Select(client => new ClientBackup(client.Id, client.Name))],
        [.. transactions.Select(TransactionBackup.From)],
        [.. payments.Select(BudgetPaymentBackup.From)],
        [.. bankAccounts.Select(BankAccountBackup.From)],
        [.. importBatches.Select(ImportBatchBackup.Of)]);

    // Bank accounts are left out: a restore matches them to the owner's rows by bank and external id.
    public IEnumerable<Guid> Ids() =>
        Clients.Select(client => client.Id)
            .Concat(Transactions.Select(transaction => transaction.Id))
            .Concat(BudgetPayments.Select(payment => payment.Id))
            .Concat(ImportBatches.Select(batch => batch.Id));

    // A version 1 file predates bank imports: no accounts, no batches, and every row the owner's own.
    public static void UpgradeFromVersion1(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        root["bankAccounts"] = new JsonArray();
        root["importBatches"] = new JsonArray();
        if (root["transactions"] is not JsonArray transactions)
        {
            return;
        }

        foreach (var transaction in transactions.OfType<JsonObject>())
        {
            transaction["bankAccountId"] = null;
            transaction["externalId"] = null;
            transaction["bankTime"] = null;
            transaction["counterparty"] = null;
            transaction["importBatchId"] = null;
            transaction["reviewStatus"] = nameof(ReviewStatus.Confirmed);
        }
    }

    /// <summary>
    /// Every rule a row must meet that the file alone can answer, through the same validators the
    /// endpoints run. The refund links need the receipts' stored state, so
    /// <see cref="TransactionsEndpoints.ValidateLinksAsync"/> checks them after the rows are written.
    /// </summary>
    public Dictionary<string, string[]>? Validate(DateOnly today)
    {
        // RespectNullableAnnotations checks members, not array elements.
        if (Array.Exists(Clients, row => row is null)
            || Array.Exists(Transactions, row => row is null)
            || Array.Exists(BudgetPayments, row => row is null)
            || Array.Exists(BankAccounts, row => row is null)
            || Array.Exists(ImportBatches, row => row is null))
        {
            return new()
            {
                ["file"] = ["clients, transactions, budgetPayments, bankAccounts and importBatches must not contain null."],
            };
        }

        var errors = new Dictionary<string, string[]>();

        void Merge(string prefix, Dictionary<string, string[]>? found)
        {
            foreach (var (key, messages) in found ?? [])
            {
                errors[$"{prefix}.{key}"] = messages;
            }
        }

        if (Settings is { } settings)
        {
            Merge("settings", SettingsEndpoints.Validate(settings.ToRequest()));
        }

        var clientNames = new Dictionary<Guid, string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Clients.Length; i++)
        {
            var client = Clients[i];
            var name = client.Name.Trim();
            if (client.Id == Guid.Empty || !clientNames.TryAdd(client.Id, name))
            {
                errors[$"clients[{i}].id"] = ["id must be a non-empty id no other client has."];
            }

            if (name.Length is 0 or > TransactionsEndpoints.MaxClientNameLength)
            {
                errors[$"clients[{i}].name"] =
                    [$"name must be 1 to {TransactionsEndpoints.MaxClientNameLength} characters."];
            }
            else if (TextRules.HasDisallowedControlChar(name))
            {
                errors[$"clients[{i}].name"] =
                    ["name must not contain a NUL or other control character (tab, line feed and carriage return are allowed)."];
            }
            else if (!seenNames.Add(name))
            {
                errors[$"clients[{i}].name"] = ["name must differ from every other client's."];
            }
        }

        var accountIds = new HashSet<Guid>();
        var accountKeys = new HashSet<(Bank, string)>();
        for (var i = 0; i < BankAccounts.Length; i++)
        {
            var account = BankAccounts[i];
            if (account.Id == Guid.Empty || !accountIds.Add(account.Id))
            {
                errors[$"bankAccounts[{i}].id"] = ["id must be a non-empty id no other bank account has."];
            }

            if (account.Error() is var (key, message))
            {
                errors[$"bankAccounts[{i}].{key}"] = [message];
            }
            else if (!accountKeys.Add((account.Bank, account.ExternalId)))
            {
                errors[$"bankAccounts[{i}].externalId"] = ["externalId must differ from every other account's of the same bank."];
            }
        }

        var batchAccounts = new Dictionary<Guid, Guid>();
        for (var i = 0; i < ImportBatches.Length; i++)
        {
            var batch = ImportBatches[i];
            if (batch.Id == Guid.Empty || !batchAccounts.TryAdd(batch.Id, batch.BankAccountId))
            {
                errors[$"importBatches[{i}].id"] = ["id must be a non-empty id no other import batch has."];
            }

            if (!accountIds.Contains(batch.BankAccountId))
            {
                errors[$"importBatches[{i}].bankAccountId"] = ["bankAccountId must be the id of one of the bank accounts."];
            }
            else if (batch.ImportedCount < 0 || batch.SkippedCount < 0 || batch.From > batch.To)
            {
                errors[$"importBatches[{i}].importedCount"] = ["An import batch has a window from before to and counts of zero or more."];
            }
        }

        var externalIds = new HashSet<(Guid, string)>();
        var transactionIds = Transactions.Select(transaction => transaction.Id).ToHashSet();
        var receiptIds = Transactions
            .Where(transaction => transaction.Kind == TransactionKind.Income)
            .Select(transaction => transaction.Id)
            .ToHashSet();
        var seenTransactionIds = new HashSet<Guid>();
        for (var i = 0; i < Transactions.Length; i++)
        {
            var transaction = Transactions[i];
            var at = $"transactions[{i}]";
            if (transaction.Id == Guid.Empty || !seenTransactionIds.Add(transaction.Id))
            {
                errors[$"{at}.id"] = ["id must be a non-empty id no other transaction has."];
            }

            string? clientName = null;
            if (transaction.ClientId is { } clientId && !clientNames.TryGetValue(clientId, out clientName))
            {
                errors[$"{at}.clientId"] = ["clientId must be the id of one of the clients."];
            }

            if (transaction.RefundsTransactionId is { } receiptId && !transactionIds.Contains(receiptId))
            {
                errors[$"{at}.refundsTransactionId"] =
                    ["refundsTransactionId must be the id of one of the transactions."];
            }
            else if (transaction.RefundsTransactionId is { } linkedId && !receiptIds.Contains(linkedId))
            {
                // ValidateLinksAsync says the same after the insert, but two refunds linking each other
                // are a cycle EF cannot order, so the insert itself would fail first.
                errors[$"{at}.refundsTransactionId"] = ["refundsTransactionId must be the id of an Income transaction."];
            }

            if (transaction.ImportError(accountIds, batchAccounts) is var (importKey, importMessage))
            {
                errors[$"{at}.{importKey}"] = [importMessage];
            }
            else if (transaction is { BankAccountId: { } accountId, ExternalId: { } externalId }
                && !externalIds.Add((accountId, externalId)))
            {
                errors[$"{at}.externalId"] = ["externalId must differ from every other transaction's of the same bank account."];
            }

            var request = transaction.ToRequest(clientName);
            var requestErrors = TransactionsEndpoints.Validate(request, TransactionsEndpoints.Normalize(request), today);
            Merge(at, requestErrors);

            if (requestErrors is null && transaction.RateError() is var (key, message))
            {
                errors[$"{at}.{key}"] = [message];
            }
        }

        var seenPaymentIds = new HashSet<Guid>();
        for (var i = 0; i < BudgetPayments.Length; i++)
        {
            var payment = BudgetPayments[i];
            if (payment.Id == Guid.Empty || !seenPaymentIds.Add(payment.Id))
            {
                errors[$"budgetPayments[{i}].id"] = ["id must be a non-empty id no other payment has."];
            }

            Merge($"budgetPayments[{i}]", PaymentsEndpoints.Validate(payment.ToRequest()));
        }

        return errors.Count == 0 ? null : errors;
    }
}

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
    string DefaultCurrency)
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
        settings.DefaultCurrency);

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
        DefaultCurrency);

    public SettingsEntity ToEntity(string userId)
    {
        var settings = new SettingsEntity { UserId = userId };
        SettingsEndpoints.Apply(settings, ToRequest());
        return settings;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientBackup(Guid Id, string Name)
{
    public Client ToEntity(string userId, Func<Guid, Guid> id) => new() { Id = id(Id), UserId = userId, Name = Name.Trim() };
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
    private const int MaxExternalIdLength = 200;

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
    public (string Key, string Message)? ImportError(
        IReadOnlySet<Guid> accountIds, IReadOnlyDictionary<Guid, Guid> batchAccounts) => this switch
    {
        { BankAccountId: null, ExternalId: not null } or { BankAccountId: not null, ExternalId: null } =>
            ("externalId", "bankAccountId and externalId are set together or not at all."),
        // A dismissed row is a deleted import kept so a sync does not record it again; it counts nowhere,
        // so a refund link it held would slip past every refund check.
        { ReviewStatus: ReviewStatus.Dismissed, ExternalId: null } =>
            ("reviewStatus", "reviewStatus must not be Dismissed on a transaction without an externalId."),
        { ReviewStatus: ReviewStatus.Dismissed, RefundsTransactionId: not null } =>
            ("refundsTransactionId", "refundsTransactionId must be null on a dismissed transaction."),
        { BankAccountId: { } accountId } when !accountIds.Contains(accountId) =>
            ("bankAccountId", "bankAccountId must be the id of one of the bank accounts."),
        { ExternalId: { Length: 0 or > MaxExternalIdLength } } =>
            ("externalId", $"externalId must be 1 to {MaxExternalIdLength} characters."),
        { ExternalId: { } externalId } when TextRules.HasDisallowedControlChar(externalId) =>
            ("externalId", "externalId must not contain a control character."),
        { Counterparty: { Length: > TransactionsEndpoints.MaxClientNameLength } } =>
            ("counterparty", $"counterparty must not exceed {TransactionsEndpoints.MaxClientNameLength} characters."),
        { Counterparty: { } counterparty } when TextRules.HasDisallowedControlChar(counterparty) =>
            ("counterparty", "counterparty must not contain a control character."),
        { ImportBatchId: { } batchId } when !batchAccounts.TryGetValue(batchId, out var batchAccount)
            || batchAccount != BankAccountId =>
            ("importBatchId", "importBatchId must be the id of an import batch of the same bank account."),
        _ => null,
    };

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
    public (string Key, string Message)? RateError() => this switch
    {
        { Currency: Currency.UAH } when RateE4 != Money.RateScale || RateDate is not null || RateSource is not null =>
            ("rateE4", $"A UAH transaction has rateE4 {Money.RateScale}, no rateDate and no rateSource."),
        { Currency: not Currency.UAH, RateSource: null } =>
            ("rateSource", "A foreign-currency transaction needs a rateSource."),
        { RateSource: Fx.RateSource.Nbu, RateDate: null } =>
            ("rateDate", "An NBU rate needs the rateDate NBU published it for."),
        { RateSource: Fx.RateSource.Nbu, RateDate: { } rateDate } when rateDate > ValueDate
            || rateDate.Year < TransactionsEndpoints.MinYear =>
            ("rateDate", "An NBU rateDate must be on or before valueDate."),
        { RateSource: Fx.RateSource.Manual, RateDate: not null } =>
            ("rateDate", "A manual rate has no rateDate."),
        _ when TransactionsEndpoints.ExceedsUahBound(AmountMinor, RateE4) =>
            ("amountMinor", "amountMinor at this rate exceeds the largest hryvnia amount."),
        _ when AmountUahKop != Money.ToUahKop(AmountMinor, RateE4) =>
            ("amountUahKop", $"amountUahKop must be {Money.ToUahKop(AmountMinor, RateE4)}, amountMinor at rateE4."),
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BudgetPaymentBackup(
    Guid Id,
    DateOnly PaidOn,
    PaymentKind Kind,
    long AmountKop,
    int PeriodYear,
    int? PeriodQuarter,
    int? PeriodMonth,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BudgetPaymentBackup From(BudgetPayment row) => new(
        row.Id,
        row.PaidOn,
        row.Kind,
        row.AmountKop,
        row.PeriodYear,
        row.PeriodQuarter,
        row.PeriodMonth,
        row.Note,
        row.CreatedAt,
        row.UpdatedAt);

    public PaymentRequest ToRequest() => new(PaidOn, Kind, AmountKop, PeriodYear, PeriodQuarter, PeriodMonth, Note);

    public BudgetPayment ToEntity(string userId, Func<Guid, Guid> id)
    {
        var row = new BudgetPayment { Id = id(Id), UserId = userId, CreatedAt = CreatedAt.ToUniversalTime() };
        PaymentsEndpoints.Apply(row, ToRequest(), UpdatedAt.ToUniversalTime());
        return row;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record BankAccountBackup(
    Guid Id,
    Bank Bank,
    string ExternalId,
    string Name,
    int CurrencyCode,
    string Iban,
    string AccountType,
    bool IsFop,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public static BankAccountBackup From(BankAccount row) => new(
        row.Id,
        row.Bank,
        row.ExternalId,
        row.Name,
        row.CurrencyCode,
        row.Iban,
        row.AccountType,
        row.IsFop,
        row.IsActive,
        row.CreatedAt);

    // The column limits of BankAccountConfiguration, and #75's rule that only a FOP account is followed.
    public (string Key, string Message)? Error() => this switch
    {
        { ExternalId: { Length: 0 or > 200 } } => ("externalId", "externalId must be 1 to 200 characters."),
        { Name.Length: > 200 } => ("name", "name must not exceed 200 characters."),
        { Iban.Length: > 34 } => ("iban", "iban must not exceed 34 characters."),
        { AccountType.Length: > 50 } => ("accountType", "accountType must not exceed 50 characters."),
        _ when new[] { ExternalId, Name, Iban, AccountType }.Any(TextRules.HasDisallowedControlChar) =>
            ("externalId", "A bank account's text must not contain a control character."),
        { IsActive: true, IsFop: false } => ("isActive", "Only a FOP account can be followed."),
        _ => null,
    };

    public BankAccount ToEntity(string userId, Guid id) => new()
    {
        Id = id,
        UserId = userId,
        Bank = Bank,
        ExternalId = ExternalId,
        Name = Name,
        CurrencyCode = CurrencyCode,
        Iban = Iban,
        AccountType = AccountType,
        IsFop = IsFop,
        IsActive = IsActive,
        CreatedAt = CreatedAt.ToUniversalTime(),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ImportBatchBackup(
    Guid Id,
    ImportSource Source,
    Guid BankAccountId,
    DateTimeOffset From,
    DateTimeOffset To,
    int ImportedCount,
    int SkippedCount,
    DateTimeOffset CreatedAt)
{
    public static ImportBatchBackup Of(ImportBatch row) => new(
        row.Id,
        row.Source,
        row.BankAccountId,
        row.From,
        row.To,
        row.ImportedCount,
        row.SkippedCount,
        row.CreatedAt);

    public ImportBatch ToEntity(string userId, Func<Guid, Guid> id, Func<Guid, Guid> accountId) => new()
    {
        Id = id(Id),
        UserId = userId,
        Source = Source,
        BankAccountId = accountId(BankAccountId),
        From = From.ToUniversalTime(),
        To = To.ToUniversalTime(),
        ImportedCount = ImportedCount,
        SkippedCount = SkippedCount,
        CreatedAt = CreatedAt.ToUniversalTime(),
    };
}
