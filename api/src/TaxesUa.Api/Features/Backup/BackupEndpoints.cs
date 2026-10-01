using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Notifications;
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
                MonobankSyncQueue queue,
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

                if (document.Validate(time.TodayInKyiv(), time.GetUtcNow()) is { } errors)
                {
                    return Results.ValidationProblem(errors, title: "The backup file breaks the rules below.");
                }

                if (await ReplaceAsync(database, user.Id, document, time, cancellationToken) is { } linkErrors)
                {
                    return Results.ValidationProblem(linkErrors, title: "The backup file breaks the rules below.");
                }

                // The restore cleared every sync cursor; walking the history again brings back what the file lacks.
                await MonobankEndpoints.EnqueueFollowedAsync(database, queue, user.Id, cancellationToken);

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
        // Dismissed imports travel too, so a restore followed by a sync does not bring them back.
        var transactions = await database.Transactions.AsNoTracking()
            .IgnoreQueryFilters()
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

        var bankAccounts = await database.BankAccounts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Bank)
            .ThenBy(row => row.ExternalId)
            .ToListAsync(cancellationToken);
        var importBatches = await database.ImportBatches.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        // Resolved candidates travel too, so a restore followed by a sync does not offer them again.
        var candidates = await database.BudgetPaymentCandidates.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.BankTime)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var invoicingDetails = await database.InvoicingDetails.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var invoicingPayments = await database.InvoicingPaymentDetails.AsNoTracking()
            .Where(row => row.UserId == userId)
            .ToListAsync(cancellationToken);
        var invoices = await database.Invoices.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var declarationDetails = await database.DeclarationDetails.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var declarationFilings = await database.DeclarationFilings.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Year)
            .ThenBy(row => row.Quarter)
            .ToListAsync(cancellationToken);
        var declarationFiles = await database.DeclarationFiles.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Year)
            .ThenBy(row => row.Quarter)
            .ThenBy(row => row.Type)
            .ToListAsync(cancellationToken);

        var treasuryAccounts = await database.TreasuryAccounts.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Kind)
            .ToListAsync(cancellationToken);

        var notificationChannels = await database.NotificationChannels.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Kind)
            .ToListAsync(cancellationToken);

        var reserveJar = await database.ReserveJars.AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);

        // The monobank connection, and the token it holds, is never part of a backup (ADR-011). Nor is
        // a Telegram link code, which stays a secret of the running server.
        return BackupDocument.From(
            settings, clients, transactions, payments, bankAccounts, importBatches, candidates,
            invoicingDetails, invoicingPayments, invoices, declarationDetails, declarationFilings,
            declarationFiles, treasuryAccounts, notificationChannels, reserveJar);
    }

    // Returns the errors, having rolled everything back, or null once the owner's data is
    // replaced. Every statement is scoped to userId, and inserts never overwrite: an id another owner
    // already holds sends the whole file through fresh ids instead.
    private static async Task<Dictionary<string, string[]>?> ReplaceAsync(
        AppDbContext database,
        string userId,
        BackupDocument document,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Serializes restores per owner. Without it a second restore's delete misses the first one's
        // uncommitted rows, then sees them as another owner's ids and inserts the file a second time.
        await database.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({userId}))", cancellationToken);

        // An issued or cancelled invoice's number is already out in the world; dropping it would let the
        // next issue take it again (Rule 14).
        if (await MissingInvoiceNumbersAsync(database, userId, document, cancellationToken) is { Length: > 0 } missing)
        {
            return new Dictionary<string, string[]> { ["missingInvoices"] = missing };
        }

        // One statement takes receipts and their refunds together: PostgreSQL checks the RESTRICT link
        // at the end of the statement, when neither side is left.
        await database.Transactions.IgnoreQueryFilters()
            .Where(row => row.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await database.ImportBatches.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Invoices.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Clients.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.BudgetPayments.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.BudgetPaymentCandidates.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.TreasuryAccounts.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.NotificationChannels.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.NotificationLinkCodes.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.ReserveJars.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.Settings.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.InvoicingPaymentDetails.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.InvoicingDetails.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.DeclarationDetails.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.DeclarationFilings.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await database.DeclarationFiles.Where(row => row.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        var id = await IdMappingAsync(database, document, cancellationToken);
        var accountId = await MatchBankAccountsAsync(database, userId, document.BankAccounts, cancellationToken);
        if (document.Settings is { } settings)
        {
            database.Settings.Add(settings.ToEntity(userId));
        }

        if (document.InvoicingDetails is { } invoicing)
        {
            var (details, payments) = invoicing.ToEntities(userId);
            database.InvoicingDetails.Add(details);
            database.InvoicingPaymentDetails.AddRange(payments);
        }

        if (document.DeclarationDetails is { } declaration)
        {
            database.DeclarationDetails.Add(declaration.ToEntity(userId));
        }

        database.DeclarationFilings.AddRange(document.DeclarationFilings.Select(filing => filing.ToEntity(userId)));
        database.DeclarationFiles.AddRange(document.DeclarationFiles.Select(file => file.ToEntity(userId)));

        database.TreasuryAccounts.AddRange(document.TreasuryAccounts.Select(account => account.ToEntity(userId)));
        database.NotificationChannels.AddRange(document.NotificationChannels.Select(channel => channel.ToEntity(userId)));
        if (document.ReserveJar is { } reserveJar)
        {
            database.ReserveJars.Add(reserveJar.ToEntity(userId));
        }

        database.Clients.AddRange(document.Clients.Select(client => client.ToEntity(userId, id)));
        database.Invoices.AddRange(document.Invoices.Select(invoice => invoice.ToEntity(userId, id)));
        database.ImportBatches.AddRange(document.ImportBatches.Select(batch => batch.ToEntity(userId, id, accountId)));
        var transactions = document.Transactions.Select(row => row.ToEntity(userId, id, accountId)).ToArray();
        database.Transactions.AddRange(transactions);
        database.BudgetPayments.AddRange(
            document.BudgetPayments.Select(payment => payment.ToEntity(userId, id, accountId)));
        database.BudgetPaymentCandidates.AddRange(
            document.BudgetPaymentCandidates.Select(candidate => candidate.ToEntity(userId, id, accountId)));
        database.AuditLog.Add(AuditEntry.Restored(
            userId,
            time.GetUtcNow(),
            document.Clients.Length,
            transactions.Length,
            document.BudgetPayments.Length));
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

    private static async Task<string[]> MissingInvoiceNumbersAsync(
        AppDbContext database, string userId, BackupDocument document, CancellationToken cancellationToken)
    {
        var kept = document.Invoices
            .Where(invoice => invoice.NumberYear is not null && invoice.NumberSequence is not null)
            .Select(invoice => (invoice.NumberYear!.Value, invoice.NumberSequence!.Value))
            .ToHashSet();
        var owned = await database.Invoices
            .Where(row => row.UserId == userId && row.Status != InvoiceStatus.Draft)
            .Select(row => new { Year = row.NumberYear!.Value, Sequence = row.NumberSequence!.Value })
            .ToListAsync(cancellationToken);

        return [.. owned
            .Where(row => !kept.Contains((row.Year, row.Sequence)))
            .OrderBy(row => row.Year)
            .ThenBy(row => row.Sequence)
            .Select(row => InvoiceNumbers.Format(row.Year, row.Sequence))];
    }

    // Runs after the owner's own rows are deleted under the owner's lock, so any id still present
    // belongs to another owner.
    private static async Task<Func<Guid, Guid>> IdMappingAsync(
        AppDbContext database, BackupDocument document, CancellationToken cancellationToken)
    {
        var ids = document.Ids().ToArray();
        var taken = await database.Clients.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.Transactions.IgnoreQueryFilters().AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.BudgetPayments.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.ImportBatches.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.BudgetPaymentCandidates.AnyAsync(row => ids.Contains(row.Id), cancellationToken)
            || await database.Invoices.AnyAsync(row => ids.Contains(row.Id), cancellationToken);
        if (!taken)
        {
            return fileId => fileId;
        }

        var fresh = ids.Distinct().ToDictionary(fileId => fileId, _ => Guid.NewGuid());
        return fileId => fresh[fileId];
    }

    // Accounts are not replaced: the monobank connection reconciles them against the bank, and a sync
    // after reconnecting must find the rows its transactions point at. A file account the owner already
    // holds (same bank and external id) maps to that row; any other is inserted, under a
    // fresh id if the file's id is taken.
    private static async Task<Func<Guid, Guid>> MatchBankAccountsAsync(
        AppDbContext database, string userId, BankAccountBackup[] accounts, CancellationToken cancellationToken)
    {
        var owned = await database.BankAccounts
            .Where(row => row.UserId == userId)
            .ToListAsync(cancellationToken);

        // The restored transactions replace the synced ones, so no cursor can vouch for them; the next
        // sync walks again from the backfill start and the ExternalId check skips what the file holds.
        foreach (var row in owned)
        {
            row.SyncedThrough = null;
            row.HistoryImportedAt = null;
            row.LastFailedAt = null;
            row.LastFailure = null;
        }

        var fileIds = accounts.Select(account => account.Id).ToArray();
        var taken = await database.BankAccounts
            .Where(row => fileIds.Contains(row.Id))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, Guid>();
        foreach (var account in accounts)
        {
            var existing = owned.FirstOrDefault(row => row.Bank == account.Bank && row.ExternalId == account.ExternalId);
            if (existing is null)
            {
                existing = account.ToEntity(userId, taken.Contains(account.Id) ? Guid.NewGuid() : account.Id);
                database.BankAccounts.Add(existing);
            }

            map[account.Id] = existing.Id;
        }

        return fileId => map[fileId];
    }

    internal static async Task<byte[]?> ReadBoundedAsync(Stream body, CancellationToken cancellationToken)
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

            if (number is < 1 or > BackupDocument.CurrentSchemaVersion)
            {
                reason = $"Backup schemaVersion {number} is not supported. This version restores schemaVersion "
                    + $"1 to {BackupDocument.CurrentSchemaVersion}.";
                return false;
            }

            var upgraded = JsonNode.Parse(body)!.AsObject();
            BackupDocument.Upgrade(upgraded, number);

            // An object root never deserializes to null.
            document = upgraded.Deserialize<BackupDocument>(options)!;
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
