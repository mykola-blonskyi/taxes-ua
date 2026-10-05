using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Transactions;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

/// <summary>
/// Where the owner pays one kind of tax (Rule 12): one row per owner and kind. The Manual details are the
/// owner's own entry and the Learned details are the recipient of the latest operation the owner confirmed
/// for the kind; Manual, when present, is the account in use. Both are kept so that reverting to Learned
/// needs no second confirmation. <see cref="NoticeAt"/> is set while a confirmation went to another IBAN
/// than the Manual account and the owner has not dismissed the notice. Each account carries the owner's word on
/// when it ends (<see cref="ManualEnd"/>, <see cref="LearnedEnd"/>, Rule 16); the Learned end belongs to the
/// Learned IBAN and goes when another IBAN replaces it.
/// </summary>
internal sealed class TreasuryAccount
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public PaymentKind Kind { get; set; }

    public string? ManualIban { get; set; }

    public string? ManualRecipientName { get; set; }

    public string? ManualRecipientCode { get; set; }

    public DateTimeOffset? ManualUpdatedAt { get; set; }

    public DateOnly? ManualValidUntil { get; private set; }

    public bool ManualEndRemoved { get; private set; }

    public AccountEnd ManualEnd
    {
        get => AccountEnd.Of(ManualValidUntil, ManualEndRemoved);
        set => (ManualValidUntil, ManualEndRemoved) = value.Columns();
    }

    public string? LearnedIban { get; set; }

    public string? LearnedRecipientName { get; set; }

    public string? LearnedRecipientCode { get; set; }

    public string? LearnedExternalId { get; set; }

    public DateOnly? LearnedPaidOn { get; set; }

    public DateTimeOffset? LearnedAt { get; set; }

    public DateOnly? LearnedValidUntil { get; private set; }

    public bool LearnedEndRemoved { get; private set; }

    public AccountEnd LearnedEnd
    {
        get => AccountEnd.Of(LearnedValidUntil, LearnedEndRemoved);
        set => (LearnedValidUntil, LearnedEndRemoved) = value.Columns();
    }

    public DateTimeOffset? NoticeAt { get; set; }

    public bool IsManual => ManualIban is not null;
}

internal sealed class TreasuryAccountConfiguration : IEntityTypeConfiguration<TreasuryAccount>
{
    public void Configure(EntityTypeBuilder<TreasuryAccount> builder)
    {
        builder.HasKey(account => account.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(account => new { account.UserId, account.Kind }).IsUnique();

        builder.Ignore(account => account.IsManual);
        builder.Ignore(account => account.ManualEnd);
        builder.Ignore(account => account.LearnedEnd);
        builder.Property(account => account.ManualIban).HasMaxLength(34);
        builder.Property(account => account.ManualRecipientName).HasMaxLength(TreasuryAccountsEndpoints.MaxManualNameLength);
        builder.Property(account => account.ManualRecipientCode).HasMaxLength(TreasuryAccountsEndpoints.RecipientCodeLength);
        builder.Property(account => account.LearnedIban).HasMaxLength(34);
        builder.Property(account => account.LearnedRecipientName).HasMaxLength(Limits.MaxClientNameLength);
        builder.Property(account => account.LearnedRecipientCode).HasMaxLength(TreasuryAccountsEndpoints.RecipientCodeLength);
        builder.Property(account => account.LearnedExternalId).HasMaxLength(200);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_TreasuryAccounts_Manual",
                "(\"ManualIban\" IS NULL) = (\"ManualRecipientName\" IS NULL) "
                + "AND (\"ManualIban\" IS NULL) = (\"ManualRecipientCode\" IS NULL) "
                + "AND (\"ManualIban\" IS NULL) = (\"ManualUpdatedAt\" IS NULL) "
                + "AND (\"ManualIban\" IS NOT NULL OR (\"ManualValidUntil\" IS NULL AND NOT \"ManualEndRemoved\")) "
                + "AND NOT (\"ManualEndRemoved\" AND \"ManualValidUntil\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_TreasuryAccounts_Learned",
                "(\"LearnedIban\" IS NULL) = (\"LearnedExternalId\" IS NULL) "
                + "AND (\"LearnedIban\" IS NULL) = (\"LearnedPaidOn\" IS NULL) "
                + "AND (\"LearnedIban\" IS NULL) = (\"LearnedAt\" IS NULL) "
                + "AND (\"LearnedIban\" IS NOT NULL OR (\"LearnedRecipientName\" IS NULL AND \"LearnedRecipientCode\" IS NULL "
                + "AND \"LearnedValidUntil\" IS NULL AND NOT \"LearnedEndRemoved\")) "
                + "AND NOT (\"LearnedEndRemoved\" AND \"LearnedValidUntil\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_TreasuryAccounts_Notice",
                "\"NoticeAt\" IS NULL OR (\"ManualIban\" IS NOT NULL AND \"LearnedIban\" IS NOT NULL)");
        });
    }
}
