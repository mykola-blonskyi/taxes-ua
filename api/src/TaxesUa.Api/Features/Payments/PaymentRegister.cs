using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaxesUa.Api.Data;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Payments;

/// <summary>
/// A year's budget payments as one CSV the owner keeps with the year's papers: UTF-8 with a BOM and
/// semicolons, the way the transactions export is written, so Excel reads the Cyrillic headers.
/// </summary>
internal static class PaymentRegister
{
    private static readonly string[] Headers = ["Дата оплати", "Платіж", "Сума, грн", "Період", "Примітка"];

    private static readonly string[] KindNames = ["Єдиний податок", "Військовий збір", "ЄСВ"];

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    public static string FileName(int year) => $"payments-{year}.csv";

    public static Task<bool> AnyAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken) =>
        database.BudgetPayments.AnyAsync(row => row.UserId == userId && row.PeriodYear == year, cancellationToken);

    /// <summary>The year's payments, oldest first: a register is read forward.</summary>
    public static async Task<byte[]> ToCsvAsync(
        AppDbContext database, string userId, int year, CancellationToken cancellationToken)
    {
        var rows = await database.BudgetPayments.AsNoTracking()
            .Where(row => row.UserId == userId && row.PeriodYear == year)
            .OrderBy(row => row.PaidOn)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        AppendLine(builder, Headers);
        foreach (var row in rows)
        {
            AppendLine(builder,
            [
                row.PaidOn.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                KindNames[(int)row.Kind],
                Amount(row.AmountKop),
                row.PeriodMonth is { } month
                    ? $"{row.PeriodYear}-{month:00}"
                    : $"{row.PeriodYear} Q{row.PeriodQuarter}",
                EscapeFormula(row.Note ?? string.Empty),
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    private static string Amount(long kop) =>
        $"{(kop < 0 ? "-" : string.Empty)}{Math.Abs(kop) / 100},{Math.Abs(kop) % 100:00}";

    // OWASP formula-injection defence for the free text, as in the transactions export.
    private static string EscapeFormula(string value) =>
        value.Length > 0 && FormulaTriggers.Contains(value[0]) ? "'" + value : value;

    private static void AppendLine(StringBuilder builder, IEnumerable<string> fields) =>
        builder.Append(string.Join(';', fields.Select(Quote))).Append("\r\n");

    private static string Quote(string field) =>
        field.IndexOfAny(['"', ';', '\r', '\n']) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
}
