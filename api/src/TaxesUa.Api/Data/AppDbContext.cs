using Microsoft.EntityFrameworkCore;

namespace TaxesUa.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
