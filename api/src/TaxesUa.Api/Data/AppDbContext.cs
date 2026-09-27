using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Data;

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Settings> Settings => Set<Settings>();

    public DbSet<TaxYearConfig> TaxYearConfigs => Set<TaxYearConfig>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<BudgetPayment> BudgetPayments => Set<BudgetPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each feature owns its own IEntityTypeConfiguration next to its entity, per ADR-008.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
