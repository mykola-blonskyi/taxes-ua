using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Calendar;

/// <summary>
/// The secret behind one owner's calendar subscription URL (ADR-017). The owner has none until they ask
/// for one, and asking again replaces it. Only the secret's SHA-256 is kept (#256), so the URL is shown
/// once, in the answer that draws it; never audited and never in the backup.
/// </summary>
internal sealed class CalendarFeed
{
    public string UserId { get; set; } = string.Empty;

    // PathSecret.Hash of the path secret, unique.
    public string SecretHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class CalendarFeedConfiguration : IEntityTypeConfiguration<CalendarFeed>
{
    public void Configure(EntityTypeBuilder<CalendarFeed> builder)
    {
        builder.HasKey(feed => feed.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(feed => feed.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(feed => feed.SecretHash).IsUnique();

        builder.Property(feed => feed.SecretHash).HasMaxLength(PathSecret.HashLength);
    }
}
