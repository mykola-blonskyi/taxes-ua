using System.Xml.Schema;
using TaxesUa.Engine;
using static TaxesUa.Api.Features.Declarations.DpsXml;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The quarterly group 3 declaration as the Electronic Cabinet imports it: form F0103309 (C_DOC F01,
/// C_DOC_SUB 033, C_DOC_VER 9), windows-1251, elements in the XSD's order with nothing between them,
/// named per DPS standard No. 729 (ADR-016), and on the year's last group 3 declaration its annex 1
/// (<see cref="F0133109"/>), each linking the other. Pure: the same input gives the same bytes.
/// </summary>
internal static class F0103309
{
    public static readonly Form Form = new("F01", "033", 9);

    private static readonly Lazy<XmlSchemaSet> Schemas = new(() => LoadSchemas(Form), LazyThreadSafetyMode.ExecutionAndPublication);

    public static DeclarationXmlFiles Write(
        DeclarationFigures figures, DeclarationHeader header, DeclarationType type, DateOnly filledOn)
    {
        var filing = new Filing(header, type, figures.Year, figures.Quarter, filledOn);
        var fileName = filing.FileName(Form);
        var annex = figures.EsvAnnex is { } esv
            ? new DeclarationXml(filing.FileName(F0133109.Form), F0133109.Write(esv, filing, fileName))
            : null;
        var linked = annex is null ? null : new LinkedDoc(F0133109.Form, 1, annex.FileName);

        var content = DpsXml.Write(Form, filing, linked, writer =>
        {
            Element(writer, TypeMark(type), 1);
            Element(writer, PeriodMark(filing.Quarter), 1);
            Element(writer, "HZY", filing.Year);
            if (type == DeclarationType.Clarifying)
            {
                Element(writer, PeriodMark(filing.Quarter) + "P", 1);
                Element(writer, "HZYP", filing.Year);
            }

            Element(writer, "HSTI", header.TaxOfficeName);
            Element(writer, "HNAME", filing.Name);
            Element(writer, "HLOC", header.Address);
            Element(writer, "HTIN", header.Rnokpp);
            Element(writer, "HNACTL", 0);
            for (var i = 0; i < header.KvedCodes.Count; i++)
            {
                Row(writer, "T1RXXXXG1S", i + 1, header.KvedCodes[i]);
            }

            // The app keeps no KVED names, and the column's type lets a row be empty.
            for (var i = 0; i < header.KvedCodes.Count; i++)
            {
                Row(writer, "T1RXXXXG2S", i + 1, string.Empty);
            }

            Element(writer, "R006G3", Amount(figures.IncomeKop));
            if (figures.ExcessIncomeKop != 0)
            {
                Element(writer, "R007G3", Amount(figures.ExcessIncomeKop));
            }

            Element(writer, "R008G3", Amount(figures.TotalIncomeKop));
            if (figures.ExcessTaxKop != 0)
            {
                Element(writer, "R009G3", Amount(figures.ExcessTaxKop));
            }

            Element(writer, "R011G3", Amount(figures.SingleTaxKop));
            Element(writer, "R012G3", Amount(figures.TotalSingleTaxKop));
            Element(writer, "R013G3", Amount(figures.PreviousSingleTaxKop));
            Element(writer, "R0141G3", Amount(figures.SingleTaxPayableKop));
            Element(writer, "R014G3", Amount(figures.SingleTaxPayableKop));
            if (figures.EsvAnnex is { } annexFigures)
            {
                Element(writer, "R021G3", Amount(annexFigures.EsvKop));
            }

            Element(writer, "R023G3", Amount(figures.MilitaryLevyKop));
            Element(writer, "R024G3", Amount(figures.PreviousMilitaryLevyKop));
            Element(writer, "R025G3", Amount(figures.MilitaryLevyPayableKop));
            if (annex is not null)
            {
                Element(writer, "HD1", 1);
            }

            Element(writer, "HFILL", filing.Filled);
            Element(writer, "HBOS", filing.Name);
        });

        return new DeclarationXmlFiles(new DeclarationXml(fileName, content), annex);
    }

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content) => DpsXml.SchemaErrors(content, Schemas.Value);
}
