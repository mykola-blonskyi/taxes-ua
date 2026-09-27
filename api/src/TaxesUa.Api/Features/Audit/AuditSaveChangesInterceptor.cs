using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;
using SettingsEntity = TaxesUa.Api.Features.Settings.Settings;

namespace TaxesUa.Api.Features.Audit;

/// <summary>
/// The one place a change to an audited record is logged, so a new endpoint cannot forget to. It only
/// sees what goes through the change tracker: an <c>ExecuteUpdate</c>, <c>ExecuteDelete</c> or raw SQL
/// against an audited table would bypass the log, so those tables are written through tracked
/// entities only (docs/architecture.md).
/// </summary>
internal sealed class AuditSaveChangesInterceptor(
    IHttpContextAccessor http,
    IOptions<IdentityOptions> identity,
    TimeProvider time) : SaveChangesInterceptor
{
    // Opt-in rather than every tracked type, so Identity's rows, which carry password hashes, security
    // stamps and passkeys, can never reach the log.
    private static readonly Dictionary<Type, AuditedEntity> Audited = new()
    {
        [typeof(Transaction)] = AuditedEntity.Transaction,
        [typeof(BudgetPayment)] = AuditedEntity.BudgetPayment,
        [typeof(SettingsEntity)] = AuditedEntity.Settings,
        [typeof(TaxYearConfig)] = AuditedEntity.TaxYearConfig,
    };

    internal static IReadOnlyCollection<Type> AuditedTypes => Audited.Keys;

    // The entry carries the owner and the time itself, and a bumped UpdatedAt alone is not a change.
    private static readonly HashSet<string> Omitted = ["UserId", "CreatedAt", "UpdatedAt"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && Pending(context).Any())
        {
            throw new InvalidOperationException(
                "Save audited changes with SaveChangesAsync: the log looks up a transaction's client name.");
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not { } context)
        {
            return result;
        }

        if (context.ChangeTracker.Entries<AuditEntry>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.Action == AuditAction.Restore))
        {
            return result;
        }

        var at = time.GetUtcNow();
        var log = new List<AuditEntry>();
        foreach (var (entry, entity) in Pending(context).ToList())
        {
            if (await ToAuditEntryAsync(context, entry, entity, at, cancellationToken) is { } audit)
            {
                log.Add(audit);
            }
        }

        context.AddRange(log);
        return result;
    }

    private static IEnumerable<(EntityEntry Entry, AuditedEntity Entity)> Pending(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && Audited.TryGetValue(entry.Metadata.ClrType, out var entity))
            {
                yield return (entry, entity);
            }
        }
    }

    private async Task<AuditEntry?> ToAuditEntryAsync(
        DbContext context,
        EntityEntry entry,
        AuditedEntity entity,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var (action, before, after) = entry.State switch
        {
            EntityState.Added => (AuditAction.Create, null, entry.CurrentValues),
            EntityState.Modified => (AuditAction.Update, entry.OriginalValues, entry.CurrentValues),
            _ => (AuditAction.Delete, entry.OriginalValues, (PropertyValues?)null),
        };

        var beforeJson = before is null ? null : await SnapshotAsync(context, entry, before, cancellationToken);
        var afterJson = after is null ? null : await SnapshotAsync(context, entry, after, cancellationToken);
        if (action == AuditAction.Update && beforeJson == afterJson)
        {
            return null;
        }

        var entityId = KeyOf(entry);

        return new AuditEntry
        {
            UserId = OwnerOf(entry) ?? throw new InvalidOperationException(
                $"{entity} {entityId} changed with no signed-in user to own its log entry."),
            At = at,
            Entity = entity,
            EntityId = entityId,
            Action = action,
            Before = beforeJson,
            After = afterJson,
        };
    }

    private static async Task<string> SnapshotAsync(
        DbContext context, EntityEntry entry, PropertyValues values, CancellationToken cancellationToken)
    {
        var key = entry.Metadata.FindPrimaryKey()!.Properties;
        var snapshot = new JsonObject();

        foreach (var property in values.Properties)
        {
            if (key.Contains(property) || Omitted.Contains(property.Name))
            {
                continue;
            }

            // A client id means nothing to the owner reading the log; the name is what they typed.
            if (entry.Entity is Transaction && property.Name == nameof(Transaction.ClientId))
            {
                snapshot["clientName"] = await ClientNameAsync(context, (Guid?)values[property], cancellationToken);
                continue;
            }

            snapshot[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] =
                JsonSerializer.SerializeToNode(values[property], property.ClrType, Json);
        }

        return snapshot.ToJsonString();
    }

    private static async Task<string?> ClientNameAsync(
        DbContext context, Guid? clientId, CancellationToken cancellationToken)
    {
        if (clientId is null)
        {
            return null;
        }

        var added = context.Set<Client>().Local.FirstOrDefault(client => client.Id == clientId);

        return added?.Name ?? await context.Set<Client>()
            .AsNoTracking()
            .Where(client => client.Id == clientId)
            .Select(client => client.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string KeyOf(EntityEntry entry) => string.Join(
        '/',
        entry.Metadata.FindPrimaryKey()!.Properties.Select(property =>
            Convert.ToString(entry.Property(property.Name).CurrentValue, CultureInfo.InvariantCulture)));

    // A record that belongs to a user is logged under that user. TaxYearConfig is one table for every
    // allowlisted user, so its entry belongs to whoever made the change.
    private string? OwnerOf(EntityEntry entry) =>
        entry.Metadata.FindProperty(nameof(AuditEntry.UserId)) is not null
            ? (string?)entry.Property(nameof(AuditEntry.UserId)).CurrentValue
            : http.HttpContext?.User.FindFirstValue(identity.Value.ClaimsIdentity.UserIdClaimType);
}
