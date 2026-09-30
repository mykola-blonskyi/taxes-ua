using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using TaxesUa.Api.Features.Export;
using TaxesUa.Api.Features.Fx;

namespace TaxesUa.Api.Features.Invoices;

/// <summary>Everything the invoice PDF prints. An issued invoice fills it from its frozen snapshot only.</summary>
internal sealed record InvoicePdfModel(
    string? Number,
    InvoiceStatus Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    Currency Currency,
    InvoiceLine[] Lines,
    long TotalMinor,
    InvoiceSnapshot Snapshot,
    byte[]? Signature);

/// <summary>
/// The A4 bilingual invoice: every label reads English then Ukrainian, and every text the owner or
/// client wrote in one language only (a name, an address) is printed as written. ADR-013.
/// </summary>
internal static class InvoicePdf
{
    private const string NoBreakSpace = "\u00a0";
    private const double MarginMm = 15;
    private const double PrintableWidthMm = 210 - 2 * MarginMm;
    private const double SignatureHeightMm = 18;

    private static readonly Dictionary<InvoiceUnit, string> Units = new()
    {
        [InvoiceUnit.Service] = "service / послуга",
        [InvoiceUnit.Hour] = "hour / година",
        [InvoiceUnit.Day] = "day / день",
        [InvoiceUnit.Month] = "month / місяць",
    };

    /// <summary>
    /// The PDF, with the signature image when it renders. A signature PDFsharp cannot read (truncated,
    /// an unusual encoding) falls back to the seller's name, which is the identifying data anyway.
    /// </summary>
    public static byte[] ToPdf(InvoicePdfModel invoice)
    {
        PdfFonts.Register();
        if (invoice.Signature is { } signature && Readable(signature))
        {
            try
            {
                return Render(invoice, signature);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Falls through to the name: an image that parsed can still fail to embed.
            }
        }

        return Render(invoice, signature: null);
    }

    private static string PaymentReference(string? number)
    {
        var shown = number ?? "____";

        return $"Payment for services under invoice No. {shown} / Оплата послуг згідно з інвойсом № {shown}";
    }

    public static string Money(long minor)
    {
        var negative = minor < 0;
        var digits = Math.Abs(minor).ToString("000", CultureInfo.InvariantCulture);
        var whole = digits[..^2];
        var grouped = new StringBuilder();
        for (var i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0)
            {
                grouped.Append(NoBreakSpace);
            }

            grouped.Append(whole[i]);
        }

