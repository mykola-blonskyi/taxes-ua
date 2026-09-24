using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

var api = app.MapGroup("/api");
api.MapOpenApi("/openapi/{documentName}.json");

api.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var payload = new { status = dbOk ? "ok" : "degraded", database = dbOk };
    return dbOk ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();
