using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Settings;

/// <summary>
/// The owner's own requisites for invoices, one row per owner (knowledge/domain-model.md). The clause
/// initialisers are the defaults, so an owner who never edited a clause reads them from a fresh
/// instance, as <see cref="Settings"/> does for its own defaults.
/// </summary>
internal sealed class InvoicingDetails
{
    public string UserId { get; set; } = string.Empty;

    public string SellerNameUk { get; set; } = string.Empty;

    public string SellerNameEn { get; set; } = string.Empty;

    public string Rnokpp { get; set; } = string.Empty;

    public string AddressUk { get; set; } = string.Empty;

    public string AddressEn { get; set; } = string.Empty;

    public string AcceptanceClauseEn { get; set; } = InvoicingDefaults.AcceptanceEn;

    public string AcceptanceClauseUk { get; set; } = InvoicingDefaults.AcceptanceUk;

    public string FeesClauseEn { get; set; } = InvoicingDefaults.FeesEn;

    public string FeesClauseUk { get; set; } = InvoicingDefaults.FeesUk;

    public string TaxStatusClauseEn { get; set; } = InvoicingDefaults.TaxStatusEn;

    public string TaxStatusClauseUk { get; set; } = InvoicingDefaults.TaxStatusUk;

    public byte[]? SignatureImage { get; set; }

    public string? SignatureContentType { get; set; }

    public DateTimeOffset? SignatureUpdatedAt { get; set; }
}

/// <summary>Where the owner is paid in one currency. At most one row per owner and currency.</summary>
internal sealed class InvoicingPaymentDetails
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Currency Currency { get; set; }

    public string Iban { get; set; } = string.Empty;

    public string BeneficiaryBank { get; set; } = string.Empty;

    public string Swift { get; set; } = string.Empty;

    // Copied from the bank app as free text: the app never hard-codes what the bank may change.
    public string IntermediaryBank { get; set; } = string.Empty;

    public string IntermediarySwift { get; set; } = string.Empty;

    public string IntermediaryAccount { get; set; } = string.Empty;
}

internal static class InvoicingDefaults
{
    public const string AcceptanceEn =
        "Payment of this invoice constitutes acceptance of the services, which are deemed rendered in full and without claims.";

    public const string AcceptanceUk =
        "Оплата цього рахунку є прийняттям послуг, які вважаються наданими в повному обсязі та без претензій.";

    public const string FeesEn = "All bank fees and charges are borne by the payer.";

    public const string FeesUk = "Усі комісії банків та збори сплачує платник.";

    public const string TaxStatusEn = "The seller is a single tax payer and is not registered for VAT.";

    public const string TaxStatusUk = "Продавець є платником єдиного податку та не є платником ПДВ.";
}

internal sealed class InvoicingDetailsConfiguration : IEntityTypeConfiguration<InvoicingDetails>
{
    public void Configure(EntityTypeBuilder<InvoicingDetails> builder)
    {
        builder.HasKey(details => details.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<InvoicingDetails>(details => details.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(details => details.SignatureContentType).HasMaxLength(20);
    }
}

internal sealed class InvoicingPaymentDetailsConfiguration : IEntityTypeConfiguration<InvoicingPaymentDetails>
{
    public void Configure(EntityTypeBuilder<InvoicingPaymentDetails> builder)
    {
        builder.HasKey(details => details.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(details => details.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(details => new { details.UserId, details.Currency }).IsUnique();
    }
}
