using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Calendar;

/// <summary>
/// The secret behind one owner's calendar subscription URL (ADR-017). The owner has none until they ask
/// for one, and asking again replaces it. Kept as drawn, like the monobank webhook secret, because
/// settings shows the URL again; never audited and never in the backup.
/// </summary>
internal sealed class CalendarFeed
{
    public string UserId { get; set; } = string.Empty;

    // 32 random bytes as 64 lowercase hex characters, unique.
    public string Secret { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
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

        builder.HasIndex(feed => feed.Secret).IsUnique();

        builder.Property(feed => feed.Secret).HasMaxLength(64);
    }
}
