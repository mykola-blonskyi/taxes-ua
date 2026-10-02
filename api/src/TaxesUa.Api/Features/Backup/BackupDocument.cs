using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
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
    DeclarationFilingBackup[] DeclarationFilings,
    DeclarationFileBackup[] DeclarationFiles,
    TreasuryAccountBackup[] TreasuryAccounts,
    NotificationChannelBackup[] NotificationChannels,
    ReserveJarBackup? ReserveJar)
{
    // 2 added bankAccounts, importBatches and the transactions' import fields (#76); 3 added
    // budgetPaymentCandidates and the payments' bank operation (#80); 4 added invoicingDetails (#91); 5 added
    // the clients' details (#90); 6 added invoices (#92); 7 added declarationDetails and declarationFilings
    // (#110); 8 added the receipts' invoice links (#93); 9 added treasuryAccounts and the candidates'
    // counterEdrpou (#98); 10 added the settings' backOnGroup3From (#118); 11 added notificationChannels (#106);
    // 12 added declarationFiles and the declaration details' taxOfficeName (#111); 13 added the declaration
    // files' annexFileName and annexContent (#112); 14 added reserveJar (#102); 15 added the notification
    // channels' confirmedAt, which email needs because an address waits for its link (#107); 16 added the
    // settings' group3Since, group3Confirmation and the three DPS registration ticks (#172). An older file is
    // upgraded to this shape one version at a time before it is read, see Upgrade.
    public const int CurrentSchemaVersion = 16;

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
        IEnumerable<DeclarationFiling> declarationFilings,
        IEnumerable<DeclarationFile> declarationFiles,
        IEnumerable<TreasuryAccount> treasuryAccounts,
        IEnumerable<NotificationChannel> notificationChannels,
        ReserveJar? reserveJar) => new(
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
        [.. declarationFilings.Select(DeclarationFilingBackup.From)],
        [.. declarationFiles.Select(DeclarationFileBackup.From)],
        [.. treasuryAccounts.Select(TreasuryAccountBackup.From)],
        [.. notificationChannels.Select(NotificationChannelBackup.From)],
        reserveJar is null ? null : ReserveJarBackup.From(reserveJar));

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

        if (version <= 8)
        {
            UpgradeFromVersion8(root);
        }

        if (version <= 9)
        {
            UpgradeFromVersion9(root);
        }

        if (version <= 10)
        {
            UpgradeFromVersion10(root);
        }

        if (version <= 11)
        {
            UpgradeFromVersion11(root);
        }

        if (version <= 12)
        {
            UpgradeFromVersion12(root);
        }

        if (version <= 13)
        {
            UpgradeFromVersion13(root);
        }

        if (version <= 14)
        {
            UpgradeFromVersion14(root);
        }

        if (version <= 15)
        {
            UpgradeFromVersion15(root);
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
        root["schemaVersion"] = 8;
        if (root["transactions"] is not JsonArray transactions)
        {
            return;
        }

        foreach (var transaction in transactions.OfType<JsonObject>())
        {
            transaction["invoiceId"] = null;
        }
    }

    // A version 8 file predates Treasury accounts, and its candidates never kept the counterparty's code.
    private static void UpgradeFromVersion8(JsonObject root)
    {
        root["schemaVersion"] = 9;
        root["treasuryAccounts"] = new JsonArray();
        if (root["budgetPaymentCandidates"] is not JsonArray candidates)
        {
            return;
        }

        foreach (var candidate in candidates.OfType<JsonObject>())
        {
            candidate["counterEdrpou"] = null;
        }
    }

    // A version 9 file predates the return to group 3 after a limit crossing: none is set.
    private static void UpgradeFromVersion9(JsonObject root)
    {
        root["schemaVersion"] = 10;
        if (root["settings"] is JsonObject settings)
        {
            settings["backOnGroup3From"] = null;
        }
    }

    // A version 10 file predates the notification channels: none is connected.
    private static void UpgradeFromVersion10(JsonObject root)
    {
        root["schemaVersion"] = 11;
        root["notificationChannels"] = new JsonArray();
    }

    // A version 11 file predates the declaration file and the tax office's name.
    private static void UpgradeFromVersion11(JsonObject root)
    {
        root["schemaVersion"] = 12;
        root["declarationFiles"] = new JsonArray();
        if (root["declarationDetails"] is JsonObject details)
        {
            details["taxOfficeName"] = string.Empty;
        }
    }

    // A version 12 file predates the ESV annex: no declaration file had one.
    private static void UpgradeFromVersion12(JsonObject root)
    {
        root["schemaVersion"] = 13;
        if (root["declarationFiles"] is JsonArray files)
        {
            foreach (var file in files.OfType<JsonObject>())
            {
                file["annexFileName"] = null;
                file["annexContent"] = null;
            }
        }
    }

    // A version 13 file predates the reserve jar: none was chosen.
    private static void UpgradeFromVersion13(JsonObject root)
    {
        root["schemaVersion"] = 14;
        root["reserveJar"] = null;
    }

    // A version 14 file predates email and the confirmation of a channel (#107). Every channel it holds is
    // a Telegram chat, confirmed when it was linked.
    private static void UpgradeFromVersion14(JsonObject root)
    {
        root["schemaVersion"] = 15;
        if (root["notificationChannels"] is JsonArray channels)
        {
            foreach (var channel in channels.OfType<JsonObject>())
            {
                channel["confirmedAt"] = channel["linkedAt"]?.DeepClone();
            }
        }
    }

    // A version 15 file predates the DPS status (#172): the app assumed group 3 from registration and
    // nothing was confirmed or ticked, which is what the migration gives a stored owner too. It also
    // predates the move off the wrong Prorated default, and cannot tell that default from a deliberate
    // choice, so its Prorated is read as FullMonth, as the migration did for stored owners (ADR-018
    // amendment). A version 16 file is written after that move, so its Prorated is the owner's choice.
    private static void UpgradeFromVersion15(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        if (root["settings"] is JsonObject settings)
        {
            if (settings["esvRegistrationMonthPolicy"]?.ToString() == nameof(EsvRegistrationMonthPolicy.Prorated))
            {
                settings["esvRegistrationMonthPolicy"] = nameof(EsvRegistrationMonthPolicy.FullMonth);
            }

            settings["group3Since"] = settings["fopRegistrationDate"]?.DeepClone();
            settings["group3Confirmation"] = null;
            settings["dpsFopRegistered"] = false;
            settings["dpsEsvRegistered"] = false;
            settings["dpsAccountsRegistered"] = false;
        }
    }

    /// <summary>
    /// Every rule a row must meet that the file alone can answer, through the same validators the
    /// endpoints run. The refund links need the receipts' stored state, so
    /// <see cref="TransactionsEndpoints.ValidateLinksAsync"/> checks them after the rows are written.
    /// </summary>
    public Dictionary<string, string[]>? Validate(DateOnly today, DateTimeOffset now)
    {
        // RespectNullableAnnotations checks members, not array elements.
        if (Array.Exists(Clients, row => row is null)
            || Array.Exists(Transactions, row => row is null)
            || Array.Exists(BudgetPayments, row => row is null)
            || Array.Exists(BankAccounts, row => row is null)
            || Array.Exists(ImportBatches, row => row is null)
            || Array.Exists(BudgetPaymentCandidates, row => row is null)
            || Array.Exists(Invoices, row => row is null)
            || Array.Exists(DeclarationFilings, row => row is null)
            || Array.Exists(DeclarationFiles, row => row is null)
            || Array.Exists(TreasuryAccounts, row => row is null)
            || Array.Exists(NotificationChannels, row => row is null))
        {
            return new()
            {
                ["file"] = ["clients, transactions, budgetPayments, bankAccounts, importBatches, budgetPaymentCandidates, invoices, declarationFilings, declarationFiles, treasuryAccounts and notificationChannels must not contain null."],
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
            Merge("settings", DpsStatusEndpoints.Validate(settings.ToDpsStatusRequest(), settings.FopRegistrationDate));
        }

        if (InvoicingDetails is { } invoicing)
        {
            Merge("invoicingDetails", invoicing.Validate());
        }

        if (DeclarationDetails is { } declaration)
        {
            Merge("declarationDetails", DeclarationDetailsEndpoints.Validate(declaration.ToRequest()));
        }

        if (ReserveJar?.Error(now) is var (jarKey, jarMessage))
        {
            errors[$"reserveJar.{jarKey}"] = [jarMessage];
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

        var preparedFiles = new HashSet<(int, int, DeclarationType)>();
        for (var i = 0; i < DeclarationFiles.Length; i++)
        {
            var file = DeclarationFiles[i];
            if (file.Error() is var (fileKey, fileMessage))
            {
                errors[$"declarationFiles[{i}].{fileKey}"] = [fileMessage];
            }
            else if (!preparedFiles.Add((file.Year, file.Quarter, file.Type)))
            {
                errors[$"declarationFiles[{i}].type"] = ["A quarter keeps at most one file per declaration type."];
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

        var treasuryKinds = new HashSet<PaymentKind>();
        for (var i = 0; i < TreasuryAccounts.Length; i++)
        {
            var account = TreasuryAccounts[i];
            if (!treasuryKinds.Add(account.Kind))
            {
                errors[$"treasuryAccounts[{i}].kind"] = ["kind must differ from every other Treasury account's."];
            }

            if (account.Error() is var (accountKey, accountMessage))
            {
                errors[$"treasuryAccounts[{i}].{accountKey}"] = [accountMessage];
            }
        }

        var channelKinds = new HashSet<NotificationChannelKind>();
        for (var i = 0; i < NotificationChannels.Length; i++)
        {
            var channel = NotificationChannels[i];
            if (!channelKinds.Add(channel.Kind))
            {
                errors[$"notificationChannels[{i}].kind"] = ["kind must differ from every other channel's."];
            }

            if (channel.Error() is var (channelKey, channelMessage))
            {
                errors[$"notificationChannels[{i}].{channelKey}"] = [channelMessage];
            }
        }

        return errors.Count == 0 ? null : errors;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TreasuryAccountBackup(
    PaymentKind Kind,
    string? ManualIban,
    string? ManualRecipientName,
    string? ManualRecipientCode,
    DateTimeOffset? ManualUpdatedAt,
    string? LearnedIban,
    string? LearnedRecipientName,
    string? LearnedRecipientCode,
    string? LearnedExternalId,
    DateOnly? LearnedPaidOn,
    DateTimeOffset? LearnedAt,
    DateTimeOffset? NoticeAt)
{
    public static TreasuryAccountBackup From(TreasuryAccount row) => new(
        row.Kind,
        row.ManualIban,
        row.ManualRecipientName,
        row.ManualRecipientCode,
        row.ManualUpdatedAt,
        row.LearnedIban,
        row.LearnedRecipientName,
        row.LearnedRecipientCode,
        row.LearnedExternalId,
        row.LearnedPaidOn,
        row.LearnedAt,
        row.NoticeAt);

    // The column limits and check constraints of TreasuryAccountConfiguration, and the rules manual entry
    // and learning apply, so a restored account is one the endpoints could have produced.
    public (string Key, string Message)? Error()
    {
        if (!Enum.IsDefined(Kind))
        {
            return ("kind", "kind must be SingleTax, MilitaryLevy or Esv.");
        }

        var manual = new object?[] { ManualIban, ManualRecipientName, ManualRecipientCode, ManualUpdatedAt };
        if (manual.Any(value => value is null) && manual.Any(value => value is not null))
        {
            return ("manualIban", "The manual account needs an IBAN, name, code and time together.");
        }

        if (ManualIban is not null)
        {
            var request = new TreasuryAccountRequest(ManualIban, ManualRecipientName!, ManualRecipientCode!);
            if (TreasuryAccountsEndpoints.Normalize(request) != request
                || TreasuryAccountsEndpoints.Validate(request) is not null)
            {
                return ("manualIban", "The manual account must be a valid Treasury account in capitals without spaces, with a trimmed name and an 8-digit code.");
            }
        }

        var learned = new object?[] { LearnedIban, LearnedExternalId, LearnedPaidOn, LearnedAt };
        if (learned.Any(value => value is null) && learned.Any(value => value is not null))
        {
            return ("learnedIban", "The learned account needs an IBAN, operation, date and time together.");
        }

        if (LearnedIban is not null)
        {
            if (!TreasuryPayment.IsTreasury(LearnedIban) || TreasuryPayment.Normalize(LearnedIban) != LearnedIban)
            {
                return ("learnedIban", "learnedIban must be a Treasury IBAN in capitals without spaces.");
            }

            if (BackupDocument.ExternalIdError(LearnedExternalId!) is { } externalError)
            {
                return ("learnedExternalId", externalError.Replace("externalId", "learnedExternalId", StringComparison.Ordinal));
            }

            if (LearnedRecipientName is { Length: > TransactionsEndpoints.MaxClientNameLength }
                || (LearnedRecipientName is not null && TextRules.HasDisallowedControlChar(LearnedRecipientName)))
            {
                return ("learnedRecipientName", "learnedRecipientName must be short text without a control character.");
            }

            if (LearnedRecipientCode is not null
                && (LearnedRecipientCode.Length != TreasuryAccountsEndpoints.RecipientCodeLength || !LearnedRecipientCode.All(char.IsAsciiDigit)))
            {
                return ("learnedRecipientCode", "learnedRecipientCode must be 8 digits.");
            }
        }
        else if (LearnedRecipientName is not null || LearnedRecipientCode is not null)
        {
            return ("learnedIban", "Learned recipient details need a learned IBAN.");
        }

        return NoticeAt is not null && (ManualIban is null || LearnedIban is null)
            ? ("noticeAt", "A notice needs both a manual and a learned account.")
            : null;
    }

    public TreasuryAccount ToEntity(string userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Kind = Kind,
        ManualIban = ManualIban,
        ManualRecipientName = ManualRecipientName,
        ManualRecipientCode = ManualRecipientCode,
        ManualUpdatedAt = ManualUpdatedAt?.ToUniversalTime(),
        LearnedIban = LearnedIban,
        LearnedRecipientName = LearnedRecipientName,
        LearnedRecipientCode = LearnedRecipientCode,
        LearnedExternalId = LearnedExternalId,
        LearnedPaidOn = LearnedPaidOn,
        LearnedAt = LearnedAt?.ToUniversalTime(),
        NoticeAt = NoticeAt?.ToUniversalTime(),
    };
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
    string? CounterEdrpou,
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
        row.CounterEdrpou,
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
        { CounterEdrpou.Length: > TreasuryAccountsEndpoints.MaxEdrpouLength } =>
            ("counterEdrpou", $"counterEdrpou must not exceed {TreasuryAccountsEndpoints.MaxEdrpouLength} characters."),
        _ when CounterEdrpou is not null && TextRules.HasDisallowedControlChar(CounterEdrpou) =>
            ("counterEdrpou", "counterEdrpou must not contain a control character."),
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
        CounterEdrpou = CounterEdrpou,
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
    string TaxOfficeName,
    string[] KvedCodes,
    string Address)
{
    public static DeclarationDetailsBackup From(DeclarationDetails details) => new(
        details.TaxOfficeRegion, details.TaxOfficeDistrict, details.TaxOfficeName, details.KvedCodes, details.Address);

    public DeclarationDetailsRequest ToRequest() => DeclarationDetailsEndpoints.Normalize(
        new DeclarationDetailsRequest(TaxOfficeRegion, TaxOfficeDistrict, TaxOfficeName, KvedCodes, Address));

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

/// <summary>
/// A prepared declaration file and its annex 1 when it has one, carried byte for byte: they record what
/// the owner imported, so a restore does not regenerate them from figures that may since have changed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationFileBackup(
    int Year,
    int Quarter,
    DeclarationType Type,
    string FileName,
    byte[] Content,
    string? AnnexFileName,
    byte[]? AnnexContent,
    DateTimeOffset GeneratedAt)
{
    private const int MaxFileNameLength = 100;

    private const int MaxContentBytes = 1024 * 1024;

    public static DeclarationFileBackup From(DeclarationFile file) => new(
        file.Year, file.Quarter, file.Type, file.FileName, file.Content, file.AnnexFileName, file.AnnexContent, file.GeneratedAt);

    public (string Key, string Message)? Error() => this switch
    {
        { Year: < 1 or > 9998 } => ("year", "year must be 1 to 9998."),
        { Quarter: < 1 or > 4 } => ("quarter", "quarter must be 1 to 4."),
        { FileName.Length: 0 or > MaxFileNameLength } => ("fileName", $"fileName must be 1 to {MaxFileNameLength} characters."),
        _ when TextRules.HasDisallowedControlChar(FileName) => ("fileName", "fileName must not contain a control character."),
        _ when !FileName.EndsWith(".xml", StringComparison.Ordinal) => ("fileName", "fileName must end with .xml."),
        { Content.Length: 0 or > MaxContentBytes } => ("content", $"content must be 1 to {MaxContentBytes} bytes."),
        _ => AnnexError(),
    };

    private (string Key, string Message)? AnnexError() => (AnnexFileName, AnnexContent) switch
    {
        (null, null) => null,
        (null, _) or (_, null) => ("annexFileName", "annexFileName and annexContent must both be set or both be null."),
        ({ Length: 0 or > MaxFileNameLength }, _) => ("annexFileName", $"annexFileName must be 1 to {MaxFileNameLength} characters."),
        (var name, _) when TextRules.HasDisallowedControlChar(name) => ("annexFileName", "annexFileName must not contain a control character."),
        (var name, _) when !name.EndsWith(".xml", StringComparison.Ordinal) => ("annexFileName", "annexFileName must end with .xml."),
        (_, { Length: 0 or > MaxContentBytes }) => ("annexContent", $"annexContent must be 1 to {MaxContentBytes} bytes."),
        _ => null,
    };

    public DeclarationFile ToEntity(string userId) => new()
    {
        UserId = userId,
        Year = Year,
        Quarter = Quarter,
        Type = Type,
        FileName = FileName,
        Content = Content,
        AnnexFileName = AnnexFileName,
        AnnexContent = AnnexContent,
        GeneratedAt = GeneratedAt,
    };
}
