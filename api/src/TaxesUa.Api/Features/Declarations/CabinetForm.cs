using System.Globalization;
using TaxesUa.Api.Features.Settings;
using TaxesUa.Engine;
using static TaxesUa.Api.Features.Declarations.DpsXml;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>Where on the Cabinet form a field sits; <c>None</c> fields are in the XML only.</summary>
internal enum CabinetPart
{
    None,
    Header,
    Period,
    Declaration,
    Footer,
    Annex,
}

/// <summary>
/// How the owner supplies the field: typed text or digits (<c>Text</c>, <c>Number</c>, <c>Amount</c>,
/// <c>Date</c>) or a box to tick (<c>Mark</c>).
/// </summary>
internal enum CabinetKind
{
    Text,
    Number,
    Amount,
    Date,
    Mark,
}

/// <summary>
/// One field of the F0103309 or F0133109 form, in the order the XSD and the form list them. The XML
/// writers and the "Fill in the Cabinet" view both read this list, so a value cannot differ between
/// them (ADR-025). <c>Value</c> is the element's text; null means the form leaves the field empty and
/// the XML has no element. <c>Line</c> is the printed line number, <c>Row</c> a table row (ROWNUM) and
/// <c>Month</c> and <c>Column</c> place a cell of the annex's monthly table.
/// </summary>
internal sealed record CabinetField(
    string Element,
    string? Value,
    CabinetPart Part = CabinetPart.None,
    CabinetKind Kind = CabinetKind.Text,
    string? Line = null,
    int Row = 0,
    int Month = 0,
    int Column = 0)
{
    /// <summary>What the owner types: the XML text, except a date, which the Cabinet takes as dd.MM.yyyy, and a text the XML normalises (a modifier apostrophe, a line break) so the view equals the file.</summary>
    public string? Entry => Kind switch
    {
        CabinetKind.Mark => null,
        CabinetKind.Date when Value is { Length: 8 } => $"{Value[..2]}.{Value[2..4]}.{Value[4..]}",
        _ => Value is null ? null : DpsXml.Text(Value),
    };
}

internal static class CabinetForm
{
    /// <summary>The form's footnote 11: category 6, a FOP on the simplified system.</summary>
    public const int InsuredPersonCategory = 6;

    /// <summary>
    /// F0103309's body in XSD order. <paramref name="header"/> is null only for the view of a declaration
    /// whose details are incomplete: the header fields are then left out, and no file is written.
    /// </summary>
    public static IReadOnlyList<CabinetField> Declaration(
        DeclarationFigures figures, DeclarationHeader? header, DeclarationType type, DateOnly filledOn)
    {
        var quarter = figures.Quarter;
        var fields = new List<CabinetField>
        {
            new(TypeMark(type), "1"),
            new(PeriodMark(quarter), "1", CabinetPart.Period, CabinetKind.Mark),
            new("HZY", Number(figures.Year), CabinetPart.Period, CabinetKind.Number),
        };
        if (type == DeclarationType.Clarifying)
        {
            fields.Add(new(PeriodMark(quarter) + "P", "1"));
            fields.Add(new("HZYP", Number(figures.Year)));
        }

        if (header is not null)
        {
            fields.Add(new("HSTI", header.TaxOfficeName, CabinetPart.Header));
            fields.Add(new("HNAME", HeaderName(header.Name), CabinetPart.Header));
            fields.Add(new("HLOC", header.Address, CabinetPart.Header));
            fields.Add(new("HEMAIL", header.Email, CabinetPart.Header));
            fields.Add(new("HTEL", header.Phone, CabinetPart.Header));
            fields.Add(new("HTIN", header.Rnokpp, CabinetPart.Header));
            fields.Add(new("HNACTL", "0"));
            for (var i = 0; i < header.KvedCodes.Count; i++)
            {
                fields.Add(new("T1RXXXXG1S", header.KvedCodes[i], CabinetPart.Header, Row: i + 1));
            }

            // Readiness holds back a code the classifier does not know, so every name is found; the empty
            // fallback only keeps a direct caller from throwing.
            for (var i = 0; i < header.KvedCodes.Count; i++)
            {
                fields.Add(new("T1RXXXXG2S", Kved.Name(header.KvedCodes[i]) ?? string.Empty, CabinetPart.Header, Row: i + 1));
            }
        }

        fields.Add(Line("R006G3", "06", figures.IncomeKop));
        fields.Add(Line("R007G3", "07", figures.ExcessIncomeKop, emptyWhenZero: true));
        fields.Add(Line("R008G3", "08", figures.TotalIncomeKop));
        fields.Add(Line("R009G3", "09", figures.ExcessTaxKop, emptyWhenZero: true));
        fields.Add(Line("R011G3", "11", figures.SingleTaxKop));
        fields.Add(Line("R012G3", "12", figures.TotalSingleTaxKop));
        fields.Add(Line("R013G3", "13", figures.PreviousSingleTaxKop));
        fields.Add(Line("R0141G3", "14.1", figures.SingleTaxPayableKop));
        fields.Add(Line("R014G3", "14", figures.SingleTaxPayableKop));
        fields.Add(new(
            "R021G3",
            figures.EsvAnnex is { } esv ? Amount(esv.EsvKop) : null,
            CabinetPart.Declaration,
            CabinetKind.Amount,
            "21"));
        fields.Add(Line("R023G3", "23", figures.MilitaryLevyKop));
        fields.Add(Line("R024G3", "24", figures.PreviousMilitaryLevyKop));
        fields.Add(Line("R025G3", "25", figures.MilitaryLevyPayableKop));
        if (figures.EsvAnnex is not null)
        {
            fields.Add(new("HD1", "1", CabinetPart.Annex, CabinetKind.Mark));
        }

        fields.Add(new("HFILL", Date(filledOn), CabinetPart.Footer, CabinetKind.Date));
        if (header is not null)
        {
            fields.Add(new("HBOS", Signature(HeaderName(header.Name)), CabinetPart.Footer));
        }

        return fields;
    }

