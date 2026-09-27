using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TaxesUa.Api.Features.Audit;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(entry => entry.Id);

        // No foreign key to the user: the log outlives what it describes, and a cascade would be a
        // DELETE the append-only trigger rejects.
        builder.HasIndex(entry => new { entry.UserId, entry.Entity, entry.EntityId, entry.At });
        builder.HasIndex(entry => new { entry.UserId, entry.At });

        // Stored as names, not the integers the other enums use: an entry is a historical record, and
        // reordering an enum member must not change what an old entry says.
        builder.Property(entry => entry.Entity).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.EntityId).HasMaxLength(64);

        builder.Property(entry => entry.Before).HasColumnType("jsonb");
        builder.Property(entry => entry.After).HasColumnType("jsonb");

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_AuditLog_Snapshots",
            "(\"Action\" = 'Create' AND \"Before\" IS NULL AND \"After\" IS NOT NULL) OR "
            + "(\"Action\" = 'Update' AND \"Before\" IS NOT NULL AND \"After\" IS NOT NULL) OR "
            + "(\"Action\" = 'Delete' AND \"Before\" IS NOT NULL AND \"After\" IS NULL) OR "
            + "(\"Action\" = 'Restore' AND \"Before\" IS NULL AND \"After\" IS NOT NULL)"));
    }
}
