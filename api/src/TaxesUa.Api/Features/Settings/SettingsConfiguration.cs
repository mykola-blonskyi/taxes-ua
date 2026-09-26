using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Settings;

internal sealed class SettingsConfiguration : IEntityTypeConfiguration<Settings>
{
    public void Configure(EntityTypeBuilder<Settings> builder)
    {
        // One row per owner, so the Identity user's id is also this row's key. No navigation
        // property: nothing reads settings through the user.
        builder.HasKey(settings => settings.UserId);

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Settings>(settings => settings.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
