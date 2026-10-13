using System.Globalization;
using System.IO.Compression;
using System.Text;
using TaxesUa.Api.Data;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Invoices;
using TaxesUa.Api.Features.Payments;

namespace TaxesUa.Api.Features.Archive;

/// <summary>
/// A year's archive as one ZIP. Each file is read when its turn comes and written straight to the
/// response, so memory holds one document at a time, never the year.
/// </summary>
internal static class YearZip
{
    public const string ContentsName = "contents.txt";

    public static string FileName(int year, DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"archive-{year}-{today:yyyy-MM-dd}.zip");

    public static async Task WriteAsync(
        Stream destination,
        AppDbContext database,
        string userId,
        int year,
        ArchiveContents contents,
        string tableOfContents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var sink = new BufferedSink(destination);
        var zip = await ZipArchive.CreateAsync(
            sink, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        var writer = new Writer(zip, sink);

        await AddAsync(writer, ContentsName, () => Task.FromResult(TextBytes(tableOfContents)), cancellationToken);

        foreach (var invoice in contents.Invoices)
        {
            await AddAsync(
                writer,
                InvoicePath(invoice),
                async () => await InvoiceDocuments.PdfAsync(database, userId, invoice.Id, cancellationToken)
                    ?? throw Missing(InvoicePath(invoice)),
                cancellationToken);
        }

        foreach (var quarter in contents.Quarters)
        {
            foreach (var file in quarter.Files)
            {
                await AddAsync(
                    writer,
                    DeclarationPath(quarter.Quarter, file.Name),
                    async () => await DeclarationDocuments.ReadAsync(
                            database, userId, year, quarter.Quarter, file.Type, annex: false, cancellationToken)
                        ?? throw Missing(DeclarationPath(quarter.Quarter, file.Name)),
                    cancellationToken);
                if (file.Annex is { } annex)
                {
                    await AddAsync(
                        writer,
                        DeclarationPath(quarter.Quarter, annex),
                        async () => await DeclarationDocuments.ReadAsync(
                                database, userId, year, quarter.Quarter, file.Type, annex: true, cancellationToken)
                            ?? throw Missing(DeclarationPath(quarter.Quarter, annex)),
                        cancellationToken);
                }
            }
        }

        if (contents.HasReceipts)
        {
            foreach (var extension in ExportEndpoints.Extensions)
            {
                await AddAsync(
                    writer,
                    StatementPath(year, extension),
                    () => ExportEndpoints.RenderAsync(database, userId, year, extension, time, cancellationToken),
                    cancellationToken);
            }
        }

        if (contents.HasPayments)
        {
            await AddAsync(
                writer,
                PaymentsPath(year),
                () => PaymentRegister.ToCsvAsync(database, userId, year, cancellationToken),
                cancellationToken);
        }

        await zip.DisposeAsync();
        await sink.DrainAsync(cancellationToken);
    }

    /// <summary>
    /// The ZIP's table of contents as plain text in the owner's language: title, creation date, the keep-until
    /// line worded as the periods screen words it, then each section with its files. A section with nothing
    /// in it says so.
    /// </summary>
    public static string TableOfContents(
        int year, ArchiveContents contents, string locale, DateOnly today, KeepUntilResponse? keepUntil)
    {
        var text = Texts.For(locale);
        var builder = new StringBuilder();
        void Line(string line = "") => builder.Append(line).Append("\r\n");
        void File(string path, string? note = null) => Line($"  {path}{(note is null ? string.Empty : " - " + note)}");
        void Heading(string heading)
        {
            Line();
            Line(heading);
        }

        Line(Format(text.Title, year));
        Line($"{text.Created}: {Date(today)}");
        Line(KeepUntilLine(text, year, keepUntil));
        if (keepUntil is not null)
        {
            Line(text.Basis);
        }

        Heading(text.Contents);
        File(ContentsName, text.ThisFile);

        Heading(text.Invoices);
        if (contents.Invoices.Length == 0)
        {
            Line($"  {text.None}");
        }

        foreach (var invoice in contents.Invoices)
        {
            var status = invoice.Status == InvoiceStatus.Cancelled ? $", {text.Cancelled}" : string.Empty;
            File(
                InvoicePath(invoice),
                $"{invoice.Number}, {invoice.Client}, {Date(invoice.IssueDate)}, {Amount(invoice.TotalMinor)} {invoice.Currency}{status}");
        }

        Heading(text.Declarations);
        if (contents.Quarters.Length == 0)
        {
            Line($"  {text.None}");
        }

        foreach (var quarter in contents.Quarters)
        {
            var filed = quarter.Filing is { } filing ? Format(text.Filed, Date(filing.FiledOn)) : text.NotFiled;
            Line($"  {Format(text.Quarter, quarter.Quarter)} ({filed})");
            if (quarter.Files.Length == 0)
            {
                Line($"    {text.NoFiles}");
            }

            foreach (var file in quarter.Files)
            {
                Line($"    {DeclarationPath(quarter.Quarter, file.Name)} - {text.Declaration}");
                if (file.Annex is { } annex)
                {
                    Line($"    {DeclarationPath(quarter.Quarter, annex)} - {text.Annex}");
                }
            }
        }

        Heading(text.Statement);
        if (!contents.HasReceipts)
        {
            Line($"  {text.None}");
        }
        else
        {
            foreach (var extension in ExportEndpoints.Extensions)
            {
                File(StatementPath(year, extension));
            }
        }

        Heading(text.Payments);
        if (contents.HasPayments)
        {
            File(PaymentsPath(year));
        }
        else
        {
            Line($"  {text.None}");
        }

        return builder.ToString();
    }

    // The wording the periods screen uses (messages periods.keepUntil), so the file and the screen cannot
    // tell the owner two things.
    private static string KeepUntilLine(Texts text, int year, KeepUntilResponse? keepUntil) => keepUntil switch
    {
        null => Format(text.KeepUntilNone, year),
        { State: KeepUntilState.ExtendedWhileSuspended } open => Format(
            text.KeepUntilExtended,
            year,
            Date(open.Date),
            open.DaysAfterSuspension ?? 0,
            Plural(open.DaysAfterSuspension ?? 0, text.Days)),
        _ => Format(text.KeepUntilFixed, year, Date(keepUntil.Date)),
    };

    private static string InvoicePath(InvoiceDocument invoice) => $"invoices/{invoice.FileName}";

    private static string DeclarationPath(int quarter, string name) => $"declarations/Q{quarter}/{name}";

    private static string StatementPath(int year, string extension) =>
        $"statement/{ExportEndpoints.FileName(year, extension)}";

    private static string PaymentsPath(int year) => $"payments/{PaymentRegister.FileName(year)}";

    private static async Task AddAsync(
        Writer writer, string path, Func<Task<byte[]>> read, CancellationToken cancellationToken)
    {
        var bytes = await read();
        var entry = writer.Zip.CreateEntry(path, CompressionLevel.Fastest);
        await using (var stream = await entry.OpenAsync(cancellationToken))
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }

        await writer.Sink.DrainAsync(cancellationToken);
    }

