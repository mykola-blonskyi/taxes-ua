using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Transactions;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(transaction => transaction.Id);

        // A dismissed import counts nowhere, so every read leaves it out unless it asks for it by name:
        // the sync's duplicate check, the currency-sale pairing, and backup and restore.
        builder.HasQueryFilter(transaction => transaction.ReviewStatus != ReviewStatus.Dismissed);

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

        // An invoice with a receipt linked is issued, and an issued invoice is never deleted.
        builder.HasOne(transaction => transaction.Invoice)
            .WithMany()
            .HasForeignKey(transaction => transaction.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(transaction => transaction.BankAccount)
            .WithMany()
            .HasForeignKey(transaction => transaction.BankAccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(transaction => transaction.ImportBatchId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(transaction => new { transaction.BankAccountId, transaction.ExternalId }).IsUnique();

        builder.Property(transaction => transaction.NonIncomeReason).HasMaxLength(1000);
        builder.Property(transaction => transaction.InvoiceNumber).HasMaxLength(100);
        builder.Property(transaction => transaction.Description).HasMaxLength(1000);
        builder.Property(transaction => transaction.ExternalId).HasMaxLength(200);
        builder.Property(transaction => transaction.Counterparty).HasMaxLength(200);
    }
}
