using System.Text.Json.Serialization;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Settings;

namespace TaxesUa.Api.Features.Backup;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationDetailsBackup(
    int? TaxOfficeRegion,
    int? TaxOfficeDistrict,
    string TaxOfficeName,
    string[] KvedCodes,
    string Address,
    string FullName,
    string Phone,
    string ReportEmail)
{
    public static DeclarationDetailsBackup From(DeclarationDetails details) => new(
        details.TaxOfficeRegion, details.TaxOfficeDistrict, details.TaxOfficeName, details.KvedCodes, details.Address,
        details.FullName, details.Phone, details.ReportEmail);

    public DeclarationDetailsRequest ToRequest() => DeclarationDetailsEndpoints.Normalize(
        new DeclarationDetailsRequest(
            TaxOfficeRegion, TaxOfficeDistrict, TaxOfficeName, KvedCodes, Address, FullName, Phone, ReportEmail));

    public DeclarationDetails ToEntity(string userId)
    {
        var details = new DeclarationDetails { UserId = userId };
        DeclarationDetailsEndpoints.Apply(details, ToRequest());
        return details;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationFilingBackup(
    int Year,
    int Quarter,
    DateOnly FiledOn,
    DeclarationType Type,
    long FiledIncomeKop,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DeclarationFilingBackup From(DeclarationFiling filing) => new(
        filing.Year,
        filing.Quarter,
        filing.FiledOn,
        filing.Type,
        filing.FiledIncomeKop,
        filing.CreatedAt,
        filing.UpdatedAt);

    public DeclarationFiling ToEntity(string userId) => new()
    {
        UserId = userId,
        Year = Year,
        Quarter = Quarter,
        FiledOn = FiledOn,
        Type = Type,
        FiledIncomeKop = FiledIncomeKop,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}

/// <summary>
/// A prepared declaration file and its annex 1 when it has one, carried byte for byte: they record what
/// the owner imported, so a restore does not regenerate them from figures that may since have changed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeclarationFileBackup(
    int Year,
    int Quarter,
    DeclarationType Type,
    string FileName,
    byte[] Content,
    string? AnnexFileName,
    byte[]? AnnexContent,
    DateTimeOffset GeneratedAt)
{
    private const int MaxFileNameLength = 100;

    private const int MaxContentBytes = 1024 * 1024;

    public static DeclarationFileBackup From(DeclarationFile file) => new(
        file.Year, file.Quarter, file.Type, file.FileName, file.Content, file.AnnexFileName, file.AnnexContent, file.GeneratedAt);

    public (string Key, Issue Issue)? Error() => this switch
    {
        { Year: < 1 or > 9998 } => ("year", new Issue(ProblemCodes.OutOfRange, "year must be 1 to 9998.")),
        { Quarter: < 1 or > 4 } => ("quarter", new Issue(ProblemCodes.OutOfRange, "quarter must be 1 to 4.")),
        { FileName.Length: 0 or > MaxFileNameLength } => ("fileName", new Issue(ProblemCodes.TooLong, $"fileName must be 1 to {MaxFileNameLength} characters.")),
        _ when TextRules.HasDisallowedControlChar(FileName) => ("fileName", new Issue(ProblemCodes.ControlCharacter, "fileName must not contain a control character.")),
        _ when !FileName.EndsWith(".xml", StringComparison.Ordinal) => ("fileName", new Issue(ProblemCodes.InvalidValue, "fileName must end with .xml.")),
        { Content.Length: 0 or > MaxContentBytes } => ("content", new Issue(ProblemCodes.TooLong, $"content must be 1 to {MaxContentBytes} bytes.")),
        _ => AnnexError(),
    };

    private (string Key, Issue Issue)? AnnexError() => (AnnexFileName, AnnexContent) switch
    {
        (null, null) => null,
        (null, _) or (_, null) => ("annexFileName", new Issue(ProblemCodes.InconsistentFields, "annexFileName and annexContent must both be set or both be null.")),
        ({ Length: 0 or > MaxFileNameLength }, _) => ("annexFileName", new Issue(ProblemCodes.TooLong, $"annexFileName must be 1 to {MaxFileNameLength} characters.")),
        (var name, _) when TextRules.HasDisallowedControlChar(name) => ("annexFileName", new Issue(ProblemCodes.ControlCharacter, "annexFileName must not contain a control character.")),
        (var name, _) when !name.EndsWith(".xml", StringComparison.Ordinal) => ("annexFileName", new Issue(ProblemCodes.InvalidValue, "annexFileName must end with .xml.")),
        (_, { Length: 0 or > MaxContentBytes }) => ("annexContent", new Issue(ProblemCodes.TooLong, $"annexContent must be 1 to {MaxContentBytes} bytes.")),
        _ => null,
    };

    public DeclarationFile ToEntity(string userId) => new()
    {
        UserId = userId,
        Year = Year,
        Quarter = Quarter,
        Type = Type,
        FileName = FileName,
        Content = Content,
        AnnexFileName = AnnexFileName,
        AnnexContent = AnnexContent,
        GeneratedAt = GeneratedAt,
    };
}
