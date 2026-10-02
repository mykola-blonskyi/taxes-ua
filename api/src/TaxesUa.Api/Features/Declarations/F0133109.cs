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

    private static readonly Lazy<XmlSchemaSet> Schemas = new(() => LoadSchemas(Form), LazyThreadSafetyMode.ExecutionAndPublication);

    public static byte[] Write(EsvAnnex annex, Filing filing, string declarationFileName) =>
        DpsXml.Write(
            Form,
            filing,
            new LinkedDoc(F0103309.Form, 2, declarationFileName),
            writer => WriteFields(writer, CabinetForm.Annex(annex, filing.Header, filing.Type)));

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content) => DpsXml.SchemaErrors(content, Schemas.Value);
}
