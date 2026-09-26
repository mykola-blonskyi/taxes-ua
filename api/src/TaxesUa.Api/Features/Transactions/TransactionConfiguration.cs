using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Transactions;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(transaction => transaction.Id);

        // No navigation to ApplicationUser, same as SettingsConfiguration: nothing reads a
        // transaction through the user.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(transaction => transaction.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(transaction => new { transaction.UserId, transaction.ValueDate });

        builder.HasOne(transaction => transaction.Client)
            .WithMany()
            .HasForeignKey(transaction => transaction.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(transaction => transaction.RefundsTransaction)
            .WithMany()
            .HasForeignKey(transaction => transaction.RefundsTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(transaction => transaction.NonIncomeReason).HasMaxLength(1000);
        builder.Property(transaction => transaction.InvoiceNumber).HasMaxLength(100);
        builder.Property(transaction => transaction.Description).HasMaxLength(1000);
    }
}
