using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The owner's record that a quarter's declaration was filed in the Cabinet, one per owner, year and
/// quarter (Rule 15). Nothing is filed from here; marking does not require readiness.
/// </summary>
internal sealed class DeclarationFiling
{
    public string UserId { get; set; } = string.Empty;

    public int Year { get; set; }

    public int Quarter { get; set; }

    public DateOnly FiledOn { get; set; }

    public DeclarationType Type { get; set; }

    /// <summary>
    /// Line 08 as it stood when marked. A later change to the quarter's income no longer matches it,
    /// which is what tells the owner the filed declaration may need a clarifying one.
    /// </summary>
    public long FiledIncomeKop { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The declaration's status, C_DOC_STAN 1, 2 and 3.</summary>
internal enum DeclarationType
{
    Reporting,
    NewReporting,
    Clarifying,
}

internal sealed class DeclarationFilingConfiguration : IEntityTypeConfiguration<DeclarationFiling>
{
    public void Configure(EntityTypeBuilder<DeclarationFiling> builder)
    {
        builder.HasKey(filing => new { filing.UserId, filing.Year, filing.Quarter });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(filing => filing.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeclarationFilings_Quarter", "\"Quarter\" BETWEEN 1 AND 4"));
    }
}