    private sealed record Writer(ZipArchive Zip, BufferedSink Sink);

    // The compressor closes an entry with a synchronous write, which the server refuses on a response
    // body. So the archive writes here, in memory, and each finished document is copied on to the response
    // asynchronously: memory holds one compressed document at a time, never the year.
    private sealed class BufferedSink(Stream destination) : Stream
    {
        private readonly MemoryStream _pending = new();

        public async Task DrainAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length > 0)
            {
                await destination.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
                _pending.SetLength(0);
            }

            await destination.FlushAsync(cancellationToken);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // A document that vanished between the listing and its turn stops the download: a ZIP that silently
    // misses a file its table of contents names is worse than none.
    private static InvalidOperationException Missing(string path) =>
        new($"The archive item {path} is no longer there.");

    // UTF-8 with a byte order mark, which is how Windows Notepad knows the Cyrillic is UTF-8.
    private static byte[] TextBytes(string text) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];

    private static string Format(string template, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, template, arguments);

    // One, few and many, which Ukrainian and Russian choose by the same rule.
    private static string Plural(int count, (string One, string Few, string Many) forms) => (count % 10, count % 100) switch
    {
        (1, not 11) => forms.One,
        ( >= 2 and <= 4, not (>= 12 and <= 14)) => forms.Few,
        _ => forms.Many,
    };

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Amount(long minor) =>
        $"{(minor < 0 ? "-" : string.Empty)}{Math.Abs(minor) / 100}.{Math.Abs(minor) % 100:00}";

    private sealed record Texts(
        string Title,
        string Created,
        string KeepUntilFixed,
        string KeepUntilExtended,
        string KeepUntilNone,
        string Basis,
        (string One, string Few, string Many) Days,
        string Contents,
        string ThisFile,
        string Invoices,
        string Cancelled,
        string Declarations,
        string Quarter,
        string Filed,
        string NotFiled,
        string NoFiles,
        string Declaration,
        string Annex,
        string Statement,
        string Payments,
        string None)
    {
        private static readonly Texts Uk = new(
            "Архів документів за {0} рік",
            "Сформовано",
            "Документи за {0} рік зберігати до {1}",
            "Документи за {0} рік зберігати не менше ніж до {1}. Строк продовжено на час воєнного стану: після його припинення або скасування ще {2} {3}.",
            "Документи за {0} рік: строк зберігання не визначено, бо за цей рік немає декларації платника третьої групи.",
            "Підстава: п. 44.3 ст. 44 Податкового кодексу.",
            ("день", "дні", "днів"),
            "Зміст",
            "цей зміст",
            "Рахунки",
            "скасований",
            "Декларації",
            "{0} квартал",
            "подано {0}",
            "не подано",
            "файлів немає",
            "декларація",
            "додаток 1",
            "Виписка про надходження",
            "Платежі до бюджету",
            "немає");

        private static readonly Texts Ru = new(
            "Архив документов за {0} год",
            "Сформировано",
            "Документы за {0} год хранить до {1}",
            "Документы за {0} год хранить не менее чем до {1}. Срок продлён на время военного положения: после его прекращения или отмены ещё {2} {3}.",
            "Документы за {0} год: срок хранения не определён, потому что за этот год нет декларации плательщика третьей группы.",
            "Основание: п. 44.3 ст. 44 Налогового кодекса.",
            ("день", "дня", "дней"),
            "Оглавление",
            "это оглавление",
            "Счета",
            "отменён",
            "Декларации",
            "{0} квартал",
            "подана {0}",
            "не подана",
            "файлов нет",
            "декларация",
            "приложение 1",
            "Выписка о поступлениях",
            "Платежи в бюджет",
            "нет");

        public static Texts For(string locale) => locale == "ru" ? Ru : Uk;
    }
}
