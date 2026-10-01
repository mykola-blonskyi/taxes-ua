using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The last F0103309 file generated for a quarter and declaration type, kept as the bytes the owner
/// downloaded, so a later change to the figures cannot silently rewrite what was imported. The year's
/// last group 3 declaration also keeps its annex 1 (F0133109): both annex columns are set or neither.
/// </summary>
internal sealed class DeclarationFile
{
    public string UserId { get; set; } = string.Empty;

    public int Year { get; set; }

    public int Quarter { get; set; }

    public DeclarationType Type { get; set; }

    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public string? AnnexFileName { get; set; }

    public byte[]? AnnexContent { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}

internal sealed class DeclarationFileConfiguration : IEntityTypeConfiguration<DeclarationFile>
{
    public void Configure(EntityTypeBuilder<DeclarationFile> builder)
    {
        builder.HasKey(file => new { file.UserId, file.Year, file.Quarter, file.Type });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(file => file.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_DeclarationFiles_Quarter", "\"Quarter\" BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "CK_DeclarationFiles_Annex", "(\"AnnexFileName\" IS NULL) = (\"AnnexContent\" IS NULL)");
        });
    }
}
