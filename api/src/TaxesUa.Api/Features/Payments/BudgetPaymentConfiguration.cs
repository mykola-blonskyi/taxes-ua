using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Features.Payments;

internal sealed class BudgetPaymentConfiguration : IEntityTypeConfiguration<BudgetPayment>
{
    public void Configure(EntityTypeBuilder<BudgetPayment> builder)
    {
        builder.HasKey(payment => payment.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(payment => payment.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(payment => new { payment.UserId, payment.PeriodYear });

        builder.Property(payment => payment.Note).HasMaxLength(1000);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(payment => payment.BankAccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(payment => new { payment.BankAccountId, payment.ExternalId }).IsUnique();
        builder.Property(payment => payment.ExternalId).HasMaxLength(200);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_BudgetPayments_OnePeriod",
                "(\"PeriodQuarter\" IS NULL) <> (\"PeriodMonth\" IS NULL)");
            table.HasCheckConstraint(
                "CK_BudgetPayments_PeriodQuarter",
                "\"PeriodQuarter\" IS NULL OR \"PeriodQuarter\" BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "CK_BudgetPayments_PeriodMonth",
                "\"PeriodMonth\" IS NULL OR \"PeriodMonth\" BETWEEN 1 AND 12");
            table.HasCheckConstraint("CK_BudgetPayments_AmountKop", "\"AmountKop\" > 0");
            table.HasCheckConstraint(
                "CK_BudgetPayments_BankOperation",
                "(\"BankAccountId\" IS NULL) = (\"ExternalId\" IS NULL)");
        });
    }
}
