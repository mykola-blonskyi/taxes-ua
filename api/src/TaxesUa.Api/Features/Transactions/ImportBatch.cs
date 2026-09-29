using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Features.Transactions;

/// <summary>One import run for one account, per knowledge/domain-model.md.</summary>
internal sealed class ImportBatch
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ImportSource Source { get; set; }

    public Guid BankAccountId { get; set; }

    // The statement window the run read, as instants: a bank window is not a whole number of Kyiv days.
    public DateTimeOffset From { get; set; }

    public DateTimeOffset To { get; set; }

    public int ImportedCount { get; set; }

    public int SkippedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal enum ImportSource
{
    Monobank,
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.HasKey(batch => batch.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(batch => batch.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(batch => batch.BankAccountId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(batch => new { batch.BankAccountId, batch.CreatedAt });
    }
}
