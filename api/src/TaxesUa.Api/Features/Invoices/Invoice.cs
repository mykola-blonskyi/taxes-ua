using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Transactions;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>
/// A bilingual invoice to one client (knowledge/domain-model.md). A draft reads the owner's details and
/// the client live; issuing numbers it and freezes <see cref="Snapshot"/>, and from then on only the
/// status and the cancel reason ever change.
/// </summary>
internal sealed class Invoice
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid ClientId { get; set; }

    public Client? Client { get; set; }

    public InvoiceStatus Status { get; set; }

    /// <summary>The year of the issue date once issued; with <see cref="NumberSequence"/> it is the number.</summary>
    public int? NumberYear { get; set; }

    public int? NumberSequence { get; set; }

    public DateOnly IssueDate { get; set; }

    public DateOnly DueDate { get; set; }

    public Currency Currency { get; set; }

    public InvoiceLine[] Lines { get; set; } = [];

    /// <summary>The sum of the lines' amounts, kept in step with <see cref="Lines"/> on every write.</summary>
    public long TotalMinor { get; set; }

    public InvoiceSnapshot? Snapshot { get; set; }

    public byte[]? SignatureImage { get; set; }

    public string? SignatureContentType { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? Number => NumberYear is { } year && NumberSequence is { } sequence
        ? InvoiceNumbers.Format(year, sequence)
        : null;
}

internal enum InvoiceStatus
{
    Draft,
    Issued,
    Cancelled,
}

internal enum InvoiceUnit
{
    Service,
    Hour,
    Day,
    Month,
}

/// <summary>One line: what was done, in both languages, and how much of it at what rate.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceLine(
    string DescriptionEn,
    string DescriptionUk,
    InvoiceUnit Unit,
    long QuantityThousandths,
    long RateMinor);

/// <summary>What the issued PDF prints about the parties and the payment, copied at issue.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceSnapshot(
    InvoiceSeller Seller,
    InvoiceBuyer Buyer,
    InvoicePayment Payment,
    InvoiceClauses Clauses);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceSeller(string NameUk, string NameEn, string Rnokpp, string AddressUk, string AddressEn);

/// <summary>The buyer as the client stood at issue. <c>CountryName</c> is frozen too, so a newer ICU cannot reword it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceBuyer(
    string Name,
    string Address,
    string Country,
    string CountryName,
    string? VatId,
    string? Email);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoicePayment(
    string Iban,
    string BeneficiaryBank,
    string Swift,
    string IntermediaryBank,
    string IntermediarySwift,
    string IntermediaryAccount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record InvoiceClauses(
    string AcceptanceEn,
    string AcceptanceUk,
    string FeesEn,
    string FeesUk,
    string TaxStatusEn,
    string TaxStatusUk);

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(invoice => invoice.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(invoice => invoice.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // An invoice never loses its buyer: a client with invoices cannot be deleted.
        builder.HasOne(invoice => invoice.Client)
            .WithMany()
            .HasForeignKey(invoice => invoice.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // An invoice with a receipt linked is issued, and an issued invoice is never deleted.
        builder.HasMany<Transaction>()
            .WithOne()
            .HasForeignKey(transaction => transaction.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(invoice => invoice.Number);

        // Backs the max-plus-one numbering: two issues that somehow raced past the owner's lock
        // cannot both commit the same number.
        builder.HasIndex(invoice => new { invoice.UserId, invoice.NumberYear, invoice.NumberSequence })
            .IsUnique()
            .HasFilter("\"NumberSequence\" IS NOT NULL");
        builder.HasIndex(invoice => new { invoice.UserId, invoice.IssueDate });

        builder.Property(invoice => invoice.Lines)
            .HasColumnType("jsonb")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, Json),
                json => JsonSerializer.Deserialize<InvoiceLine[]>(json, Json)!,
                new ValueComparer<InvoiceLine[]>(
                    (left, right) => left!.SequenceEqual(right!),
                    lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line)),
                    lines => lines.ToArray()));

        builder.Property(invoice => invoice.Snapshot)
            .HasColumnType("jsonb")
            .HasConversion(
                snapshot => JsonSerializer.Serialize(snapshot, Json),
                json => JsonSerializer.Deserialize<InvoiceSnapshot>(json, Json));

        builder.Property(invoice => invoice.SignatureContentType).HasMaxLength(20);
        builder.Property(invoice => invoice.CancelReason).HasMaxLength(InvoiceRules.MaxCancelReasonLength);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Invoices_Status",
            "(\"Status\" = 0 AND \"NumberSequence\" IS NULL AND \"Snapshot\" IS NULL AND \"CancelReason\" IS NULL) OR "
            + "(\"Status\" = 1 AND \"NumberSequence\" IS NOT NULL AND \"Snapshot\" IS NOT NULL AND \"CancelReason\" IS NULL) OR "
            + "(\"Status\" = 2 AND \"NumberSequence\" IS NOT NULL AND \"Snapshot\" IS NOT NULL AND \"CancelReason\" IS NOT NULL)"));
    }
}
