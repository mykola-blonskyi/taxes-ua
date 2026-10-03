using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Banking;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

/// <summary>
/// A settled debit from a followed FOP account to a Treasury account, waiting for the owner to say
/// what it paid (Rule 12). One row per (<see cref="BankAccountId"/>, <see cref="ExternalId"/>) whatever
/// its status, so a sync never brings back a candidate the owner confirmed or dismissed.
/// <see cref="ConfirmedKind"/> is set exactly when <see cref="Status"/> is <c>Confirmed</c>, and is
/// what the next candidate to the same <see cref="CounterIban"/> is suggested.
/// </summary>
internal sealed class BudgetPaymentCandidate
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid BankAccountId { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public DateTimeOffset BankTime { get; set; }

    public long AmountKop { get; set; }

    public string CounterIban { get; set; } = string.Empty;

    public string? CounterName { get; set; }

    // The counterparty's code as the bank sent it: the recipient code of the account a confirmation teaches.
    public string? CounterEdrpou { get; set; }

    // The bank's description and the payer's comment, as an imported transaction keeps them.
    public string? Purpose { get; set; }

    public CandidateStatus Status { get; set; }

    public PaymentKind? ConfirmedKind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateOnly PaidOn => BankTime.KyivDate();
}

internal enum CandidateStatus
{
    Pending,
    Confirmed,
    Dismissed,
}

internal sealed class BudgetPaymentCandidateConfiguration : IEntityTypeConfiguration<BudgetPaymentCandidate>
{
    public void Configure(EntityTypeBuilder<BudgetPaymentCandidate> builder)
    {
        builder.HasKey(candidate => candidate.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(candidate => candidate.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BankAccount>()
            .WithMany()
            .HasForeignKey(candidate => candidate.BankAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(candidate => new { candidate.BankAccountId, candidate.ExternalId }).IsUnique();
        builder.HasIndex(candidate => new { candidate.UserId, candidate.Status });

        builder.Ignore(candidate => candidate.PaidOn);
        builder.Property(candidate => candidate.ExternalId).HasMaxLength(200);
        builder.Property(candidate => candidate.CounterIban).HasMaxLength(34);
        builder.Property(candidate => candidate.CounterName).HasMaxLength(200);
        builder.Property(candidate => candidate.CounterEdrpou).HasMaxLength(TreasuryAccountsEndpoints.MaxEdrpouLength);
        builder.Property(candidate => candidate.Purpose).HasMaxLength(1000);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_BudgetPaymentCandidates_AmountKop", "\"AmountKop\" > 0");
            table.HasCheckConstraint(
                "CK_BudgetPaymentCandidates_ConfirmedKind",
                $"(\"Status\" = {(int)CandidateStatus.Confirmed}) = (\"ConfirmedKind\" IS NOT NULL)");
        });
    }
}
