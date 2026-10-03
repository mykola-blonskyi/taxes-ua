using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaxesUa.Api.Features.DatabaseBackups;

internal sealed class DatabaseBackupRunConfiguration : IEntityTypeConfiguration<DatabaseBackupRun>
{
    public void Configure(EntityTypeBuilder<DatabaseBackupRun> builder)
    {
        builder.HasKey(run => run.Id);

        builder.HasIndex(run => new { run.Job, run.FinishedAt });

        builder.Property(run => run.Job).HasConversion<string>().HasMaxLength(32);
        builder.Property(run => run.Detail).HasMaxLength(1000);
    }
}