        return $"{(negative ? "-" : string.Empty)}{grouped}.{digits[^2..]}";
    }

    public static string Quantity(long thousandths)
    {
        var whole = (thousandths / 1000).ToString(CultureInfo.InvariantCulture);
        var fraction = (thousandths % 1000).ToString("000", CultureInfo.InvariantCulture).TrimEnd('0');

        return fraction.Length == 0 ? whole : $"{whole}.{fraction}";
    }

    private static bool Readable(byte[] image)
    {
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var parsed = XImage.FromStream(stream);
            return parsed.PixelWidth > 0 && parsed.PixelHeight > 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static byte[] Render(InvoicePdfModel invoice, byte[]? signature)
    {
        var snapshot = invoice.Snapshot;
        var document = new Document();
        document.Info.Title = invoice.Number is { } number ? $"Invoice {number}" : "Invoice draft";
        document.Info.Author = snapshot.Seller.NameEn;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 9;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromMillimeter(MarginMm);
        section.PageSetup.RightMargin = Unit.FromMillimeter(MarginMm);
        section.PageSetup.TopMargin = Unit.FromMillimeter(MarginMm);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(MarginMm);

        if (Mark(invoice.Status) is { } mark)
        {
            var header = section.Headers.Primary.AddParagraph(mark);
            header.Format.Alignment = ParagraphAlignment.Center;
            header.Format.Font.Size = 16;
            header.Format.Font.Bold = true;
            header.Format.Font.Color = Colors.IndianRed;
        }

        var title = section.AddParagraph($"Invoice / Інвойс № {invoice.Number ?? "____"}");
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromMillimeter(2);

        Labelled(section, "Date of issue / Дата складання", Date(invoice.IssueDate));
        Labelled(section, "Due date / Оплатити до", Date(invoice.DueDate));

        Gap(section, 4);
        section.Add(Parties(snapshot));
        Gap(section, 5);
        section.Add(LinesTable(invoice));

        var total = section.AddParagraph();
        total.Format.Alignment = ParagraphAlignment.Right;
        total.Format.SpaceBefore = Unit.FromMillimeter(2);
        total.Format.Font.Size = 11;
        total.AddFormattedText("Total / Разом: ", TextFormat.NotBold);
        total.AddFormattedText($"{Money(invoice.TotalMinor)} {invoice.Currency}", TextFormat.Bold);

        Heading(section, "Payment details / Платіжні реквізити");
        Labelled(section, "Beneficiary / Отримувач", $"{snapshot.Seller.NameEn} / {snapshot.Seller.NameUk}");
        Labelled(section, "IBAN", snapshot.Payment.Iban);
        Labelled(section, "Beneficiary bank / Банк отримувача", snapshot.Payment.BeneficiaryBank);
        Labelled(section, "SWIFT", snapshot.Payment.Swift);
        if (snapshot.Payment.IntermediaryBank.Length > 0)
        {
            Labelled(section, "Intermediary bank / Банк-посередник", snapshot.Payment.IntermediaryBank);
        }

        if (snapshot.Payment.IntermediarySwift.Length > 0)
        {
            Labelled(section, "Intermediary SWIFT / SWIFT банку-посередника", snapshot.Payment.IntermediarySwift);
        }

        if (snapshot.Payment.IntermediaryAccount.Length > 0)
        {
            Labelled(section, "Intermediary account / Рахунок у банку-посереднику", snapshot.Payment.IntermediaryAccount);
        }

        Labelled(section, "Payment reference / Призначення платежу", PaymentReference(invoice.Number));

        Heading(section, "Terms / Умови");
        section.Add(Clauses(snapshot.Clauses));

        Signature(section, snapshot.Seller, signature);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string? Mark(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "DRAFT / ЧЕРНЕТКА",
        InvoiceStatus.Cancelled => "CANCELLED / АНУЛЬОВАНО",
        _ => null,
    };

    private static Table Parties(InvoiceSnapshot snapshot)
    {
        var table = new Table { Borders = { Width = 0 } };
        table.AddColumn(Unit.FromMillimeter(PrintableWidthMm / 2));
        table.AddColumn(Unit.FromMillimeter(PrintableWidthMm / 2));
        var row = table.AddRow();
        row.TopPadding = Unit.FromMillimeter(2);

        var seller = row.Cells[0];
        Bold(seller.AddParagraph("Seller / Продавець"));
        seller.AddParagraph(snapshot.Seller.NameEn);
        seller.AddParagraph(snapshot.Seller.NameUk);
        seller.AddParagraph($"RNOKPP / РНОКПП: {snapshot.Seller.Rnokpp}");
        seller.AddParagraph(snapshot.Seller.AddressEn);
        seller.AddParagraph(snapshot.Seller.AddressUk);

        var buyer = row.Cells[1];
        Bold(buyer.AddParagraph("Buyer / Покупець"));
        buyer.AddParagraph(snapshot.Buyer.Name);
        buyer.AddParagraph(snapshot.Buyer.Address);
        buyer.AddParagraph($"Country / Країна: {snapshot.Buyer.CountryName}");
        if (snapshot.Buyer.VatId is { } vatId)
        {
            buyer.AddParagraph($"VAT or tax ID / Податковий номер: {vatId}");
        }

        if (snapshot.Buyer.Email is { } email)
        {
            buyer.AddParagraph($"Email / Ел. пошта: {email}");
        }

        return table;
    }

    private static Table LinesTable(InvoicePdfModel invoice)
    {
        var table = new Table
        {
            Borders = { Width = 0.5, Color = Colors.Gray },
            LeftPadding = Unit.FromMillimeter(1),
            RightPadding = Unit.FromMillimeter(1),
            TopPadding = Unit.FromMillimeter(1),
            BottomPadding = Unit.FromMillimeter(1),
        };
        double[] widths = [8, 66, 25, 23, 28, 30];
        foreach (var width in widths)
        {
            table.AddColumn(Unit.FromMillimeter(width));
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = Colors.Gainsboro;
        header.Format.Font.Bold = true;
        string[] headers =
        [
            "#",
            "Description / Опис",
            "Unit / Одиниця",
            "Qty / К-сть",
            $"Rate / Ціна, {invoice.Currency}",
            $"Amount / Сума, {invoice.Currency}",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            header.Cells[i].AddParagraph(headers[i]);
        }

        using var measure = XGraphics.CreateMeasureContext(new XSize(1, 1), XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont(PdfFonts.Family, 9);
        var descriptionWidth = Unit.FromMillimeter(widths[1] - 2).Point - 1;
        for (var i = 0; i < invoice.Lines.Length; i++)
        {
            var line = invoice.Lines[i];
            var row = table.AddRow();
            row.Cells[0].AddParagraph((i + 1).ToString(CultureInfo.InvariantCulture));
            foreach (var description in new[] { line.DescriptionEn, line.DescriptionUk })
            {
                var paragraph = row.Cells[1].AddParagraph();
                var pieces = TransactionPdf.BreakOverlongWords(
                    description, descriptionWidth, value => measure.MeasureString(value, font).Width);
                for (var piece = 0; piece < pieces.Count; piece++)
                {
                    if (piece > 0)
                    {
                        paragraph.AddLineBreak();
                    }

                    paragraph.AddText(pieces[piece]);
                }
            }

            row.Cells[2].AddParagraph(Units[line.Unit]);
            Right(row.Cells[3].AddParagraph(Quantity(line.QuantityThousandths)));
            Right(row.Cells[4].AddParagraph(Money(line.RateMinor)));
            Right(row.Cells[5].AddParagraph(Money(InvoiceRules.LineAmountMinor(line.QuantityThousandths, line.RateMinor))));
        }

        return table;
    }

    private static Table Clauses(InvoiceClauses clauses)
    {
        var table = new Table { Borders = { Width = 0 } };
        table.AddColumn(Unit.FromMillimeter(PrintableWidthMm / 2));
        table.AddColumn(Unit.FromMillimeter(PrintableWidthMm / 2));
        foreach (var (en, uk) in new[]
                 {
                     (clauses.AcceptanceEn, clauses.AcceptanceUk),
                     (clauses.FeesEn, clauses.FeesUk),
                     (clauses.TaxStatusEn, clauses.TaxStatusUk),
                 })
        {
            var row = table.AddRow();
            row.BottomPadding = Unit.FromMillimeter(1.5);
            row.Cells[0].AddParagraph(en);
            row.Cells[1].AddParagraph(uk);
            row.Cells[1].Format.LeftIndent = Unit.FromMillimeter(3);
        }

        return table;
    }

    private static void Signature(Section section, InvoiceSeller seller, byte[]? signature)
    {
        Heading(section, "Seller's signature / Підпис продавця");
        if (signature is not null)
        {
            var image = section.AddParagraph().AddImage("base64:" + Convert.ToBase64String(signature));
            image.Height = Unit.FromMillimeter(SignatureHeightMm);
            image.LockAspectRatio = true;
        }

        section.AddParagraph($"{seller.NameEn} / {seller.NameUk}");
    }

    private static void Heading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1.5);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 10;
    }

    private static void Labelled(Section section, string label, string value)
    {
        var paragraph = section.AddParagraph();
        paragraph.AddFormattedText($"{label}: ", TextFormat.NotBold);
        paragraph.AddFormattedText(value, TextFormat.Bold);
    }

    // A table's own paragraph format applies to every paragraph in its cells, so the space around a
    // table is an empty paragraph instead.
    private static void Gap(Section section, double millimeters) =>
        section.AddParagraph().Format.SpaceAfter = Unit.FromMillimeter(millimeters);

    private static void Bold(Paragraph paragraph) => paragraph.Format.Font.Bold = true;

    private static void Right(Paragraph paragraph) => paragraph.Format.Alignment = ParagraphAlignment.Right;

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
