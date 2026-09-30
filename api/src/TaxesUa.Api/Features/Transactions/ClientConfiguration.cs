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

        builder.Property(client => client.Name).HasMaxLength(ClientRules.MaxNameLength);
        builder.Property(client => client.Address).HasMaxLength(ClientRules.MaxAddressLength);
        builder.Property(client => client.Country).HasMaxLength(2);
        builder.Property(client => client.VatId).HasMaxLength(ClientRules.MaxVatIdLength);
        builder.Property(client => client.Email).HasMaxLength(ClientRules.MaxEmailLength);
        builder.Property(client => client.Notes).HasMaxLength(ClientRules.MaxNotesLength);

        builder.HasIndex(client => new { client.UserId, client.Name }).IsUnique();
    }
}
