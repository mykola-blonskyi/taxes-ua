using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Backup;

internal sealed partial record BackupDocument
{
    /// <summary>
    /// Every rule a row must meet that the file alone can answer, through the same validators the
    /// endpoints run. The refund links need the receipts' stored state, so
    /// <see cref="TransactionsEndpoints.ValidateLinksAsync"/> checks them after the rows are written.
    /// </summary>
    public FieldErrors? Validate(DateOnly today, DateTimeOffset now)
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
            var nulls = new FieldErrors();
            nulls.Set(
                "file",
                ProblemCodes.NullItem,
                "clients, transactions, budgetPayments, bankAccounts, importBatches, budgetPaymentCandidates, invoices, declarationFilings, declarationFiles, treasuryAccounts and notificationChannels must not contain null.");

            return nulls;
        }

        var errors = new FieldErrors();

        void Merge(string prefix, FieldErrors? found)
        {
            if (found is not null)
            {
                errors.Merge(prefix, found);
            }
        }

        if (Settings is { } settings)
        {
            Merge("settings", SettingsEndpoints.Validate(settings.ToRequest()));
            Merge("settings", DpsStatusEndpoints.Validate(settings.ToDpsStatusRequest(), settings.FopRegistrationDate, today));
        }

        if (InvoicingDetails is { } invoicing)
        {
            Merge("invoicingDetails", invoicing.Validate());
        }

        if (DeclarationDetails is { } declaration)
        {
            Merge("declarationDetails", DeclarationDetailsEndpoints.Validate(declaration.ToRequest(), requireKnownKved: false));
        }

        if (ReserveJar?.Error(now) is var (jarKey, jarIssue))
        {
            errors.Set($"reserveJar.{jarKey}", jarIssue);
        }

        var filedQuarters = new HashSet<(int, int)>();
        for (var i = 0; i < DeclarationFilings.Length; i++)
        {
            var filing = DeclarationFilings[i];
            Merge($"declarationFilings[{i}]", DeclarationsEndpoints.ValidateFiling(filing.Year, filing.Quarter, filing.FiledOn, today));
            if (!filedQuarters.Add((filing.Year, filing.Quarter)))
            {
                errors.Set(
                    $"declarationFilings[{i}].quarter",
                    ProblemCodes.DuplicateValue,
                    "A quarter is marked filed at most once.");
            }
        }

        var preparedFiles = new HashSet<(int, int, DeclarationType)>();
        for (var i = 0; i < DeclarationFiles.Length; i++)
        {
            var file = DeclarationFiles[i];
            if (file.Error() is var (fileKey, fileIssue))
            {
                errors.Set($"declarationFiles[{i}].{fileKey}", fileIssue);
            }
            else if (!preparedFiles.Add((file.Year, file.Quarter, file.Type)))
            {
                errors.Set(
                    $"declarationFiles[{i}].type",
                    ProblemCodes.DuplicateValue,
                    "A quarter keeps at most one file per declaration type.");
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
                errors.Set(
                    $"clients[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other client has.");
            }

            Merge($"clients[{i}]", ClientRules.Validate(normalized));
            if (name.Length > 0 && !seenNames.Add(name))
            {
                errors.Set(
                    $"clients[{i}].name",
                    ProblemCodes.DuplicateValue,
                    "name must differ from every other client's.");
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
                errors.Set(
                    $"invoices[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other invoice has.");
            }
            else
            {
                invoicesById[invoice.Id] = invoice;
            }

            if (!clientNames.ContainsKey(invoice.ClientId))
            {
                errors.Set(
                    $"invoices[{i}].clientId",
                    ProblemCodes.UnknownReference,
                    "clientId must be the id of one of the clients.");
            }

            Merge($"invoices[{i}]", invoice.Validate());
            if (invoice is { NumberYear: { } year, NumberSequence: { } sequence } && !invoiceNumbers.Add((year, sequence)))
            {
                errors.Set(
                    $"invoices[{i}].numberSequence",
                    ProblemCodes.DuplicateValue,
                    "The invoice number must differ from every other invoice's.");
            }
        }

        var accountIds = new HashSet<Guid>();
        var accountKeys = new HashSet<(Bank, string)>();
        for (var i = 0; i < BankAccounts.Length; i++)
        {
            var account = BankAccounts[i];
            if (account.Id == Guid.Empty || !accountIds.Add(account.Id))
            {
                errors.Set(
                    $"bankAccounts[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other bank account has.");
            }

            if (account.Error() is var (key, issue))
            {
                errors.Set($"bankAccounts[{i}].{key}", issue);
            }
            else if (!accountKeys.Add((account.Bank, account.ExternalId)))
            {
                errors.Set(
                    $"bankAccounts[{i}].externalId",
                    ProblemCodes.DuplicateValue,
                    "externalId must differ from every other account's of the same bank.");
            }
        }

        var batchAccounts = new Dictionary<Guid, Guid>();
        for (var i = 0; i < ImportBatches.Length; i++)
        {
            var batch = ImportBatches[i];
            if (batch.Id == Guid.Empty || !batchAccounts.TryAdd(batch.Id, batch.BankAccountId))
            {
                errors.Set(
                    $"importBatches[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other import batch has.");
            }

            if (!accountIds.Contains(batch.BankAccountId))
            {
                errors.Set(
                    $"importBatches[{i}].bankAccountId",
                    ProblemCodes.UnknownReference,
                    "bankAccountId must be the id of one of the bank accounts.");
            }
            else if (batch.ImportedCount < 0 || batch.SkippedCount < 0 || batch.From > batch.To)
            {
                errors.Set(
                    $"importBatches[{i}].importedCount",
                    ProblemCodes.InvalidValue,
                    "An import batch has a window from before to and counts of zero or more.");
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
                errors.Set($"{at}.id", ProblemCodes.IdNotUnique, "id must be a non-empty id no other transaction has.");
            }

            string? clientName = null;
            if (transaction.ClientId is { } clientId && !clientNames.TryGetValue(clientId, out clientName))
            {
                errors.Set(
                    $"{at}.clientId",
                    ProblemCodes.UnknownReference,
                    "clientId must be the id of one of the clients.");
            }

            if (transaction.RefundsTransactionId is { } receiptId && !transactionIds.Contains(receiptId))
            {
                errors.Set(
                    $"{at}.refundsTransactionId",
                    ProblemCodes.UnknownReference,
                    "refundsTransactionId must be the id of one of the transactions.");
            }
            else if (transaction.RefundsTransactionId is { } linkedId && !receiptIds.Contains(linkedId))
            {
                // ValidateLinksAsync says the same after the insert, but two refunds linking each other
                // are a cycle EF cannot order, so the insert itself would fail first.
                errors.Set(
                    $"{at}.refundsTransactionId",
                    ProblemCodes.UnknownReference,
                    "refundsTransactionId must be the id of an Income transaction.");
            }

            if (transaction.InvoiceError(invoicesById) is { } invoiceError)
            {
                errors.Set($"{at}.invoiceId", invoiceError);
            }

            if (transaction.ImportError(accountIds, batchAccounts) is var (importKey, importIssue))
            {
                errors.Set($"{at}.{importKey}", importIssue);
            }
            else if (transaction is { BankAccountId: { } accountId, ExternalId: { } externalId }
                && !externalIds.Add((accountId, externalId)))
            {
                errors.Set(
                    $"{at}.externalId",
                    ProblemCodes.DuplicateValue,
                    "externalId must differ from every other transaction's of the same bank account.");
            }

            var request = transaction.ToRequest(clientName);
            var requestErrors = TransactionsEndpoints.Validate(request, TransactionsEndpoints.Normalize(request), today);
            Merge(at, requestErrors);

            if (requestErrors is null && transaction.RateError() is var (key, issue))
            {
                errors.Set($"{at}.{key}", issue);
            }
        }

        var seenPaymentIds = new HashSet<Guid>();
        var paymentOperations = new HashSet<(Guid, string)>();
        for (var i = 0; i < BudgetPayments.Length; i++)
        {
            var payment = BudgetPayments[i];
            if (payment.Id == Guid.Empty || !seenPaymentIds.Add(payment.Id))
            {
                errors.Set(
                    $"budgetPayments[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other payment has.");
            }

            if (payment.OperationError(accountIds) is var (key, issue))
            {
                errors.Set($"budgetPayments[{i}].{key}", issue);
            }
            else if (payment is { BankAccountId: { } accountId, ExternalId: { } externalId }
                && !paymentOperations.Add((accountId, externalId)))
            {
                errors.Set(
                    $"budgetPayments[{i}].externalId",
                    ProblemCodes.DuplicateValue,
                    "externalId must differ from every other payment's of the same bank account.");
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
                errors.Set(
                    $"budgetPaymentCandidates[{i}].id",
                    ProblemCodes.IdNotUnique,
                    "id must be a non-empty id no other candidate has.");
            }

            if (candidate.Error(accountIds) is var (key, issue))
            {
                errors.Set($"budgetPaymentCandidates[{i}].{key}", issue);
            }
            else if (!candidateOperations.Add((candidate.BankAccountId, candidate.ExternalId)))
            {
                errors.Set(
                    $"budgetPaymentCandidates[{i}].externalId",
                    ProblemCodes.DuplicateValue,
                    "externalId must differ from every other candidate's of the same bank account.");
            }
        }

        var treasuryKinds = new HashSet<PaymentKind>();
        for (var i = 0; i < TreasuryAccounts.Length; i++)
        {
            var account = TreasuryAccounts[i];
            if (!treasuryKinds.Add(account.Kind))
            {
                errors.Set(
                    $"treasuryAccounts[{i}].kind",
                    ProblemCodes.DuplicateValue,
                    "kind must differ from every other Treasury account's.");
            }

            if (account.Error() is var (accountKey, accountIssue))
            {
                errors.Set($"treasuryAccounts[{i}].{accountKey}", accountIssue);
            }
        }

        var channelKinds = new HashSet<NotificationChannelKind>();
        for (var i = 0; i < NotificationChannels.Length; i++)
        {
            var channel = NotificationChannels[i];
            if (!channelKinds.Add(channel.Kind))
            {
                errors.Set(
                    $"notificationChannels[{i}].kind",
                    ProblemCodes.DuplicateValue,
                    "kind must differ from every other channel's.");
            }

            if (channel.Error() is var (channelKey, channelIssue))
            {
                errors.Set($"notificationChannels[{i}].{channelKey}", channelIssue);
            }
        }

        return errors.OrNull();
    }
}
