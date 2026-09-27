namespace TaxesUa.Api.Features.Audit;

/// <summary>
/// One recorded change to an audited record. Written only by <see cref="AuditSaveChangesInterceptor"/>
/// and never updated or deleted: a database trigger rejects both.
/// </summary>
internal sealed class AuditEntry
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }

    public AuditedEntity Entity { get; set; }

    public string EntityId { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }
}

internal enum AuditedEntity
{
    Transaction,
    BudgetPayment,
    Settings,
    TaxYearConfig,
}

internal enum AuditAction
{
    Create,
    Update,
    Delete,
}
