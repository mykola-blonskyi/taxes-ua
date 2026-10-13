using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Payments;

namespace TaxesUa.Api.Features.Archive;

/// <summary>
/// What the app holds for one year, as the owning features list it. The listing and the ZIP both read
/// this one value, so the ZIP holds exactly what the screen shows.
/// </summary>
internal sealed record ArchiveContents(
    InvoiceDocument[] Invoices,
    QuarterDocuments[] Quarters,
    bool HasReceipts,
    bool HasPayments)
{
    public static async Task<ArchiveContents> LoadAsync(
        AppDbContext database, string userId, int year, TimeProvider time, CancellationToken cancellationToken) => new(
        await InvoiceDocuments.ListAsync(database, userId, year, cancellationToken),
        await DeclarationDocuments.ListAsync(database, userId, year, time.TodayInKyiv(), cancellationToken),
        await ExportEndpoints.AnyAsync(database, userId, year, cancellationToken),
        await PaymentRegister.AnyAsync(database, userId, year, cancellationToken));
}
