using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Notifications;

internal sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder)
    {
        builder.HasKey(channel => channel.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(channel => channel.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(channel => new { channel.UserId, channel.Kind }).IsUnique();

        builder.Property(channel => channel.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(channel => channel.Address).HasMaxLength(320);
        builder.Property(channel => channel.LastFailure).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class NotificationLinkCodeConfiguration : IEntityTypeConfiguration<NotificationLinkCode>
{
    public void Configure(EntityTypeBuilder<NotificationLinkCode> builder)
    {
        builder.HasKey(code => code.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(code => code.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(code => code.CodeHash).IsUnique();
        builder.HasIndex(code => code.UserId);

        builder.Property(code => code.Kind).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class TelegramPollStateConfiguration : IEntityTypeConfiguration<TelegramPollState>
{
    public void Configure(EntityTypeBuilder<TelegramPollState> builder)
    {
        builder.HasKey(state => state.BotId);
        builder.Property(state => state.BotId).ValueGeneratedNever();
    }
}

internal sealed class SentReminderConfiguration : IEntityTypeConfiguration<SentReminder>
{
    public void Configure(EntityTypeBuilder<SentReminder> builder)
    {
        builder.HasKey(sent => sent.Id);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(sent => sent.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(sent => new { sent.UserId, sent.Date, sent.Kinds, sent.Offset, sent.Channel })
            .IsUnique()
            .HasFilter("\"Incident\" = ''");
        builder.HasIndex(sent => new { sent.UserId, sent.Incident, sent.Channel })
            .IsUnique()
            .HasFilter("\"Incident\" <> ''");

        builder.Property(sent => sent.Incident).HasMaxLength(64).HasDefaultValue(string.Empty);

        builder.Property(sent => sent.Channel).HasConversion<string>().HasMaxLength(20);
    }
}
