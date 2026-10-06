using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Backup;

/// <summary>
/// Everything one owner stored, as the file the owner downloads and restores. The shape is its own
/// versioned contract rather than the API's request records, so a request gaining a field cannot
/// silently change what schema version 1 means. TaxYearConfig, the limitation suspension and FxRates are
/// not here: all three are shared by every owner, and one owner's file must not rewrite another owner's
/// tax parameters.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed partial record BackupDocument(
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
    ReserveJarBackup? ReserveJar = null)
{
    // The only version a restore reads; an older file is refused as too old (#254). A new optional member goes
    // last in its record with a default, so a file written before it restores and the version stays. Removing,
    // renaming or retyping a member, changing what a value means, or adding a member with no default raises it
    // (docs/decisions.md, ADR-031 amendment of 2026-10-05).
    public const int CurrentSchemaVersion = 19;

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

    public static Issue? ExternalIdError(string externalId) => externalId switch
    {
        { Length: 0 or > MaxExternalIdLength } =>
            new Issue(ProblemCodes.TooLong, $"externalId must be 1 to {MaxExternalIdLength} characters."),
        _ when TextRules.HasDisallowedControlChar(externalId) =>
            new Issue(ProblemCodes.ControlCharacter, "externalId must not contain a control character."),
        _ => null,
    };
}
