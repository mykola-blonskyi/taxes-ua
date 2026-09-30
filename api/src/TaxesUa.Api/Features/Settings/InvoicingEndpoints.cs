using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Auth;
using TaxesUa.Api.Features.Fx;
using TaxesUa.Api.Features.Monobank;

namespace TaxesUa.Api.Features.Settings;

public static partial class InvoicingEndpoints
{
    internal const int MaxSignatureBytes = 512 * 1024;

    // Every monobank account is held at Universal Bank, so these are constants rather than data read
    // from the bank (issue #91).
    internal const string MonobankBeneficiaryBank = "JSC Universal Bank, Kyiv";

    internal const string MonobankSwift = "UNJSUAUKXXX";

    private const int MaxNameLength = 200;

    private const int MaxAddressLength = 500;

    private const int MaxClauseLength = 1000;

    private const int MaxBankFieldLength = 200;

    private const string ClientInfoMethod = "client-info";

    private static readonly string[] SignatureTypes = ["image/png", "image/jpeg"];

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    public static IEndpointRouteBuilder MapInvoicingApi(this IEndpointRouteBuilder routes)
    {
        var invoicing = routes.MapGroup("/settings/invoicing").WithTags("Invoicing").RequireAuthorization();

        invoicing.MapGet("", async (
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

                var details = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken)
                    ?? new InvoicingDetails { UserId = user.Id };
                var payments = await database.InvoicingPaymentDetails.AsNoTracking()
                    .Where(row => row.UserId == user.Id)
                    .ToListAsync(cancellationToken);

                return Results.Ok(ToResponse(details, payments));
            })
            .Produces<InvoicingDetailsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        invoicing.MapPut("", async (
                InvoicingDetailsRequest request,
                UserManager<ApplicationUser> users,
                AppDbContext database,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var normalized = Normalize(request);
                if (Validate(normalized) is { } errors)
                {
                    return Results.ValidationProblem(errors);
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var details = await database.InvoicingDetails.FindAsync([user.Id], cancellationToken);
                if (details is null)
                {
                    details = new InvoicingDetails { UserId = user.Id };
                    database.InvoicingDetails.Add(details);
                }

                Apply(details, normalized);

                var stored = await database.InvoicingPaymentDetails
                    .Where(row => row.UserId == user.Id)
                    .ToListAsync(cancellationToken);
                ApplyPayments(database, user.Id, stored, normalized.PaymentDetails);
                await database.SaveChangesAsync(cancellationToken);

                var saved = await database.InvoicingPaymentDetails.AsNoTracking()
                    .Where(row => row.UserId == user.Id)
                    .ToListAsync(cancellationToken);

                return Results.Ok(ToResponse(details, saved));
            })
            .Produces<InvoicingDetailsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        invoicing.MapGet("/signature", async (
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

                var details = await database.InvoicingDetails.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                if (details is not { SignatureImage: { } image, SignatureContentType: { } contentType })
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "private, no-cache";
                http.Response.Headers.XContentTypeOptions = "nosniff";

                return Results.File(image, contentType);
            })
            .Produces(StatusCodes.Status200OK, contentType: "image/png", additionalContentTypes: "image/jpeg")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        invoicing.MapPut("/signature", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TimeProvider time,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                // A raw image body, so a cross-site form cannot send it: the type needs a CORS preflight
                // the api never grants (as for the restore).
                var contentType = http.Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
                if (contentType is null || !SignatureTypes.Contains(contentType))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status415UnsupportedMediaType,
                        title: "A signature is a PNG or JPEG image.");
                }

                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var body = await ReadBoundedAsync(http.Request.Body, cancellationToken);
                if (body is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: $"A signature image must not exceed {MaxSignatureBytes / 1024} KB.");
                }

                if (SignatureError(body, contentType) is { } error)
                {
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: error);
                }

                var details = await database.InvoicingDetails.FindAsync([user.Id], cancellationToken);
                if (details is null)
                {
                    details = new InvoicingDetails { UserId = user.Id };
                    database.InvoicingDetails.Add(details);
                }

                details.SignatureImage = body;
                details.SignatureContentType = contentType;
                details.SignatureUpdatedAt = time.GetUtcNow();
                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .Accepts<byte[]>("image/png", "image/jpeg")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        invoicing.MapDelete("/signature", async (
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

                var details = await database.InvoicingDetails.FindAsync([user.Id], cancellationToken);
                if (details is { SignatureImage: not null })
                {
                    details.SignatureImage = null;
                    details.SignatureContentType = null;
                    details.SignatureUpdatedAt = null;
                    await database.SaveChangesAsync(cancellationToken);
                }

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        // A read that answers suggestions and stores nothing: the owner saves them, or does not.
        invoicing.MapPost("/prefill-from-monobank", async (
                UserManager<ApplicationUser> users,
                AppDbContext database,
                TokenEncryptor encryptor,
                MonobankRateGate gate,
                MonobankClient client,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var connection = await database.MonobankConnections.AsNoTracking()
                    .FirstOrDefaultAsync(row => row.UserId == user.Id, cancellationToken);
                if (connection is null || connection.RejectedAt is not null || !encryptor.IsConfigured)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "monobank is not connected.");
                }

                string token;
                try
                {
                    token = encryptor.Decrypt(connection.EncryptedToken);
                }
                catch (CryptographicException)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "The stored monobank token cannot be read. Connect monobank again.");
                }

                // The accounts are the stored rows; only the name needs the bank, and the gate keeps
                // this call inside monobank's limit of one client-info per minute.
                await gate.WaitTurnAsync(user.Id, ClientInfoMethod, cancellationToken);
                if (await client.GetClientInfoAsync(token, cancellationToken) is not ClientInfoResult.Found found)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status502BadGateway,
                        title: "monobank could not be reached for the name.");
                }

                var accounts = await database.BankAccounts.AsNoTracking()
                    .Where(account => account.UserId == user.Id && account.Bank == Bank.Monobank && account.IsFop)
                    .OrderByDescending(account => account.IsActive)
                    .ThenBy(account => account.ExternalId)
                    .ToListAsync(cancellationToken);

                var suggestions = new List<PaymentDetailsInput>();
                foreach (var account in accounts)
                {
                    if (IsoCurrency.FromNumeric(account.CurrencyCode) is not { } currency
                        || suggestions.Exists(suggestion => suggestion.Currency == currency))
                    {
                        continue;
                    }

                    suggestions.Add(new PaymentDetailsInput(
                        currency,
                        account.Iban,
                        MonobankBeneficiaryBank,
                        MonobankSwift,
                        string.Empty,
                        string.Empty,
                        string.Empty));
                }

                return Results.Ok(new MonobankPrefillResponse(
                    found.Info.Name.Trim(), [.. suggestions.OrderBy(suggestion => suggestion.Currency)]));
            })
            .Produces<MonobankPrefillResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return routes;
    }

    internal static InvoicingDetailsRequest Normalize(InvoicingDetailsRequest request) => request with
    {
        SellerNameUk = request.SellerNameUk.Trim(),
        SellerNameEn = request.SellerNameEn.Trim(),
        Rnokpp = request.Rnokpp.Trim(),
        AddressUk = request.AddressUk.Trim(),
        AddressEn = request.AddressEn.Trim(),
        AcceptanceClauseEn = request.AcceptanceClauseEn.Trim(),
        AcceptanceClauseUk = request.AcceptanceClauseUk.Trim(),
        FeesClauseEn = request.FeesClauseEn.Trim(),
        FeesClauseUk = request.FeesClauseUk.Trim(),
        TaxStatusClauseEn = request.TaxStatusClauseEn.Trim(),
        TaxStatusClauseUk = request.TaxStatusClauseUk.Trim(),
        PaymentDetails = [.. request.PaymentDetails.Select(Normalize).Where(payment => !payment.IsBlank)],
    };

    /// <summary>Validates a request already passed through <see cref="Normalize(InvoicingDetailsRequest)"/>.</summary>
    internal static Dictionary<string, string[]>? Validate(InvoicingDetailsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        void Text(string key, string value, int max, bool multiline = false)
        {
            if (value.Length > max)
            {
                errors[key] = [$"{key} must not exceed {max} characters."];
            }
            else if (multiline ? TextRules.HasDisallowedControlChar(value) : value.Any(char.IsControl))
            {
                errors[key] = [$"{key} must not contain a control character."];
            }
        }

        void Clause(string key, string value)
        {
            if (value.Length == 0)
            {
                errors[key] = [$"{key} is required. Reset it to the default to restore the wording."];
            }
            else
            {
                Text(key, value, MaxClauseLength, multiline: true);
            }
        }

        Text("sellerNameUk", request.SellerNameUk, MaxNameLength);
        Text("sellerNameEn", request.SellerNameEn, MaxNameLength);
        Text("addressUk", request.AddressUk, MaxAddressLength, multiline: true);
        Text("addressEn", request.AddressEn, MaxAddressLength, multiline: true);
        if (request.Rnokpp.Length > 0 && !RnokppPattern().IsMatch(request.Rnokpp))
        {
            errors["rnokpp"] = ["rnokpp must be 10 digits."];
        }

        Clause("acceptanceClauseEn", request.AcceptanceClauseEn);
        Clause("acceptanceClauseUk", request.AcceptanceClauseUk);
        Clause("feesClauseEn", request.FeesClauseEn);
        Clause("feesClauseUk", request.FeesClauseUk);
        Clause("taxStatusClauseEn", request.TaxStatusClauseEn);
        Clause("taxStatusClauseUk", request.TaxStatusClauseUk);

        for (var i = 0; i < request.PaymentDetails.Length; i++)
        {
            var payment = request.PaymentDetails[i];
            var at = $"paymentDetails[{i}]";

            if (request.PaymentDetails.Take(i).Any(other => other.Currency == payment.Currency))
            {
                errors[$"{at}.currency"] = ["currency must not repeat: one set of payment details per currency."];
            }

            if (!IsValidUkrainianIban(payment.Iban))
            {
                errors[$"{at}.iban"] = ["iban must be a valid Ukrainian IBAN: UA and 27 characters."];
            }

            if (payment.BeneficiaryBank.Length == 0)
            {
                errors[$"{at}.beneficiaryBank"] = ["beneficiaryBank is required."];
            }
            else
            {
                Text($"{at}.beneficiaryBank", payment.BeneficiaryBank, MaxBankFieldLength);
            }

            if (!SwiftPattern().IsMatch(payment.Swift))
            {
                errors[$"{at}.swift"] = ["swift must be 8 or 11 letters and digits."];
            }

            Text($"{at}.intermediaryBank", payment.IntermediaryBank, MaxBankFieldLength);
            Text($"{at}.intermediarySwift", payment.IntermediarySwift, MaxBankFieldLength);
            Text($"{at}.intermediaryAccount", payment.IntermediaryAccount, MaxBankFieldLength);
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static bool IsValidUkrainianIban(string iban)
    {
        if (!IbanPattern().IsMatch(iban))
        {
            return false;
        }

        // ISO 13616: move the first four characters to the end and read letters as 10 to 35; the
        // number must leave 1 modulo 97.
        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = (remainder * (value < 10 ? 10 : 100) + value) % 97;
        }

        return remainder == 1;
    }

    internal static string? SignatureError(byte[] image, string contentType)
    {
        if (image.Length == 0)
        {
            return "The signature image is empty.";
        }

        if (image.Length > MaxSignatureBytes)
        {
            return $"A signature image must not exceed {MaxSignatureBytes / 1024} KB.";
        }

        if (!SignatureTypes.Contains(contentType))
        {
            return "A signature is a PNG or JPEG image.";
        }

        var magic = contentType == "image/png" ? PngMagic : JpegMagic;

        return image.AsSpan().StartsWith(magic) ? null : $"The file is not a {contentType[6..].ToUpperInvariant()} image.";
    }

    internal static void Apply(InvoicingDetails details, InvoicingDetailsRequest request)
    {
        details.SellerNameUk = request.SellerNameUk;
        details.SellerNameEn = request.SellerNameEn;
        details.Rnokpp = request.Rnokpp;
        details.AddressUk = request.AddressUk;
        details.AddressEn = request.AddressEn;
        details.AcceptanceClauseEn = request.AcceptanceClauseEn;
        details.AcceptanceClauseUk = request.AcceptanceClauseUk;
        details.FeesClauseEn = request.FeesClauseEn;
        details.FeesClauseUk = request.FeesClauseUk;
        details.TaxStatusClauseEn = request.TaxStatusClauseEn;
        details.TaxStatusClauseUk = request.TaxStatusClauseUk;
    }

    internal static InvoicingPaymentDetails ToEntity(string userId, Guid id, PaymentDetailsInput payment) => new()
    {
        Id = id,
        UserId = userId,
        Currency = payment.Currency,
        Iban = payment.Iban,
        BeneficiaryBank = payment.BeneficiaryBank,
        Swift = payment.Swift,
        IntermediaryBank = payment.IntermediaryBank,
        IntermediarySwift = payment.IntermediarySwift,
        IntermediaryAccount = payment.IntermediaryAccount,
    };

    private static PaymentDetailsInput Normalize(PaymentDetailsInput payment) => payment with
    {
        Iban = string.Concat(payment.Iban.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant(),
        BeneficiaryBank = payment.BeneficiaryBank.Trim(),
        Swift = string.Concat(payment.Swift.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant(),
        IntermediaryBank = payment.IntermediaryBank.Trim(),
        IntermediarySwift = payment.IntermediarySwift.Trim(),
        IntermediaryAccount = payment.IntermediaryAccount.Trim(),
    };

    private static void ApplyPayments(
        AppDbContext database,
        string userId,
        List<InvoicingPaymentDetails> stored,
        PaymentDetailsInput[] wanted)
    {
        foreach (var row in stored.Where(row => wanted.All(payment => payment.Currency != row.Currency)))
        {
            database.InvoicingPaymentDetails.Remove(row);
        }

        foreach (var payment in wanted)
        {
            var row = stored.Find(existing => existing.Currency == payment.Currency);
            if (row is null)
            {
                database.InvoicingPaymentDetails.Add(ToEntity(userId, Guid.NewGuid(), payment));
                continue;
            }

            row.Iban = payment.Iban;
            row.BeneficiaryBank = payment.BeneficiaryBank;
            row.Swift = payment.Swift;
            row.IntermediaryBank = payment.IntermediaryBank;
            row.IntermediarySwift = payment.IntermediarySwift;
            row.IntermediaryAccount = payment.IntermediaryAccount;
        }
    }

    private static InvoicingDetailsResponse ToResponse(
        InvoicingDetails details, IEnumerable<InvoicingPaymentDetails> payments) => new(
        details.SellerNameUk,
        details.SellerNameEn,
        details.Rnokpp,
        details.AddressUk,
        details.AddressEn,
        details.AcceptanceClauseEn,
        details.AcceptanceClauseUk,
        details.FeesClauseEn,
        details.FeesClauseUk,
        details.TaxStatusClauseEn,
        details.TaxStatusClauseUk,
        [.. payments.OrderBy(payment => payment.Currency).Select(payment => new PaymentDetailsInput(
            payment.Currency,
            payment.Iban,
            payment.BeneficiaryBank,
            payment.Swift,
            payment.IntermediaryBank,
            payment.IntermediarySwift,
            payment.IntermediaryAccount))],
        details.SignatureImage is not null,
        details.SignatureUpdatedAt,
        new InvoicingClauseDefaults(
            InvoicingDefaults.AcceptanceEn,
            InvoicingDefaults.AcceptanceUk,
            InvoicingDefaults.FeesEn,
            InvoicingDefaults.FeesUk,
            InvoicingDefaults.TaxStatusEn,
            InvoicingDefaults.TaxStatusUk));

    private static async Task<byte[]?> ReadBoundedAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxSignatureBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex RnokppPattern();

    [GeneratedRegex("^UA[0-9A-Z]{27}$")]
    private static partial Regex IbanPattern();

    [GeneratedRegex("^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$")]
    private static partial Regex SwiftPattern();
}

internal sealed record PaymentDetailsInput(
    Currency Currency,
    string Iban,
    string BeneficiaryBank,
    string Swift,
    string IntermediaryBank,
    string IntermediarySwift,
    string IntermediaryAccount)
{
    [JsonIgnore]
    public bool IsBlank =>
        Iban.Length == 0
        && BeneficiaryBank.Length == 0
        && Swift.Length == 0
        && IntermediaryBank.Length == 0
        && IntermediarySwift.Length == 0
        && IntermediaryAccount.Length == 0;
}

internal sealed record InvoicingDetailsRequest(
    string SellerNameUk,
    string SellerNameEn,
    string Rnokpp,
    string AddressUk,
    string AddressEn,
    string AcceptanceClauseEn,
    string AcceptanceClauseUk,
    string FeesClauseEn,
    string FeesClauseUk,
    string TaxStatusClauseEn,
    string TaxStatusClauseUk,
    PaymentDetailsInput[] PaymentDetails);

internal sealed record InvoicingClauseDefaults(
    string AcceptanceClauseEn,
    string AcceptanceClauseUk,
    string FeesClauseEn,
    string FeesClauseUk,
    string TaxStatusClauseEn,
    string TaxStatusClauseUk);

internal sealed record InvoicingDetailsResponse(
    string SellerNameUk,
    string SellerNameEn,
    string Rnokpp,
    string AddressUk,
    string AddressEn,
    string AcceptanceClauseEn,
    string AcceptanceClauseUk,
    string FeesClauseEn,
    string FeesClauseUk,
    string TaxStatusClauseEn,
    string TaxStatusClauseUk,
    PaymentDetailsInput[] PaymentDetails,
    bool HasSignature,
    DateTimeOffset? SignatureUpdatedAt,
    InvoicingClauseDefaults Defaults);

internal sealed record MonobankPrefillResponse(string SellerNameUk, PaymentDetailsInput[] PaymentDetails);
