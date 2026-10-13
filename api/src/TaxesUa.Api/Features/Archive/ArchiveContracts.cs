using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Invoices;

namespace TaxesUa.Api.Features.Archive;

/// <summary>
/// What the app holds for one tax year, each item with the route that downloads it. <c>Id</c> is stable
/// across calls (kind, then the year and the item's own key), <c>Name</c> is the file name the download
/// is served under and <c>Url</c> is the route. A year with nothing has empty arrays, never an error.
/// <c>Statements</c> is the receipts statement in each format and <c>Payments</c> the budget payments
/// register; both are empty while the year has no rows to list.
/// </summary>
internal sealed record ArchiveResponse(
    int Year,
    ArchiveInvoice[] Invoices,
    ArchiveQuarter[] Quarters,
    ArchiveItem[] Statements,
    ArchiveItem[] Payments);

internal sealed record ArchiveItem(string Id, string Name, string Url);

/// <summary>An issued or cancelled invoice; <c>Url</c> serves the PDF rendered from its frozen snapshot.</summary>
internal sealed record ArchiveInvoice(
    string Id,
    string Name,
    string Url,
    string Number,
    InvoiceStatus Status,
    DateOnly IssueDate,
    string Client,
    string Currency,
    long TotalMinor);

/// <summary>A quarter with a filing mark or a downloadable file; the other quarters are left out.</summary>
internal sealed record ArchiveQuarter(int Quarter, ArchiveFiling? Filed, ArchiveDeclarationFile[] Files);

internal sealed record ArchiveFiling(DateOnly FiledOn, DeclarationType Type);

/// <summary>One XML file: a declaration or, when <c>Annex</c> is true, its annex 1.</summary>
internal sealed record ArchiveDeclarationFile(
    string Id, DeclarationType Type, bool Annex, string Name, string Url, DateTimeOffset GeneratedAt);
