using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Transactions;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(client => client.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(client => client.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(client => client.Name).HasMaxLength(200);

        builder.HasIndex(client => new { client.UserId, client.Name }).IsUnique();
    }
}
