using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;

namespace TaxesUa.Api.Features.Settings;

public static partial class DeclarationDetailsEndpoints
{
    private const int MaxKvedCodes = 20;

    private const int MaxAddressLength = 500;

    private const int MaxTaxOfficeNameLength = 200;

    public static IEndpointRouteBuilder MapDeclarationDetailsApi(this IEndpointRouteBuilder routes)
    {
        var declaration = routes.MapGroup("/settings/declaration").WithTags("Settings").RequireAuthorization();

        declaration.MapGet("", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var details = await database.DeclarationDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                var invoicing = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);

                return Results.Ok(ToResponse(invoicing, details ?? new DeclarationDetails { UserId = user.Id }));
            })
            .Produces<DeclarationDetailsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        declaration.MapPut("", async (
                DeclarationDetailsRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = Normalize(request);
                if (Validate(normalized) is { } errors)
                {
                    return Problems.Validation(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var details = await database.DeclarationDetails.FindAsync([user.Id], cancellationToken);
                if (details is null)
                {
                    details = new DeclarationDetails { UserId = user.Id };
                    database.DeclarationDetails.Add(details);
                }

                Apply(details, normalized);
                await database.SaveChangesAsync(cancellationToken);

                var invoicing = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);

                return Results.Ok(ToResponse(invoicing, details));
            })
            .Produces<DeclarationDetailsResponse>()
            .ProducesFieldProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    // RespectNullableAnnotations checks members, not array elements, so a null code is read as blank
    // and refused below rather than failing here.
    internal static DeclarationDetailsRequest Normalize(DeclarationDetailsRequest request) => request with
    {
        TaxOfficeName = request.TaxOfficeName.Trim(),
        KvedCodes = [.. request.KvedCodes.Select(code => code?.Trim() ?? string.Empty)],
        Address = request.Address.Trim(),
    };

    /// <summary>Validates a request already passed through <see cref="Normalize"/>.</summary>
    internal static FieldErrors? Validate(DeclarationDetailsRequest request)
    {
        var errors = new FieldErrors();

        if (request.TaxOfficeRegion is { } region && region is < 1 or > 99)
        {
            errors.Set("taxOfficeRegion", ProblemCodes.TaxOfficeRegionRange, "taxOfficeRegion must be 1 to 99.");
        }
        else if (request.TaxOfficeRegion is null && request.TaxOfficeDistrict is not null)
        {
            errors.Set(
                "taxOfficeRegion",
                ProblemCodes.TaxOfficePairIncomplete,
                "taxOfficeRegion is required with taxOfficeDistrict.");
        }

        if (request.TaxOfficeDistrict is { } district && district is < 0 or > 99)
        {
            errors.Set("taxOfficeDistrict", ProblemCodes.TaxOfficeDistrictRange, "taxOfficeDistrict must be 0 to 99.");
        }
        else if (request.TaxOfficeDistrict is null && request.TaxOfficeRegion is not null)
        {
            errors.Set(
                "taxOfficeDistrict",
                ProblemCodes.TaxOfficePairIncomplete,
                "taxOfficeDistrict is required with taxOfficeRegion.");
        }

        if (request.TaxOfficeName.Length > MaxTaxOfficeNameLength)
        {
            errors.Set(
                "taxOfficeName",
                ProblemCodes.TooLong,
                $"taxOfficeName must not exceed {MaxTaxOfficeNameLength} characters.");
        }
        else if (TextRules.HasDisallowedControlChar(request.TaxOfficeName))
        {
            errors.Set(
                "taxOfficeName",
                ProblemCodes.ControlCharacter,
                "taxOfficeName must not contain a control character.");
        }

        if (request.KvedCodes.Length > MaxKvedCodes)
        {
            errors.Set(
                "kvedCodes",
                ProblemCodes.KvedTooMany,
                $"kvedCodes must not list more than {MaxKvedCodes} codes.");
        }

        for (var i = 0; i < request.KvedCodes.Length; i++)
        {
            var code = request.KvedCodes[i];
            if (!KvedPattern().IsMatch(code))
            {
                errors.Set(
                    $"kvedCodes[{i}]",
                    ProblemCodes.KvedFormatInvalid,
                    "A KVED code is two digits, a dot and two digits, such as 62.01.");
            }
            else if (request.KvedCodes.Take(i).Contains(code))
            {
                errors.Set($"kvedCodes[{i}]", ProblemCodes.KvedDuplicate, "A KVED code must not repeat.");
            }
        }

        if (request.Address.Length > MaxAddressLength)
        {
            errors.Set("address", ProblemCodes.TooLong, $"address must not exceed {MaxAddressLength} characters.");
        }
        else if (TextRules.HasDisallowedControlChar(request.Address))
        {
            errors.Set("address", ProblemCodes.ControlCharacter, "address must not contain a control character.");
        }

        return errors.OrNull();
    }

    internal static void Apply(DeclarationDetails details, DeclarationDetailsRequest request)
    {
        details.TaxOfficeRegion = request.TaxOfficeRegion;
        details.TaxOfficeDistrict = request.TaxOfficeDistrict;
        details.TaxOfficeName = request.TaxOfficeName;
        details.KvedCodes = request.KvedCodes;
        details.Address = request.Address;
    }

    private static DeclarationDetailsResponse ToResponse(InvoicingDetails? invoicing, DeclarationDetails details) => new(
        invoicing?.SellerNameUk ?? string.Empty,
        invoicing?.Rnokpp ?? string.Empty,
        details.TaxOfficeRegion,
        details.TaxOfficeDistrict,
        details.TaxOfficeName,
        details.KvedCodes,
        details.Address,
        DeclarationDetails.Missing(invoicing, details));

    [GeneratedRegex("^[0-9]{2}\\.[0-9]{2}$")]
    private static partial Regex KvedPattern();
}

internal sealed record DeclarationDetailsRequest(
    int? TaxOfficeRegion,
    int? TaxOfficeDistrict,
    string TaxOfficeName,
    string[] KvedCodes,
    string Address);

/// <summary>
/// <c>Name</c> and <c>Rnokpp</c> are read-only here: they are the invoicing details' own
/// <c>SellerNameUk</c> and <c>Rnokpp</c>, sent so the form can show where they come from.
/// </summary>
internal sealed record DeclarationDetailsResponse(
    string Name,
    string Rnokpp,
    int? TaxOfficeRegion,
    int? TaxOfficeDistrict,
    string TaxOfficeName,
    string[] KvedCodes,
    string Address,
    DeclarationDetailField[] MissingDetails);
