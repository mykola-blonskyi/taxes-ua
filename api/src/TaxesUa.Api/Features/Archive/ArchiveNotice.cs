using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Archive;

/// <summary>
/// The owner's last answer to a year's archive notice: the year's ZIP downloaded, or the notice dismissed,
/// and when (Rule 19). One row per owner and year, overwritten by the next answer. An answer counts only
/// for a year finished no later than <c>At</c>: a ZIP taken before the last filing mark lacks that mark.
/// </summary>
internal sealed class ArchiveNotice
{
    public string UserId { get; set; } = string.Empty;

    public int Year { get; set; }

    public ArchiveNoticeOutcome Outcome { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>
    /// Records <paramref name="outcome"/> in one statement, so a download and a dismissal at once leave one
    /// row whichever lands second.
    /// </summary>
    public static Task RecordAsync(
        AppDbContext database,
        string userId,
        int year,
        ArchiveNoticeOutcome outcome,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ArchiveNotices" ("UserId", "Year", "Outcome", "At")
            VALUES ({userId}, {year}, {outcome.ToString()}, {at})
            ON CONFLICT ("UserId", "Year") DO UPDATE SET "Outcome" = EXCLUDED."Outcome", "At" = EXCLUDED."At"
            """,
            cancellationToken);
}

internal enum ArchiveNoticeOutcome
{
    Downloaded,
    Dismissed,
}

internal sealed class ArchiveNoticeConfiguration : IEntityTypeConfiguration<ArchiveNotice>
{
    public void Configure(EntityTypeBuilder<ArchiveNotice> builder)
    {
        builder.HasKey(notice => new { notice.UserId, notice.Year });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(notice => notice.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(notice => notice.Outcome).HasConversion<string>().HasMaxLength(20);
    }
}
