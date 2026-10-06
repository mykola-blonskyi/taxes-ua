using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.TaxYears;

/// <summary>
/// The martial-law suspension of limitation periods that Rule 14's retention follows. It is one stretch
/// of days shared by every tax year and every user, like <see cref="TaxYearConfig"/>, so it is a single
/// row rather than a column per year. <c>End</c> is the last suspended day, null while it lasts.
/// </summary>
internal sealed class LimitationSuspensionConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public DateOnly Start { get; set; }

    public DateOnly? End { get; set; }

    public string Source { get; set; } = string.Empty;

    public LimitationSuspension ToEngineInput() => new(Start, End);
}

internal sealed class LimitationSuspensionConfigConfiguration : IEntityTypeConfiguration<LimitationSuspensionConfig>
{
    public void Configure(EntityTypeBuilder<LimitationSuspensionConfig> builder)
    {
        builder.HasKey(config => config.Id);
        builder.Property(config => config.Id).ValueGeneratedNever();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_LimitationSuspensionConfigs_Singleton", $"\"Id\" = {LimitationSuspensionConfig.SingletonId}");
            table.HasCheckConstraint("CK_LimitationSuspensionConfigs_EndAfterStart", "\"End\" IS NULL OR \"End\" >= \"Start\"");
        });

        // Rule 14's retention: Tax Code 102.9 stopped every period of the Code from 17 March 2022, the
        // day Law 2120-IX was published, and XX.10.69.9 did the same; both ended on 31 July 2023, when
        // Law 3219-IX put XX.10.69.36 in their place from 1 August 2023, which still stops the limitation
        // period and extends retention by it. No end is set in the law, so none is seeded.
        builder.HasData(new LimitationSuspensionConfig
        {
            Id = LimitationSuspensionConfig.SingletonId,
            Start = new DateOnly(2022, 3, 17),
            End = null,
            Source = "ПКУ п. 102.9 (ЗУ № 2120-IX, з 17.03.2022 по 31.07.2023), підп. 69.9 та 69.36 п. 69 підрозд. 10 розд. XX (ЗУ № 3219-IX, № 3453-IX)",
        });
    }
}
