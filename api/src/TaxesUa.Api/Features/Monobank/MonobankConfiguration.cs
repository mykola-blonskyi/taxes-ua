using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Monobank;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(account => account.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(account => new { account.UserId, account.Bank, account.ExternalId }).IsUnique();

        builder.Property(account => account.ExternalId).HasMaxLength(200);
        builder.Property(account => account.Name).HasMaxLength(200);
        builder.Property(account => account.Iban).HasMaxLength(34);
        builder.Property(account => account.AccountType).HasMaxLength(50);
        builder.Property(account => account.LastFailure).HasConversion<string>().HasMaxLength(30);
    }
}

internal sealed class MonobankConnectionConfiguration : IEntityTypeConfiguration<MonobankConnection>
{
    public void Configure(EntityTypeBuilder<MonobankConnection> builder)
    {
        builder.HasKey(connection => connection.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<MonobankConnection>(connection => connection.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(connection => connection.MonobankClientId).HasMaxLength(100);
        builder.Property(connection => connection.WebhookSecret).HasMaxLength(64);
        builder.HasIndex(connection => connection.WebhookSecret).IsUnique();
        builder.Property(connection => connection.WebhookUrl).HasMaxLength(500);
        builder.Property(connection => connection.WebhookFailure).HasConversion<string>().HasMaxLength(30);
    }
}
