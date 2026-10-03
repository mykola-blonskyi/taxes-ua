using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Declarations;

namespace TaxesUa.Api.Features.Settings;

/// <summary>
/// What the declaration's header needs beyond the invoicing requisites, one row per owner
/// (knowledge/domain-model.md). The RNOKPP is not repeated here: it is <see cref="InvoicingDetails"/>'
/// own, so the two cannot drift apart. The name is the invoicing name unless <see cref="FullName"/> is set.
/// </summary>
internal sealed class DeclarationDetails
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>The tax office's region code, C_REG.</summary>
    public int? TaxOfficeRegion { get; set; }

    /// <summary>The tax office's district code within the region, C_RAJ.</summary>
    public int? TaxOfficeDistrict { get; set; }

    /// <summary>The tax office's name as the declaration's header prints it, HSTI.</summary>
    public string TaxOfficeName { get; set; } = string.Empty;

    /// <summary>The first is the main activity.</summary>
    public string[] KvedCodes { get; set; } = [];

    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Surname, given name and patronymic as in the registration documents, HNAME. Empty means the
    /// invoicing name, which often has no patronymic.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The phone the header prints, HTEL, as +380 and nine digits; empty leaves it out.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>The email the header prints, HEMAIL; empty leaves it out. Not the email channel's address.</summary>
    public string ReportEmail { get; set; } = string.Empty;

    /// <summary>
    /// What the declaration still lacks, in the order of the form's header. Saving an incomplete set
    /// is allowed; completeness only decides the declaration's readiness (Rule 15). A stored KVED code the
    /// classifier does not know counts as missing, so no declaration is built with an empty name.
    /// </summary>
    public static DeclarationDetailField[] Missing(InvoicingDetails? invoicing, DeclarationDetails? details)
    {
        var missing = new List<DeclarationDetailField>();
        if (string.IsNullOrEmpty(details?.FullName) && string.IsNullOrEmpty(invoicing?.SellerNameUk))
        {
            missing.Add(DeclarationDetailField.Name);
        }

        if (string.IsNullOrEmpty(invoicing?.Rnokpp))
        {
            missing.Add(DeclarationDetailField.Rnokpp);
        }

        if (details?.TaxOfficeRegion is null || details.TaxOfficeName.Length == 0)
        {
            missing.Add(DeclarationDetailField.TaxOffice);
        }

        if (details is null || details.KvedCodes.Length == 0 || details.KvedCodes.Any(code => Kved.Name(code) is null))
        {
            missing.Add(DeclarationDetailField.Kved);
        }

        if (string.IsNullOrEmpty(details?.Address))
        {
            missing.Add(DeclarationDetailField.Address);
        }

        return [.. missing];
    }
}

internal enum DeclarationDetailField
{
    Name,
    Rnokpp,
    TaxOffice,
    Kved,
    Address,
}

internal sealed class DeclarationDetailsConfiguration : IEntityTypeConfiguration<DeclarationDetails>
{
    public void Configure(EntityTypeBuilder<DeclarationDetails> builder)
    {
        builder.HasKey(details => details.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<DeclarationDetails>(details => details.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_DeclarationDetails_TaxOffice",
            "(\"TaxOfficeRegion\" IS NULL) = (\"TaxOfficeDistrict\" IS NULL)"));
    }
}
