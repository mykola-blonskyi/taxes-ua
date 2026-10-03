using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Transactions;

public static class ClientsEndpoints
{
    private const string DuplicateName = "A client with this name already exists.";

    public static IEndpointRouteBuilder MapClientsApi(this IEndpointRouteBuilder routes)
    {
        var clients = routes.MapGroup("/clients")
            .WithTags("Clients")
            .RequireAuthorization();

        clients.MapGet("", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var rows = await database.Clients
                    .Where(client => client.UserId == user.Id)
                    .OrderBy(client => client.Name)
                    .Select(client => new ClientResponse(
                        client.Id,
                        client.Name,
                        client.Address,
                        client.Country,
                        client.VatId,
                        client.Email,
                        client.DefaultCurrency,
                        client.Notes,
                        database.Transactions.Count(row => row.ClientId == client.Id)))
                    .ToArrayAsync(cancellationToken);

                return Results.Ok(rows);
            })
            .Produces<ClientResponse[]>()
            .Produces(StatusCodes.Status401Unauthorized);

        clients.MapPost("", async (
                ClientRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = request.Normalized();
                if (ClientRules.Validate(normalized) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                if (await NameTaken(database, user.Id, normalized.Name, except: null, cancellationToken))
                {
                    return DuplicateNameProblem();
                }

                var client = new Client { Id = Guid.NewGuid(), UserId = user.Id };
                normalized.ApplyTo(client);
                database.Clients.Add(client);

                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception)
                    when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return DuplicateNameProblem();
                }

                return Results.Created($"/api/clients/{client.Id}", ToResponse(client, receiptCount: 0));
            })
            .Produces<ClientResponse>(StatusCodes.Status201Created)
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        clients.MapPut("/{id:guid}", async (
                Guid id,
                ClientRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = request.Normalized();
                if (ClientRules.Validate(normalized) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var client = await database.Clients
                    .FirstOrDefaultAsync(row => row.Id == id && row.UserId == user.Id, cancellationToken);
                if (client is null)
                {
                    return Problems.NotFound(ProblemCodes.ClientNotFound, "client", id);
                }

                if (await NameTaken(database, user.Id, normalized.Name, except: id, cancellationToken))
                {
                    return DuplicateNameProblem();
                }

                normalized.ApplyTo(client);

                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception)
                    when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return DuplicateNameProblem();
                }

                var receiptCount = await database.Transactions.CountAsync(row => row.ClientId == id, cancellationToken);

                return Results.Ok(ToResponse(client, receiptCount));
            })
            .Produces<ClientResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound);

        clients.MapDelete("/{id:guid}", async (
                Guid id,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var client = await database.Clients
                    .FirstOrDefaultAsync(row => row.Id == id && row.UserId == user.Id, cancellationToken);
                if (client is null)
                {
                    return Problems.NotFound(ProblemCodes.ClientNotFound, "client", id);
                }

                var receiptCount = await database.Transactions.CountAsync(row => row.ClientId == id, cancellationToken);
                if (receiptCount > 0)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.ClientHasReceipts,
                        $"This client has {receiptCount} receipt(s) and cannot be deleted. Unlink or delete them first.");
                }

                var invoiceCount = await database.Invoices.CountAsync(row => row.ClientId == id, cancellationToken);
                if (invoiceCount > 0)
                {
                    return Problems.Create(
                        StatusCodes.Status409Conflict,
                        ProblemCodes.ClientHasInvoices,
                        $"This client has {invoiceCount} invoice(s) and cannot be deleted.");
                }

                database.Clients.Remove(client);
                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesCodedProblem(StatusCodes.Status404NotFound)
            .ProducesCodedProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static Task<bool> NameTaken(
        AppDbContext database, string userId, string name, Guid? except, CancellationToken cancellationToken) =>
        database.Clients.AnyAsync(
            client => client.UserId == userId && client.Name == name && client.Id != except, cancellationToken);

    private static IResult DuplicateNameProblem() =>
        Problems.Validation("name", ProblemCodes.NameTaken, DuplicateName);

    private static ClientResponse ToResponse(Client client, int receiptCount) => new(
        client.Id,
        client.Name,
        client.Address,
        client.Country,
        client.VatId,
        client.Email,
        client.DefaultCurrency,
        client.Notes,
        receiptCount);
}

internal sealed record ClientResponse(
    Guid Id,
    string Name,
    string? Address,
    string? Country,
    string? VatId,
    string? Email,
    Currency? DefaultCurrency,
    string? Notes,
    int ReceiptCount);
