using System.Xml.Schema;
using TaxesUa.Engine;
using static TaxesUa.Api.Features.Declarations.DpsXml;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// Annex 1 to the group 3 declaration, the year's ESV for oneself: form F0133109 (C_DOC F01, C_DOC_SUB
/// 331, C_DOC_VER 9), written only beside the <see cref="F0103309"/> it belongs to, with the same
/// header, type and period, and linked back to it.
/// </summary>
internal static class F0133109
{
    public static readonly Form Form = new("F01", "331", 9);

    // The form's footnote 11: category 6, a FOP on the simplified system.
    private const int InsuredPersonCategory = 6;

    private static readonly Lazy<XmlSchemaSet> Schemas = new(() => LoadSchemas(Form), LazyThreadSafetyMode.ExecutionAndPublication);

    public static byte[] Write(EsvAnnex annex, Filing filing, string declarationFileName) =>
        DpsXml.Write(Form, filing, new LinkedDoc(F0103309.Form, 2, declarationFileName), writer =>
        {
            Element(writer, TypeMark(filing.Type), 1);
            Element(writer, "HTIN", filing.Header.Rnokpp);
            Element(writer, "HNAME", filing.Name);
            Element(writer, PeriodMark(filing.Quarter), 1);
            Element(writer, "HZY", filing.Year);
            if (filing.Type == DeclarationType.Clarifying)
            {
                Element(writer, PeriodMark(filing.Quarter) + "P", 1);
                Element(writer, "HZYP", filing.Year);
            }

            if (annex.LeavesGroup3)
            {
                Element(writer, "H03", 1);
            }

            Element(writer, "HKVED", filing.Header.KvedCodes[0]);
            Element(writer, "R08G1D", Date(annex.From));
            Element(writer, "R08G2D", Date(annex.To));
            Element(writer, "R081G1", InsuredPersonCategory);
            foreach (var month in annex.Months)
            {
                Element(writer, $"R09{month.Month}G2", Amount(month.BaseKop));
                Element(writer, $"R09{month.Month}G3", Amount(month.RateBp));
                Element(writer, $"R09{month.Month}G4", Amount(month.EsvKop));
            }

            Element(writer, "R09G2", Amount(annex.BaseKop));
            Element(writer, "R09G4", Amount(annex.EsvKop));
            Element(writer, "HBOS", filing.Name);
        });

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content) => DpsXml.SchemaErrors(content, Schemas.Value);
}
