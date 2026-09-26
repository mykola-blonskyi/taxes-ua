using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Settings;

internal sealed class SettingsConfiguration : IEntityTypeConfiguration<Settings>
{
    public void Configure(EntityTypeBuilder<Settings> builder)
    {
        // The Identity user's id is the key rather than a surrogate one, since there is exactly one
        // row per owner. No navigation property either: nothing reads settings through the user.
        builder.HasKey(settings => settings.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Settings>(settings => settings.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