    /// <summary>F0133109's body in XSD order; the header-derived fields need <paramref name="header"/>.</summary>
    public static IReadOnlyList<CabinetField> Annex(EsvAnnex annex, DeclarationHeader? header, DeclarationType type)
    {
        var fields = new List<CabinetField> { new(TypeMark(type), "1") };
        if (header is not null)
        {
            fields.Add(new("HTIN", header.Rnokpp));
            fields.Add(new("HNAME", HeaderName(header.Name)));
        }

        fields.Add(new(PeriodMark(annex.Quarter), "1"));
        fields.Add(new("HZY", Number(annex.Year)));
        if (type == DeclarationType.Clarifying)
        {
            fields.Add(new(PeriodMark(annex.Quarter) + "P", "1"));
            fields.Add(new("HZYP", Number(annex.Year)));
        }

        if (annex.LeavesGroup3)
        {
            fields.Add(new("H03", "1", CabinetPart.Annex, CabinetKind.Mark));
        }

        if (header is not null)
        {
            fields.Add(new("HKVED", header.KvedCodes[0], CabinetPart.Annex));
        }

        fields.Add(new("R08G1D", Date(annex.From), CabinetPart.Annex, CabinetKind.Date, "8"));
        fields.Add(new("R08G2D", Date(annex.To), CabinetPart.Annex, CabinetKind.Date, "8"));
        fields.Add(new("R081G1", Number(InsuredPersonCategory), CabinetPart.Annex, CabinetKind.Number, "8.1"));
        foreach (var month in annex.Months)
        {
            fields.Add(MonthCell(month, 2, month.BaseKop));
            fields.Add(MonthCell(month, 3, month.RateBp));
            fields.Add(MonthCell(month, 4, month.EsvKop));
        }

        fields.Add(new("R09G2", Amount(annex.BaseKop), CabinetPart.Annex, CabinetKind.Amount, "9"));
        fields.Add(new("R09G4", Amount(annex.EsvKop), CabinetPart.Annex, CabinetKind.Amount, "9"));
        if (header is not null)
        {
            fields.Add(new("HBOS", Signature(HeaderName(header.Name))));
        }

        return fields;
    }

    private static CabinetField Line(string element, string line, long kop, bool emptyWhenZero = false) =>
        new(element, emptyWhenZero && kop == 0 ? null : Amount(kop), CabinetPart.Declaration, CabinetKind.Amount, line);

    // Column 3 is the rate in basis points, which Amount prints as percent with two decimals.
    private static CabinetField MonthCell(EsvMonth month, int column, long value) =>
        new($"R09{month.Month}G{column}", Amount(value), CabinetPart.Annex, CabinetKind.Amount, "9", Month: month.Month, Column: column);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
