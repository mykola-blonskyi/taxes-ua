using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Features.Audit;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Monobank;
using TaxesUa.Api.Features.Payments;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Api.Features.TaxYears;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Data;

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Settings> Settings => Set<Settings>();

    public DbSet<InvoicingDetails> InvoicingDetails => Set<InvoicingDetails>();

    public DbSet<InvoicingPaymentDetails> InvoicingPaymentDetails => Set<InvoicingPaymentDetails>();

    public DbSet<TaxYearConfig> TaxYearConfigs => Set<TaxYearConfig>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<BudgetPayment> BudgetPayments => Set<BudgetPayment>();

    public DbSet<BudgetPaymentCandidate> BudgetPaymentCandidates => Set<BudgetPaymentCandidate>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<MonobankConnection> MonobankConnections => Set<MonobankConnection>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ForeignDebit> ForeignDebits => Set<ForeignDebit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each feature owns its own IEntityTypeConfiguration next to its entity, per ADR-008.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
