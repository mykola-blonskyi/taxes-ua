using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
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
    ImportBatchBackup[] ImportBatches,
    PaymentCandidateBackup[] BudgetPaymentCandidates,
    InvoicingDetailsBackup? InvoicingDetails,
    InvoiceBackup[] Invoices,
    DeclarationDetailsBackup? DeclarationDetails,
    DeclarationFilingBackup[] DeclarationFilings)
{
    // 2 added bankAccounts, importBatches and the transactions' import fields (#76); 3 added
    // budgetPaymentCandidates and the payments' bank operation (#80); 4 added invoicingDetails (#91); 5 added
    // the clients' details (#90); 6 added invoices (#92); 7 added declarationDetails and declarationFilings
    // (#110); 8 added the receipts' invoice links (#93). An older file is upgraded to this shape one version
    // at a time before it is read, see Upgrade.
    public const int CurrentSchemaVersion = 8;

    private const int MaxExternalIdLength = 200;

    public static BackupDocument From(
        SettingsEntity? settings,
        IEnumerable<Client> clients,
        IEnumerable<Transaction> transactions,
        IEnumerable<BudgetPayment> payments,
        IEnumerable<BankAccount> bankAccounts,
        IEnumerable<ImportBatch> importBatches,
        IEnumerable<BudgetPaymentCandidate> candidates,
        InvoicingDetails? invoicingDetails,
        IEnumerable<InvoicingPaymentDetails> invoicingPayments,
        IEnumerable<Invoice> invoices,
        DeclarationDetails? declarationDetails,
        IEnumerable<DeclarationFiling> declarationFilings) => new(
        CurrentSchemaVersion,
        settings is null ? null : SettingsBackup.From(settings),
        [.. clients.Select(ClientBackup.From)],
        [.. transactions.Select(TransactionBackup.From)],
        [.. payments.Select(BudgetPaymentBackup.From)],
        [.. bankAccounts.Select(BankAccountBackup.From)],
        [.. importBatches.Select(ImportBatchBackup.Of)],
        [.. candidates.Select(PaymentCandidateBackup.From)],
        invoicingDetails is null ? null : InvoicingDetailsBackup.From(invoicingDetails, invoicingPayments),
        [.. invoices.Select(InvoiceBackup.From)],
        declarationDetails is null ? null : DeclarationDetailsBackup.From(declarationDetails),
        [.. declarationFilings.Select(DeclarationFilingBackup.From)]);

    // Bank accounts are left out: a restore matches them to the owner's rows by bank and external id.
    public IEnumerable<Guid> Ids() =>
        Clients.Select(client => client.Id)
            .Concat(Transactions.Select(transaction => transaction.Id))
            .Concat(BudgetPayments.Select(payment => payment.Id))
            .Concat(ImportBatches.Select(batch => batch.Id))
            .Concat(BudgetPaymentCandidates.Select(candidate => candidate.Id))
            .Concat(Invoices.Select(invoice => invoice.Id));

    public static void Upgrade(JsonObject root, int version)
    {
        if (version == 1)
        {
            UpgradeFromVersion1(root);
        }

        if (version <= 2)
        {
            UpgradeFromVersion2(root);
        }

        if (version <= 3)
        {
            UpgradeFromVersion3(root);
        }

        if (version <= 4)
        {
            UpgradeFromVersion4(root);
        }

        if (version <= 5)
        {
            UpgradeFromVersion5(root);
        }

        if (version <= 6)
        {
            UpgradeFromVersion6(root);
        }

        if (version <= 7)
        {
            UpgradeFromVersion7(root);
        }
    }

    public static string? ExternalIdError(string externalId) => externalId switch
    {
        { Length: 0 or > MaxExternalIdLength } => $"externalId must be 1 to {MaxExternalIdLength} characters.",
        _ when TextRules.HasDisallowedControlChar(externalId) => "externalId must not contain a control character.",
        _ => null,
    };

    // A version 3 file predates the invoicing details.
    private static void UpgradeFromVersion3(JsonObject root)
    {
        root["schemaVersion"] = 4;
        root["invoicingDetails"] = null;
    }

    // A version 1 file predates bank imports: no accounts, no batches, and every row the owner's own.
    private static void UpgradeFromVersion1(JsonObject root)
    {
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

    // A version 2 file predates budget payment candidates: every payment was typed by the owner.
    private static void UpgradeFromVersion2(JsonObject root)
    {
        root["schemaVersion"] = 3;
        root["budgetPaymentCandidates"] = new JsonArray();
        if (root["budgetPayments"] is not JsonArray payments)
        {
            return;
        }

        foreach (var payment in payments.OfType<JsonObject>())
        {
            payment["bankAccountId"] = null;
            payment["externalId"] = null;
        }
    }

    // A version 4 file predates client details: every client has only its name.
    private static void UpgradeFromVersion4(JsonObject root)
    {
        root["schemaVersion"] = 5;
        if (root["clients"] is not JsonArray clients)
        {
            return;
        }

        foreach (var client in clients.OfType<JsonObject>())
        {
            client["address"] = null;
            client["country"] = null;
            client["vatId"] = null;
            client["email"] = null;
            client["defaultCurrency"] = null;
            client["notes"] = null;
        }
    }

    // A version 5 file predates invoices.
    private static void UpgradeFromVersion5(JsonObject root)
    {
        root["schemaVersion"] = 6;
        root["invoices"] = new JsonArray();
    }

    // A version 6 file predates the declaration: no details and nothing marked filed.
    private static void UpgradeFromVersion6(JsonObject root)
    {
        root["schemaVersion"] = 7;
        root["declarationDetails"] = null;
        root["declarationFilings"] = new JsonArray();
    }

    // A version 7 file predates paying an invoice with a receipt: no receipt is linked.
    private static void UpgradeFromVersion7(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        if (root["transactions"] is not JsonArray transactions)
        {
            return;
        }

        foreach (var transaction in transactions.OfType<JsonObject>())
        {
            transaction["invoiceId"] = null;
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
            || Array.Exists(ImportBatches, row => row is null)
            || Array.Exists(BudgetPaymentCandidates, row => row is null)
            || Array.Exists(Invoices, row => row is null)
            || Array.Exists(DeclarationFilings, row => row is null))
        {
            return new()
            {
                ["file"] = ["clients, transactions, budgetPayments, bankAccounts, importBatches, budgetPaymentCandidates, invoices and declarationFilings must not contain null."],
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

        if (InvoicingDetails is { } invoicing)
        {
            Merge("invoicingDetails", invoicing.Validate());
        }

        if (DeclarationDetails is { } declaration)
        {
            Merge("declarationDetails", DeclarationDetailsEndpoints.Validate(declaration.ToRequest()));
        }

        var filedQuarters = new HashSet<(int, int)>();
        for (var i = 0; i < DeclarationFilings.Length; i++)
        {
            var filing = DeclarationFilings[i];
            Merge($"declarationFilings[{i}]", DeclarationsEndpoints.ValidateFiling(filing.Year, filing.Quarter, filing.FiledOn, today));
            if (!filedQuarters.Add((filing.Year, filing.Quarter)))
            {
                errors[$"declarationFilings[{i}].quarter"] = ["A quarter is marked filed at most once."];
            }
        }

        var clientNames = new Dictionary<Guid, string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Clients.Length; i++)
        {
            var client = Clients[i];
            var normalized = client.ToRequest();
            var name = normalized.Name;
            if (client.Id == Guid.Empty || !clientNames.TryAdd(client.Id, name))
            {
                errors[$"clients[{i}].id"] = ["id must be a non-empty id no other client has."];
            }

            Merge($"clients[{i}]", ClientRules.Validate(normalized));
            if (name.Length > 0 && !seenNames.Add(name))
            {
                errors[$"clients[{i}].name"] = ["name must differ from every other client's."];
            }
        }

        var invoiceIds = new HashSet<Guid>();
        var invoicesById = new Dictionary<Guid, InvoiceBackup>();
        var invoiceNumbers = new HashSet<(int, int)>();
        for (var i = 0; i < Invoices.Length; i++)
        {
            var invoice = Invoices[i];
            if (invoice.Id == Guid.Empty || !invoiceIds.Add(invoice.Id))
            {
                errors[$"invoices[{i}].id"] = ["id must be a non-empty id no other invoice has."];
            }
            else
            {
                invoicesById[invoice.Id] = invoice;
            }

            if (!clientNames.ContainsKey(invoice.ClientId))
            {
                errors[$"invoices[{i}].clientId"] = ["clientId must be the id of one of the clients."];
            }

            Merge($"invoices[{i}]", invoice.Validate());
            if (invoice is { NumberYear: { } year, NumberSequence: { } sequence } && !invoiceNumbers.Add((year, sequence)))
            {
                errors[$"invoices[{i}].numberSequence"] = ["The invoice number must differ from every other invoice's."];
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

            if (transaction.InvoiceError(invoicesById) is { } invoiceError)
            {
                errors[$"{at}.invoiceId"] = [invoiceError];
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
        var paymentOperations = new HashSet<(Guid, string)>();
        for (var i = 0; i < BudgetPayments.Length; i++)
        {
            var payment = BudgetPayments[i];
            if (payment.Id == Guid.Empty || !seenPaymentIds.Add(payment.Id))
            {
                errors[$"budgetPayments[{i}].id"] = ["id must be a non-empty id no other payment has."];
            }

            if (payment.OperationError(accountIds) is var (key, message))
            {
                errors[$"budgetPayments[{i}].{key}"] = [message];
            }
            else if (payment is { BankAccountId: { } accountId, ExternalId: { } externalId }
                && !paymentOperations.Add((accountId, externalId)))
            {
                errors[$"budgetPayments[{i}].externalId"] = ["externalId must differ from every other payment's of the same bank account."];
            }

            Merge($"budgetPayments[{i}]", PaymentsEndpoints.Validate(payment.ToRequest()));
        }

        var seenCandidateIds = new HashSet<Guid>();
        var candidateOperations = new HashSet<(Guid, string)>();
        for (var i = 0; i < BudgetPaymentCandidates.Length; i++)
        {
            var candidate = BudgetPaymentCandidates[i];
            if (candidate.Id == Guid.Empty || !seenCandidateIds.Add(candidate.Id))
            {
                errors[$"budgetPaymentCandidates[{i}].id"] = ["id must be a non-empty id no other candidate has."];
            }

            if (candidate.Error(accountIds) is var (key, message))
            {
                errors[$"budgetPaymentCandidates[{i}].{key}"] = [message];
            }
            else if (!candidateOperations.Add((candidate.BankAccountId, candidate.ExternalId)))
            {
                errors[$"budgetPaymentCandidates[{i}].externalId"] = ["externalId must differ from every other candidate's of the same bank account."];
            }
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
        { ExternalId: { } externalId } when BackupDocument.ExternalIdError(externalId) is { } error => ("externalId", error),
        { Counterparty: { Length: > TransactionsEndpoints.MaxClientNameLength } } =>
            ("counterparty", $"counterparty must not exceed {TransactionsEndpoints.MaxClientNameLength} characters."),
        { Counterparty: { } counterparty } when TextRules.HasDisallowedControlChar(counterparty) =>
            ("counterparty", "counterparty must not contain a control character."),
        { ImportBatchId: { } batchId } when !batchAccounts.TryGetValue(batchId, out var batchAccount)
            || batchAccount != BankAccountId =>
            ("importBatchId", "importBatchId must be the id of an import batch of the same bank account."),
        _ => null,
    };

    // What linking leaves on a receipt (Rule 14): a confirmed Income row in the currency of an issued
    // invoice, carrying its number. Linking confirms an imported receipt, and dismissing one unlinks it.
    public string? InvoiceError(IReadOnlyDictionary<Guid, InvoiceBackup> invoices)
    {
        if (InvoiceId is not { } invoiceId)
        {
            return null;
        }

        if (!invoices.TryGetValue(invoiceId, out var invoice))
        {
            return "invoiceId must be the id of one of the invoices.";
        }

        var number = invoice is { NumberYear: { } year, NumberSequence: { } sequence }
            ? InvoiceNumbers.Format(year, sequence)
            : null;

        return this switch
        {
            _ when Kind != TransactionKind.Income || ReviewStatus != ReviewStatus.Confirmed =>
                "invoiceId is set only on a confirmed Income transaction.",
            _ when invoice.Status != InvoiceStatus.Issued => "invoiceId must be the id of an issued invoice.",
            _ when Currency != invoice.Currency => $"A receipt paying a {invoice.Currency} invoice must be in {invoice.Currency}.",
            _ when InvoiceNumber?.Trim() != number => "invoiceNumber must be the number of the invoice the receipt pays.",
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
    Guid? BankAccountId,
    string? ExternalId,
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
        row.BankAccountId,
        row.ExternalId,
        row.CreatedAt,
        row.UpdatedAt);

    public (string Key, string Message)? OperationError(IReadOnlySet<Guid> accountIds) => this switch
    {
        { BankAccountId: null, ExternalId: not null } or { BankAccountId: not null, ExternalId: null } =>
            ("externalId", "bankAccountId and externalId are set together or not at all."),
        { BankAccountId: { } accountId } when !accountIds.Contains(accountId) =>
            ("bankAccountId", "bankAccountId must be the id of one of the bank accounts."),
        { ExternalId: { } externalId } when BackupDocument.ExternalIdError(externalId) is { } error => ("externalId", error),
        _ => null,
    };

    public PaymentRequest ToRequest() => new(PaidOn, Kind, AmountKop, PeriodYear, PeriodQuarter, PeriodMonth, Note);

    public BudgetPayment ToEntity(string userId, Func<Guid, Guid> id, Func<Guid, Guid> accountId)
    {
        var row = new BudgetPayment
        {
            Id = id(Id),
            UserId = userId,
            BankAccountId = BankAccountId is { } bankAccountId ? accountId(bankAccountId) : null,
            ExternalId = ExternalId,
            CreatedAt = CreatedAt.ToUniversalTime(),
        };
        PaymentsEndpoints.Apply(row, ToRequest(), UpdatedAt.ToUniversalTime());
        return row;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PaymentCandidateBackup(
    Guid Id,
    Guid BankAccountId,
    string ExternalId,
    DateTimeOffset BankTime,
    long AmountKop,
    string CounterIban,
    string? CounterName,
    string? Purpose,
    CandidateStatus Status,
    PaymentKind? ConfirmedKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt)
{
    public static PaymentCandidateBackup From(BudgetPaymentCandidate row) => new(
        row.Id,
        row.BankAccountId,
        row.ExternalId,
        row.BankTime,
        row.AmountKop,
        row.CounterIban,
        row.CounterName,
        row.Purpose,
        row.Status,
        row.ConfirmedKind,
        row.CreatedAt,
        row.ResolvedAt);

    // The column limits and check constraints of BudgetPaymentCandidateConfiguration, and the IBAN form
    // the sync stores, so the next candidate to the same account still finds what the owner confirmed.
    public (string Key, string Message)? Error(IReadOnlySet<Guid> accountIds) => this switch
    {
        _ when !accountIds.Contains(BankAccountId) =>
            ("bankAccountId", "bankAccountId must be the id of one of the bank accounts."),
        _ when BackupDocument.ExternalIdError(ExternalId) is { } error => ("externalId", error),
        { AmountKop: <= 0 } => ("amountKop", "amountKop must be positive."),
        _ when !TreasuryPayment.IsTreasury(CounterIban) || TreasuryPayment.Normalize(CounterIban) != CounterIban =>
            ("counterIban", "counterIban must be a Treasury IBAN in capitals without spaces."),
        { CounterName.Length: > TransactionsEndpoints.MaxClientNameLength } =>
            ("counterName", $"counterName must not exceed {TransactionsEndpoints.MaxClientNameLength} characters."),
        { Purpose.Length: > TransactionsEndpoints.MaxDescriptionLength } =>
            ("purpose", $"purpose must not exceed {TransactionsEndpoints.MaxDescriptionLength} characters."),
        _ when new[] { CounterName, Purpose }.Any(text => text is not null && TextRules.HasDisallowedControlChar(text)) =>
            ("purpose", "counterName and purpose must not contain a control character."),
        { Status: CandidateStatus.Confirmed, ConfirmedKind: null } or { Status: not CandidateStatus.Confirmed, ConfirmedKind: not null } =>
            ("confirmedKind", "confirmedKind is set exactly when status is Confirmed."),
        _ => null,
    };

    public BudgetPaymentCandidate ToEntity(string userId, Func<Guid, Guid> id, Func<Guid, Guid> accountId) => new()
    {
        Id = id(Id),
        UserId = userId,
        BankAccountId = accountId(BankAccountId),
        ExternalId = ExternalId,
        BankTime = BankTime.ToUniversalTime(),
        AmountKop = AmountKop,
        CounterIban = CounterIban,
        CounterName = CounterName,
        Purpose = Purpose,
        Status = Status,
        ConfirmedKind = ConfirmedKind,
        CreatedAt = CreatedAt.ToUniversalTime(),
        ResolvedAt = ResolvedAt?.ToUniversalTime(),
    };
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoicingDetailsBackup(
    string SellerNameUk,
    string SellerNameEn,
    string Rnokpp,
    string AddressUk,
    string AddressEn,
    string AcceptanceClauseEn,
    string AcceptanceClauseUk,
    string FeesClauseEn,
    string FeesClauseUk,
    string TaxStatusClauseEn,
    string TaxStatusClauseUk,
    PaymentDetailsInput[] PaymentDetails,
    // The image as base64, since the file is JSON.
    string? SignatureImage,
    string? SignatureContentType,
    DateTimeOffset? SignatureUpdatedAt)
{
    public static InvoicingDetailsBackup From(InvoicingDetails details, IEnumerable<InvoicingPaymentDetails> payments) => new(
        details.SellerNameUk,
        details.SellerNameEn,
        details.Rnokpp,
        details.AddressUk,
        details.AddressEn,
        details.AcceptanceClauseEn,
        details.AcceptanceClauseUk,
        details.FeesClauseEn,
        details.FeesClauseUk,
        details.TaxStatusClauseEn,
        details.TaxStatusClauseUk,
        [.. payments.OrderBy(payment => payment.Currency).Select(payment => new PaymentDetailsInput(
            payment.Currency,
            payment.Iban,
            payment.BeneficiaryBank,
            payment.Swift,
            payment.IntermediaryBank,
            payment.IntermediarySwift,
            payment.IntermediaryAccount))],
        details.SignatureImage is null ? null : Convert.ToBase64String(details.SignatureImage),
        details.SignatureContentType,
        details.SignatureUpdatedAt);

    public InvoicingDetailsRequest ToRequest() => InvoicingEndpoints.Normalize(new InvoicingDetailsRequest(
        SellerNameUk,
        SellerNameEn,
        Rnokpp,
        AddressUk,
        AddressEn,
        AcceptanceClauseEn,
        AcceptanceClauseUk,
        FeesClauseEn,
        FeesClauseUk,
        TaxStatusClauseEn,
        TaxStatusClauseUk,
        PaymentDetails));

    // The endpoint's own rules, plus the image: it travels as text, so it is decoded and checked again.
    public Dictionary<string, string[]>? Validate()
    {
        if (Array.Exists(PaymentDetails, row => row is null))
        {
            return new() { ["paymentDetails"] = ["paymentDetails must not contain null."] };
        }

        var errors = InvoicingEndpoints.Validate(ToRequest()) ?? [];
        if (SignatureError(SignatureImage, SignatureContentType) is { } error)
        {
            errors["signatureImage"] = [error];
        }

        return errors.Count == 0 ? null : errors;
    }

    public (InvoicingDetails Details, InvoicingPaymentDetails[] Payments) ToEntities(string userId)
    {
        var request = ToRequest();
        var details = new InvoicingDetails { UserId = userId };
        InvoicingEndpoints.Apply(details, request);
        details.SignatureImage = DecodeSignature();
        details.SignatureContentType = details.SignatureImage is null ? null : SignatureContentType;
        details.SignatureUpdatedAt = details.SignatureImage is null ? null : SignatureUpdatedAt?.ToUniversalTime();

        return (
            details,
            [.. request.PaymentDetails.Select(payment => InvoicingEndpoints.ToEntity(userId, Guid.NewGuid(), payment))]);
    }

    private byte[]? DecodeSignature() => DecodeSignature(SignatureImage);

    internal static byte[]? DecodeSignature(string? base64)
    {
        if (base64 is null || base64.Length > InvoicingEndpoints.MaxSignatureBytes * 2)
        {
            return null;
        }

        var buffer = new byte[base64.Length];

        return Convert.TryFromBase64String(base64, buffer, out var written) ? buffer[..written] : null;
    }

    /// <summary>The image rule the signature upload enforces, for an image that travelled as base64.</summary>
    internal static string? SignatureError(string? base64, string? contentType)
    {
        if ((base64 is null) != (contentType is null))
        {
            return "signatureImage and signatureContentType are set together or not at all.";
        }

        if (base64 is null)
        {
            return null;
        }

        return DecodeSignature(base64) is { } image
            ? InvoicingEndpoints.SignatureError(image, contentType!)
            : "signatureImage must be base64.";
    }
}

/// <summary>
/// An invoice with its lines and, once issued, its frozen snapshot and signature. The number is the
/// year and sequence, so a restore keeps every number the owner already sent.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceBackup(
    Guid Id,
    Guid ClientId,
    InvoiceStatus Status,
    int? NumberYear,
    int? NumberSequence,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLine[] Lines,
    InvoiceSnapshot? Snapshot,
    string? SignatureImage,
    string? SignatureContentType,
    string? CancelReason,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static InvoiceBackup From(Invoice invoice) => new(
        invoice.Id,
        invoice.ClientId,
        invoice.Status,
        invoice.NumberYear,
        invoice.NumberSequence,
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Currency,
        invoice.Lines,
        invoice.Snapshot,
        invoice.SignatureImage is null ? null : Convert.ToBase64String(invoice.SignatureImage),
        invoice.SignatureContentType,
        invoice.CancelReason,
        invoice.IssuedAt,
        invoice.CancelledAt,
        invoice.CreatedAt,
        invoice.UpdatedAt);

    /// <summary>The draft rules on the lines and dates, and the rules each status sets for the rest.</summary>
    public Dictionary<string, string[]>? Validate()
    {
        if (Array.Exists(Lines, line => line is null))
        {
            return new() { ["lines"] = ["lines must not contain null."] };
        }

        var request = new InvoiceRequest(
            ClientId,
            IssueDate,
            DueDate,
            Currency,
            [.. Lines.Select(line => new InvoiceLineRequest(
                line.DescriptionEn, line.DescriptionUk, line.Unit, line.QuantityThousandths, line.RateMinor))]);
        var errors = InvoiceRules.Validate(request.Normalized()) ?? [];

        var numbered = NumberYear is not null || NumberSequence is not null;
        switch (Status)
        {
            case InvoiceStatus.Draft when numbered || Snapshot is not null || SignatureImage is not null
                || CancelReason is not null || IssuedAt is not null || CancelledAt is not null:
                errors["status"] = ["A draft has no number, snapshot, signature, cancel reason or issue and cancel times."];
                break;
            case InvoiceStatus.Issued or InvoiceStatus.Cancelled when NumberYear != IssueDate.Year
                || NumberSequence is not > 0 || Snapshot is null || IssuedAt is null:
                errors["status"] = ["An issued invoice has a number of its issue date's year, a snapshot and an issue time."];
                break;
            case InvoiceStatus.Issued when CancelReason is not null || CancelledAt is not null:
                errors["cancelReason"] = ["Only a cancelled invoice has a cancel reason."];
                break;
            case InvoiceStatus.Cancelled when CancelledAt is null || InvoiceRules.CancelReasonError(CancelReason?.Trim() ?? string.Empty) is not null:
                errors["cancelReason"] = [InvoiceRules.CancelReasonError(CancelReason?.Trim() ?? string.Empty) ?? "A cancelled invoice has a cancel time."];
                break;
        }

        if (!Enum.IsDefined(Status))
        {
            errors["status"] = ["status must be Draft, Issued or Cancelled."];
        }

        if (Snapshot is not null && SnapshotTexts(Snapshot).Any(text => text is not null && TextRules.HasDisallowedControlChar(text)))
        {
            errors["snapshot"] = ["The snapshot must not contain a control character."];
        }

        if (InvoicingDetailsBackup.SignatureError(SignatureImage, SignatureContentType) is { } signatureError)
        {
            errors["signatureImage"] = [signatureError];
        }

        return errors.Count == 0 ? null : errors;
    }

    public Invoice ToEntity(string userId, Func<Guid, Guid> id)
    {
        var lines = Lines.Select(line => line with
        {
            DescriptionEn = line.DescriptionEn.Trim(),
            DescriptionUk = line.DescriptionUk.Trim(),
        }).ToArray();
        var signature = InvoicingDetailsBackup.DecodeSignature(SignatureImage);

        return new Invoice
        {
            Id = id(Id),
            UserId = userId,
            ClientId = id(ClientId),
            Status = Status,
            NumberYear = NumberYear,
            NumberSequence = NumberSequence,
            IssueDate = IssueDate,
            DueDate = DueDate,
            Currency = Currency,
            Lines = lines,
            TotalMinor = InvoiceRules.TotalMinor(lines),
            Snapshot = Snapshot,
            SignatureImage = signature,
            SignatureContentType = signature is null ? null : SignatureContentType,
            CancelReason = CancelReason?.Trim(),
            IssuedAt = IssuedAt?.ToUniversalTime(),
            CancelledAt = CancelledAt?.ToUniversalTime(),
            CreatedAt = CreatedAt.ToUniversalTime(),
            UpdatedAt = UpdatedAt.ToUniversalTime(),
        };
    }

    private static IEnumerable<string?> SnapshotTexts(InvoiceSnapshot snapshot) =>
    [
        snapshot.Seller.NameUk, snapshot.Seller.NameEn, snapshot.Seller.Rnokpp, snapshot.Seller.AddressUk,
        snapshot.Seller.AddressEn, snapshot.Buyer.Name, snapshot.Buyer.Address, snapshot.Buyer.Country,
        snapshot.Buyer.CountryName, snapshot.Buyer.VatId, snapshot.Buyer.Email, snapshot.Payment.Iban,
        snapshot.Payment.BeneficiaryBank, snapshot.Payment.Swift, snapshot.Payment.IntermediaryBank,
        snapshot.Payment.IntermediarySwift, snapshot.Payment.IntermediaryAccount, snapshot.Clauses.AcceptanceEn,
        snapshot.Clauses.AcceptanceUk, snapshot.Clauses.FeesEn, snapshot.Clauses.FeesUk,
        snapshot.Clauses.TaxStatusEn, snapshot.Clauses.TaxStatusUk,
    ];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationDetailsBackup(
    int? TaxOfficeRegion,
    int? TaxOfficeDistrict,
    string[] KvedCodes,
    string Address)
{
    public static DeclarationDetailsBackup From(DeclarationDetails details) => new(
        details.TaxOfficeRegion, details.TaxOfficeDistrict, details.KvedCodes, details.Address);

    public DeclarationDetailsRequest ToRequest() => DeclarationDetailsEndpoints.Normalize(
        new DeclarationDetailsRequest(TaxOfficeRegion, TaxOfficeDistrict, KvedCodes, Address));

    public DeclarationDetails ToEntity(string userId)
    {
        var details = new DeclarationDetails { UserId = userId };
        DeclarationDetailsEndpoints.Apply(details, ToRequest());
        return details;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationFilingBackup(
    int Year,
    int Quarter,
    DateOnly FiledOn,
    DeclarationType Type,
    long FiledIncomeKop,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DeclarationFilingBackup From(DeclarationFiling filing) => new(
        filing.Year,
        filing.Quarter,
        filing.FiledOn,
        filing.Type,
        filing.FiledIncomeKop,
        filing.CreatedAt,
        filing.UpdatedAt);

    public DeclarationFiling ToEntity(string userId) => new()
    {
        UserId = userId,
        Year = Year,
        Quarter = Quarter,
        FiledOn = FiledOn,
        Type = Type,
        FiledIncomeKop = FiledIncomeKop,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}
