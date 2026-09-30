using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Periods;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Backup;

/// <summary>
/// Merges a prototype export into the owner's data. Unlike a restore it never deletes or edits: a
/// record already present is skipped, so importing the same file twice changes nothing. What counts
/// as "already present" is a natural key rather than a stored source id, so the match survives a
/// backup and restore and also catches a receipt the owner already typed in by hand.
/// </summary>
public static class ImportEndpoints
{
    private static readonly PaymentKind[] Kinds = [PaymentKind.SingleTax, PaymentKind.MilitaryLevy, PaymentKind.Esv];

    public static IEndpointRouteBuilder MapImportApi(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/import/prototype", async (
                bool? dryRun,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // The same reason as the restore: only a script sends this type cross-site, and that
                // needs a CORS preflight the api never grants.
                if (!http.Request.HasJsonContentType())
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status415UnsupportedMediaType,
                        title: "A prototype file is sent as application/json.");
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var body = await BackupEndpoints.ReadBoundedAsync(http.Request.Body, cancellationToken);
                if (body is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: $"A prototype file must not exceed {BackupEndpoints.MaxRestoreBytes / 1024 / 1024} MB.");
                }

                var today = time.TodayInKyiv();
                PrototypeFile? file;
                Dictionary<string, string[]> errors;
                try
                {
                    using var json = JsonDocument.Parse(body);
                    file = PrototypeFile.Parse(json.RootElement, today, out errors);
                }
                catch (JsonException exception)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: $"The file is not valid JSON. {exception.Message}");
                }

                if (file is null)
                {
                    return Results.ValidationProblem(errors, title: "The prototype file breaks the rules below.");
                }

                return await ImportAsync(database, user.Id, file, today, time.GetUtcNow(), dryRun ?? false, cancellationToken) switch
                {
                    (_, { } importErrors) => Results.ValidationProblem(
                        importErrors, title: "The prototype file breaks the rules below."),
                    var (result, _) => Results.Ok(result),
                };
            })
            .WithTags("Backup")
            .RequireAuthorization()
            .Accepts<JsonElement>("application/json")
            .Produces<ImportResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return routes;
    }

    // A dry run takes the same path and rolls back, so the counts the owner confirms are the counts the
    // real import writes.
    private static async Task<(ImportResponse? Result, Dictionary<string, string[]>? Errors)> ImportAsync(
        AppDbContext database,
        string userId,
        PrototypeFile file,
        DateOnly today,
        DateTimeOffset now,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // The restore's lock, so an import and a restore of the same owner cannot interleave, and two
        // imports of one file cannot both see a record as missing.
        await database.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({userId}))", cancellationToken);

        var settings = await SettingsEndpoints.LoadOrDefaultAsync(database, userId, cancellationToken);
        if (file.PaidMonths.Length > 0 && settings.FopRegistrationDate is null)
        {
            return (null, new()
            {
                ["mpaid"] = ["Set the FOP registration date in Settings first: a paid month's amounts are computed from it."],
            });
        }

        var (added, present) = await AddIncomesAsync(database, userId, file.Incomes, now, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        var payments = await AddPaymentsAsync(database, userId, file.PaidMonths, today, now, cancellationToken);
        if (payments.Errors is { } errors)
        {
            return (null, errors);
        }

        await database.SaveChangesAsync(cancellationToken);
        if (!dryRun)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return (new ImportResponse(
            dryRun,
            added,
            present,
            payments.Added,
            payments.Present,
            payments.NothingDue), null);
    }

    // Counted as a multiset: a file with two identical receipts on one day imports both, and a second
    // import finds both and adds neither.
    private static async Task<(int Added, int Present)> AddIncomesAsync(
        AppDbContext database,
        string userId,
        PrototypeIncome[] incomes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dates = incomes.Select(income => income.Date).Distinct().ToArray();
        var stored = await database.Transactions
            .Where(row => row.UserId == userId && row.Kind == TransactionKind.Income && dates.Contains(row.ValueDate))
            .Select(row => new IncomeKey(
                row.ValueDate,
                row.Currency,
                row.AmountMinor,
                row.AmountUahKop,
                row.Client == null ? null : row.Client.Name))
            .ToListAsync(cancellationToken);
        var remaining = stored.CountBy(key => key).ToDictionary();

        // Loaded once and tracked: a lookup per receipt through the context would run change detection
        // over every row already added, and the change log finds tracked clients without a query.
        var clients = await database.Clients
            .Where(client => client.UserId == userId)
            .ToDictionaryAsync(client => client.Name, StringComparer.Ordinal, cancellationToken);

        var added = 0;
        foreach (var income in incomes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = income.ToRequest();
            var text = TransactionsEndpoints.Normalize(request);
            var key = new IncomeKey(income.Date, income.Currency, income.AmountMinor, income.UahKop, text.ClientName);
            if (remaining.TryGetValue(key, out var count) && count > 0)
            {
                remaining[key] = count - 1;
                continue;
            }

            Guid? clientId = null;
            if (text.ClientName is { } name)
            {
                if (!clients.TryGetValue(name, out var client))
                {
                    client = new Client { Id = Guid.NewGuid(), UserId = userId, Name = name };
                    database.Clients.Add(client);
                    clients[name] = client;
                }

                clientId = client.Id;
            }

            database.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ValueDate = income.Date,
                AmountMinor = income.AmountMinor,
                Currency = income.Currency,
                RateE4 = income.RateE4,
                RateSource = income.Currency == Currency.UAH ? null : RateSource.Manual,
                AmountUahKop = income.UahKop,
                Kind = TransactionKind.Income,
                ClientId = clientId,
                InvoiceNumber = text.InvoiceNumber,
                Description = text.Description,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        return (added, incomes.Length - added);
    }

    /// <summary>
    /// A prototype month marked paid becomes one month-period payment per kind, for what that month
    /// accrued (Rule 6's split), dated on its recommended advance date or today if that is later than
    /// today. A kind that already has a payment for the month is left alone, and a kind that accrued
    /// nothing (no income, or before registration) has nothing to pay.
    /// </summary>
    private static async Task<PaymentsResult> AddPaymentsAsync(
        AppDbContext database,
        string userId,
        PaidMonth[] months,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var years = months.Select(month => month.Year).Distinct().Order().ToArray();
        var stored = (await database.BudgetPayments
                .Where(row => row.UserId == userId && row.PeriodMonth != null && years.Contains(row.PeriodYear))
                .Select(row => new { row.Kind, row.PeriodYear, row.PeriodMonth })
                .ToListAsync(cancellationToken))
            .Select(row => (row.Kind, row.PeriodYear, row.PeriodMonth!.Value))
            .ToHashSet();

        var errors = new Dictionary<string, string[]>();
        var (added, present, nothingDue) = (0, 0, 0);
        foreach (var year in years)
        {
            var loaded = await YearAccruals.LoadAsync(database, userId, year, cancellationToken);
            if (loaded is null)
            {
                foreach (var month in months.Where(month => month.Year == year))
                {
                    errors[$"mpaid.{year}-{month.Month:00}"] =
                        [$"Tax year {year} has no parameters. Add them under Settings first."];
                }

                continue;
            }

            foreach (var month in months.Where(month => month.Year == year))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (loaded.Viewed.Accrual.Months.SingleOrDefault(accrual => accrual.Month == month.Month) is not { } accrued)
                {
                    nothingDue += Kinds.Length;
                    continue;
                }

                var recommended = new DateOnly(year, month.Month, 1).AddMonths(1)
                    .AddDays(loaded.Viewed.Config.AdvanceRecommendedDay - 1);
                var paidOn = recommended < today ? recommended : today;
                foreach (var kind in Kinds)
                {
                    var amountKop = kind switch
                    {
                        PaymentKind.SingleTax => accrued.SingleTaxKop,
                        PaymentKind.MilitaryLevy => accrued.MilitaryLevyKop,
                        _ => accrued.EsvKop,
                    };
                    if (stored.Contains((kind, year, month.Month)))
                    {
                        present++;
                        continue;
                    }

                    if (amountKop <= 0)
                    {
                        nothingDue++;
                        continue;
                    }

                    var request = new PaymentRequest(paidOn, kind, amountKop, year, null, month.Month, null);
                    if (PaymentsEndpoints.Validate(request) is { } invalid)
                    {
                        foreach (var (key, messages) in invalid)
                        {
                            errors[$"mpaid.{year}-{month.Month:00}.{key}"] = messages;
                        }

                        continue;
                    }

                    var row = new BudgetPayment { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now };
                    PaymentsEndpoints.Apply(row, request, now);
                    database.BudgetPayments.Add(row);
                    added++;
                }
            }
        }

        return new PaymentsResult(added, present, nothingDue, errors.Count == 0 ? null : errors);
    }

    private sealed record IncomeKey(
        DateOnly ValueDate, Currency Currency, long AmountMinor, long AmountUahKop, string? ClientName);

    private sealed record PaymentsResult(int Added, int Present, int NothingDue, Dictionary<string, string[]>? Errors);
}

/// <summary>
/// What an import wrote, or would write on a dry run. <c>PaymentsNothingDue</c> counts the kinds of a
/// paid month that accrued nothing, so the owner can tell why a month gave fewer than three payments.
/// </summary>
internal sealed record ImportResponse(
    bool DryRun,
    int TransactionsAdded,
    int TransactionsAlreadyPresent,
    int PaymentsAdded,
    int PaymentsAlreadyPresent,
    int PaymentsNothingDue);
