using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Monobank;

/// <summary>
/// The monobank jar one owner keeps for taxes (Rule 13): at most one row per owner. The title and the
/// balance are the bank's last answer, kept with <see cref="FetchedAt"/> so a stale balance says when it
/// was true. Only a UAH jar is ever stored, so the balance needs no conversion.
/// </summary>
internal sealed class ReserveJar
{
    public string UserId { get; set; } = string.Empty;

    // The jar's id in client-info; unlike the title it does not change when the owner renames the jar.
    public string JarId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long BalanceKop { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}

internal sealed class ReserveJarConfiguration : IEntityTypeConfiguration<ReserveJar>
{
    public const int MaxJarIdLength = 100;

    public const int MaxTitleLength = 200;

    public void Configure(EntityTypeBuilder<ReserveJar> builder)
    {
        builder.HasKey(jar => jar.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<ReserveJar>(jar => jar.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(jar => jar.JarId).HasMaxLength(MaxJarIdLength);
        builder.Property(jar => jar.Title).HasMaxLength(MaxTitleLength);
    }
}
