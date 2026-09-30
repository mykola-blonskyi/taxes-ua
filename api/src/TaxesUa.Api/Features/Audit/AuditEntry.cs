using System.Text.Json;

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

    /// <summary>
    /// The one entry a restore from backup writes, in the same save as the rows it inserts. Its
    /// presence tells the interceptor to log that save by this summary alone: the rows are the file's
    /// state, not changes the owner made one by one.
    /// </summary>
    public static AuditEntry Restored(
        string userId, DateTimeOffset at, int clients, int transactions, int budgetPayments) => new()
    {
        UserId = userId,
        At = at,
        Entity = AuditedEntity.Backup,
        Action = AuditAction.Restore,
        After = JsonSerializer.Serialize(
            new { clients, transactions, budgetPayments }, JsonSerializerOptions.Web),
    };
}

internal enum AuditedEntity
{
    Transaction,
    BudgetPayment,
    Settings,
    InvoicingDetails,
    TaxYearConfig,
    Backup,
    Client,
    Invoice,
}

internal enum AuditAction
{
    Create,
    Update,
    Delete,
    Restore,
}
