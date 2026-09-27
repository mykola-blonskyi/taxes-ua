using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace TaxesUa.Api.Features.Backup;

public static class BackupEndpoints
{
    // Years of an owner's ledger serialize to well under a megabyte, so this only keeps a wrong or
    // hostile file from being buffered whole. It sits below the web proxy's 10 MB body cap, so an
    // oversized file reaches this check and gets a 413 instead of a proxy 500.
    internal const int MaxRestoreBytes = 8 * 1024 * 1024;

    public static IEndpointRouteBuilder MapBackupApi(this IEndpointRouteBuilder routes)
    {
        var backup = routes.MapGroup("").WithTags("Backup").RequireAuthorization();

        backup.MapGet("/backup", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                IOptions<HttpJsonOptions> json,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var document = await LoadAsync(database, user.Id, cancellationToken);
                var options = new JsonSerializerOptions(json.Value.SerializerOptions) { WriteIndented = true };

                return Results.File(
                    JsonSerializer.SerializeToUtf8Bytes(document, options),
                    "application/json",
                    $"taxes-ua-backup-{time.TodayInKyiv():yyyy-MM-dd}.json");
            })
            .Produces<BackupDocument>(StatusCodes.Status200OK, "application/json")
            .Produces(StatusCodes.Status401Unauthorized);

        backup.MapPost("/restore", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                IOptions<HttpJsonOptions> json,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // Only a script can send this type cross-site, and that needs a CORS preflight the api
                // never grants, so a forged form post cannot replace the owner's data.
                if (!http.Request.HasJsonContentType())
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status415UnsupportedMediaType,
                        title: "A backup is sent as application/json.");
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var body = await ReadBoundedAsync(http.Request.Body, cancellationToken);
                if (body is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: $"A backup file must not exceed {MaxRestoreBytes / 1024 / 1024} MB.");
                }

                if (!TryParse(body, json.Value.SerializerOptions, out var document, out var reason))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: reason);
                }

                if (document.Validate(time.TodayInKyiv()) is { } errors)
                {
                    return Results.ValidationProblem(errors, title: "The backup file breaks the rules below.");
                }

                if (await ReplaceAsync(database, user.Id, document, cancellationToken) is { } linkErrors)
                {
                    return Results.ValidationProblem(linkErrors, title: "The backup file breaks the rules below.");
                }

                return Results.Ok(new RestoreResponse(
                    document.Clients.Length, document.Transactions.Length, document.BudgetPayments.Length));
            })
            .Accepts<BackupDocument>("application/json")
            .Produces<RestoreResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return routes;
    }

    // Ordered by columns the restore writes back, so a restored account serializes to the same bytes.
    private static async Task<BackupDocument> LoadAsync(
        AppDbContext database, string userId, CancellationToken cancellationToken)
    {
        var settings = await database.Settings.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var clients = await database.Clients.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Name)
            .ToListAsync(cancellationToken);
        var transactions = await database.Transactions.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.ValueDate)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var payments = await database.BudgetPayments.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.PaidOn)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return BackupDocument.From(settings, clients, transactions, payments);
    }

    // Returns the refund-link errors, having rolled everything back, or null once the owner's data is
    // replaced. Every statement is scoped to userId, and inserts never overwrite: an id another owner
    // already holds sends the whole file through fresh ids instead.
    private static async Task<Dictionary<string, string[]>?> ReplaceAsync(
        AppDbContext database, string userId, BackupDocument document, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Serializes restores per owner. Without it a second restore's delete misses the first one's
        // uncommitted rows, then sees them as another owner's ids and inserts the file a second time.
        await database.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({userId}))", cancellationToken);

        // One statement takes receipts and their refunds together: PostgreSQL checks the RESTRICT link
        // at the end of the statement, when neither side is left.
        await database.Transactions.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Clients.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.BudgetPayments.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Settings.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        var id = await IdMappingAsync(database, document, cancellationToken);
        if (document.Settings is { } settings)
        {
            database.Settings.Add(settings.ToEntity(userId));
        }

        database.Clients.AddRange(document.Clients.Select(client => client.ToEntity(userId, id)));
        var transactions = document.Transactions.Select(row => row.ToEntity(userId, id)).ToArray();
        database.Transactions.AddRange(transactions);
        database.BudgetPayments.AddRange(document.BudgetPayments.Select(payment => payment.ToEntity(userId, id)));
        await database.SaveChangesAsync(cancellationToken);

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < transactions.Length; i++)
        {
            var row = transactions[i];
            if (row.RefundsTransactionId is null)
            {
                continue;
            }

            var request = document.Transactions[i].ToRequest(clientName: null) with
            {
                RefundsTransactionId = row.RefundsTransactionId,
            };
            foreach (var (key, messages) in
                     await TransactionsEndpoints.ValidateLinksAsync(database, userId, row, request, cancellationToken) ?? [])
            {
                errors[$"transactions[{i}].{key}"] = messages;
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    // Runs after the owner's own rows are deleted under the owner's lock, so any id still present
    // belongs to another owner.
    private static async Task<Func<Guid, Guid>> IdMappingAsync(
        AppDbContext database, BackupDocument document, CancellationToken cancellationToken)
    {
        var ids = document.Ids().ToArray();
        var taken = await database.Clients.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.Transactions.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.BudgetPayments.AnyAsync(row => ids.Contains(row.Id), cancellationToken);
        if (!taken)
        {
            return fileId => fileId;
        }

        var fresh = ids.Distinct().ToDictionary(fileId => fileId, _ => Guid.NewGuid());
        return fileId => fresh[fileId];
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxRestoreBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    // The version is read before the shape, so a file from a newer build is named as such instead of
    // failing on whichever field it added.
    private static bool TryParse(
        byte[] body,
        JsonSerializerOptions options,
        [NotNullWhen(true)] out BackupDocument? document,
        [NotNullWhen(false)] out string? reason)
    {
        document = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number))
            {
                reason = "The file is not a taxes-ua backup: it has no schemaVersion.";
                return false;
            }

            if (number != BackupDocument.CurrentSchemaVersion)
            {
                reason = $"Backup schemaVersion {number} is not supported. This version restores schemaVersion "
                    + $"{BackupDocument.CurrentSchemaVersion}.";
                return false;
            }

            // An object root never deserializes to null.
            document = root.Deserialize<BackupDocument>(options)!;
            reason = null;
            return true;
        }
        catch (JsonException exception)
        {
            reason = $"The file is not a valid backup. {exception.Message}";
            return false;
        }
    }
}

internal sealed record RestoreResponse(int Clients, int Transactions, int BudgetPayments);
