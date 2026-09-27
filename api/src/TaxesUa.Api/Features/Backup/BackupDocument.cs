using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Fx;
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
    BudgetPaymentBackup[] BudgetPayments)
{
    public const int CurrentSchemaVersion = 1;

    public static BackupDocument From(
        SettingsEntity? settings,
        IEnumerable<Client> clients,
        IEnumerable<Transaction> transactions,
        IEnumerable<BudgetPayment> payments) => new(
        CurrentSchemaVersion,
        settings is null ? null : SettingsBackup.From(settings),
        [.. clients.Select(client => new ClientBackup(client.Id, client.Name))],
        [.. transactions.Select(TransactionBackup.From)],
        [.. payments.Select(BudgetPaymentBackup.From)]);

    public IEnumerable<Guid> Ids() =>
        Clients.Select(client => client.Id)
            .Concat(Transactions.Select(transaction => transaction.Id))
            .Concat(BudgetPayments.Select(payment => payment.Id));

    /// <summary>
    /// Every rule a row must meet that the file alone can answer, through the same validators the
    /// endpoints run. The refund links need the receipts' stored state, so
    /// <see cref="TransactionsEndpoints.ValidateLinksAsync"/> checks them after the rows are written.
    /// </summary>
    public Dictionary<string, string[]>? Validate()
    {
        // RespectNullableAnnotations checks members, not array elements.
        if (Array.Exists(Clients, row => row is null)
            || Array.Exists(Transactions, row => row is null)
            || Array.Exists(BudgetPayments, row => row is null))
        {
            return new() { ["file"] = ["clients, transactions and budgetPayments must not contain null."] };
        }

        var errors = new Dictionary<string, string[]>();

        void Undefined(string prefix, IEnumerable<string> fields)
        {
            foreach (var field in fields)
            {
                errors[$"{prefix}.{field}"] = [$"{field} is not a known value."];
            }
        }

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
            Undefined("settings", settings.UndefinedEnums());
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
            else if (!seenNames.Add(name))
            {
                errors[$"clients[{i}].name"] = ["name must differ from every other client's."];
            }
        }

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

            Undefined(at, transaction.UndefinedEnums());

            var request = transaction.ToRequest(clientName);
            var requestErrors = TransactionsEndpoints.Validate(request, TransactionsEndpoints.Normalize(request));
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
            if (!Enum.IsDefined(payment.Kind))
            {
                Undefined($"budgetPayments[{i}]", ["kind"]);
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

    // The enum converter accepts a comma list such as "Friday, Saturday" as a flags value no member has.
    public IEnumerable<string> UndefinedEnums()
    {
        if (!Enum.IsDefined(PaymentMode))
        {
            yield return "paymentMode";
        }

        if (!Enum.IsDefined(EsvRegistrationMonthPolicy))
        {
            yield return "esvRegistrationMonthPolicy";
        }

        if (!WeekendDays.All(Enum.IsDefined))
        {
            yield return "weekendDays";
        }
    }

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
        row.InvoiceNumber,
        row.Description,
        row.CreatedAt,
        row.UpdatedAt);

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

    public IEnumerable<string> UndefinedEnums()
    {
        if (!Enum.IsDefined(Currency))
        {
            yield return "currency";
        }

        if (!Enum.IsDefined(Kind))
        {
            yield return "kind";
        }

        if (RateSource is { } source && !Enum.IsDefined(source))
        {
            yield return "rateSource";
        }
    }

    public Transaction ToEntity(string userId, Func<Guid, Guid> id)
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
