using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Backup;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TreasuryAccountBackup(
    PaymentKind Kind,
    string? ManualIban,
    string? ManualRecipientName,
    string? ManualRecipientCode,
    DateTimeOffset? ManualUpdatedAt,
    DateOnly? ManualValidUntil,
    string? LearnedIban,
    string? LearnedRecipientName,
    string? LearnedRecipientCode,
    string? LearnedExternalId,
    DateOnly? LearnedPaidOn,
    DateTimeOffset? LearnedAt,
    DateOnly? LearnedValidUntil,
    DateTimeOffset? NoticeAt)
{
    public static TreasuryAccountBackup From(TreasuryAccount row) => new(
        row.Kind,
        row.ManualIban,
        row.ManualRecipientName,
        row.ManualRecipientCode,
        row.ManualUpdatedAt,
        row.ManualValidUntil,
        row.LearnedIban,
        row.LearnedRecipientName,
        row.LearnedRecipientCode,
        row.LearnedExternalId,
        row.LearnedPaidOn,
        row.LearnedAt,
        row.LearnedValidUntil,
        row.NoticeAt);

    // The column limits and check constraints of TreasuryAccountConfiguration, and the rules manual entry
    // and learning apply, so a restored account is one the endpoints could have produced.
    public (string Key, Issue Issue)? Error()
    {
        if (!Enum.IsDefined(Kind))
        {
            return ("kind", new Issue(ProblemCodes.InvalidValue, "kind must be SingleTax, MilitaryLevy or Esv."));
        }

        var manual = new object?[] { ManualIban, ManualRecipientName, ManualRecipientCode, ManualUpdatedAt };
        if (manual.Any(value => value is null) && manual.Any(value => value is not null))
        {
            return ("manualIban", new Issue(ProblemCodes.InconsistentFields, "The manual account needs an IBAN, name, code and time together."));
        }

        if (ManualIban is null && ManualValidUntil is not null)
        {
            return ("manualValidUntil", new Issue(ProblemCodes.InconsistentFields, "manualValidUntil needs a manual account."));
        }

        if (LearnedIban is null && LearnedValidUntil is not null)
        {
            return ("learnedValidUntil", new Issue(ProblemCodes.InconsistentFields, "learnedValidUntil needs a learned account."));
        }

        if (TreasuryAccountsEndpoints.ValidUntilProblem(LearnedValidUntil) is { } learnedEndProblem)
        {
            return ("learnedValidUntil", learnedEndProblem);
        }

        if (ManualIban is not null)
        {
            var request = new TreasuryAccountRequest(ManualIban, ManualRecipientName!, ManualRecipientCode!, ManualValidUntil);
            if (TreasuryAccountsEndpoints.Normalize(request) != request
                || TreasuryAccountsEndpoints.Validate(request) is not null)
            {
                return ("manualIban", new Issue(ProblemCodes.InvalidValue, "The manual account must be a valid Treasury account in capitals without spaces, with a trimmed name and an 8-digit code."));
            }
        }

        var learned = new object?[] { LearnedIban, LearnedExternalId, LearnedPaidOn, LearnedAt };
        if (learned.Any(value => value is null) && learned.Any(value => value is not null))
        {
            return ("learnedIban", new Issue(ProblemCodes.InconsistentFields, "The learned account needs an IBAN, operation, date and time together."));
        }

        if (LearnedIban is not null)
        {
            if (!TreasuryPayment.IsTreasury(LearnedIban) || TreasuryPayment.Normalize(LearnedIban) != LearnedIban)
            {
                return ("learnedIban", new Issue(ProblemCodes.InvalidValue, "learnedIban must be a Treasury IBAN in capitals without spaces."));
            }

            if (BackupDocument.ExternalIdError(LearnedExternalId!) is { } externalError)
            {
                return ("learnedExternalId", externalError with { Message = externalError.Message.Replace("externalId", "learnedExternalId", StringComparison.Ordinal) });
            }

            if (LearnedRecipientName is { Length: > Limits.MaxClientNameLength }
                || (LearnedRecipientName is not null && TextRules.HasDisallowedControlChar(LearnedRecipientName)))
            {
                return ("learnedRecipientName", new Issue(ProblemCodes.ControlCharacter, "learnedRecipientName must be short text without a control character."));
            }

            if (LearnedRecipientCode is not null
                && (LearnedRecipientCode.Length != TreasuryAccountsEndpoints.RecipientCodeLength || !LearnedRecipientCode.All(char.IsAsciiDigit)))
            {
                return ("learnedRecipientCode", new Issue(ProblemCodes.InvalidValue, "learnedRecipientCode must be 8 digits."));
            }
        }
        else if (LearnedRecipientName is not null || LearnedRecipientCode is not null)
        {
            return ("learnedIban", new Issue(ProblemCodes.InconsistentFields, "Learned recipient details need a learned IBAN."));
        }

        return NoticeAt is not null && (ManualIban is null || LearnedIban is null)
            ? ("noticeAt", new Issue(ProblemCodes.InconsistentFields, "A notice needs both a manual and a learned account."))
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
        ManualValidUntil = ManualValidUntil,
        LearnedIban = LearnedIban,
        LearnedRecipientName = LearnedRecipientName,
        LearnedRecipientCode = LearnedRecipientCode,
        LearnedExternalId = LearnedExternalId,
        LearnedPaidOn = LearnedPaidOn,
        LearnedAt = LearnedAt?.ToUniversalTime(),
        LearnedValidUntil = LearnedValidUntil,
        NoticeAt = NoticeAt?.ToUniversalTime(),
    };
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

    public (string Key, Issue Issue)? OperationError(IReadOnlySet<Guid> accountIds) => this switch
    {
        { BankAccountId: null, ExternalId: not null } or { BankAccountId: not null, ExternalId: null } =>
            ("externalId", new Issue(ProblemCodes.InconsistentFields, "bankAccountId and externalId are set together or not at all.")),
        { BankAccountId: { } accountId } when !accountIds.Contains(accountId) =>
            ("bankAccountId", new Issue(ProblemCodes.UnknownReference, "bankAccountId must be the id of one of the bank accounts.")),
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
    public (string Key, Issue Issue)? Error(IReadOnlySet<Guid> accountIds) => this switch
    {
        _ when !accountIds.Contains(BankAccountId) =>
            ("bankAccountId", new Issue(ProblemCodes.UnknownReference, "bankAccountId must be the id of one of the bank accounts.")),
        _ when BackupDocument.ExternalIdError(ExternalId) is { } error => ("externalId", error),
        { AmountKop: <= 0 } => ("amountKop", new Issue(ProblemCodes.NotPositive, "amountKop must be positive.")),
        _ when !TreasuryPayment.IsTreasury(CounterIban) || TreasuryPayment.Normalize(CounterIban) != CounterIban =>
            ("counterIban", new Issue(ProblemCodes.InvalidValue, "counterIban must be a Treasury IBAN in capitals without spaces.")),
        { CounterName.Length: > Limits.MaxClientNameLength } =>
            ("counterName", new Issue(ProblemCodes.TooLong, $"counterName must not exceed {Limits.MaxClientNameLength} characters.")),
        { CounterEdrpou.Length: > TreasuryAccountsEndpoints.MaxEdrpouLength } =>
            ("counterEdrpou", new Issue(ProblemCodes.TooLong, $"counterEdrpou must not exceed {TreasuryAccountsEndpoints.MaxEdrpouLength} characters.")),
        _ when CounterEdrpou is not null && TextRules.HasDisallowedControlChar(CounterEdrpou) =>
            ("counterEdrpou", new Issue(ProblemCodes.ControlCharacter, "counterEdrpou must not contain a control character.")),
        { Purpose.Length: > TransactionsEndpoints.MaxDescriptionLength } =>
            ("purpose", new Issue(ProblemCodes.TooLong, $"purpose must not exceed {TransactionsEndpoints.MaxDescriptionLength} characters.")),
        _ when new[] { CounterName, Purpose }.Any(text => text is not null && TextRules.HasDisallowedControlChar(text)) =>
            ("purpose", new Issue(ProblemCodes.ControlCharacter, "counterName and purpose must not contain a control character.")),
        { Status: CandidateStatus.Confirmed, ConfirmedKind: null } or { Status: not CandidateStatus.Confirmed, ConfirmedKind: not null } =>
            ("confirmedKind", new Issue(ProblemCodes.InconsistentFields, "confirmedKind is set exactly when status is Confirmed.")),
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
    public (string Key, Issue Issue)? Error() => this switch
    {
        { ExternalId: { Length: 0 or > 200 } } => ("externalId", new Issue(ProblemCodes.TooLong, "externalId must be 1 to 200 characters.")),
        { Name.Length: > 200 } => ("name", new Issue(ProblemCodes.TooLong, "name must not exceed 200 characters.")),
        { Iban.Length: > 34 } => ("iban", new Issue(ProblemCodes.TooLong, "iban must not exceed 34 characters.")),
        { AccountType.Length: > 50 } => ("accountType", new Issue(ProblemCodes.TooLong, "accountType must not exceed 50 characters.")),
        _ when new[] { ExternalId, Name, Iban, AccountType }.Any(TextRules.HasDisallowedControlChar) =>
            ("externalId", new Issue(ProblemCodes.ControlCharacter, "A bank account's text must not contain a control character.")),
        { IsActive: true, IsFop: false } => ("isActive", new Issue(ProblemCodes.InconsistentFields, "Only a FOP account can be followed.")),
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
