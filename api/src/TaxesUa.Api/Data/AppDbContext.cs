using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;

namespace TaxesUa.Api.Data;

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Settings> Settings => Set<Settings>();

    public DbSet<TaxYearConfig> TaxYearConfigs => Set<TaxYearConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each feature owns its own IEntityTypeConfiguration next to its entity (ADR-008), so this
        // context aggregates the sets and nothing else.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
