using System.Xml.Schema;
using TaxesUa.Engine;
using static TaxesUa.Api.Features.Declarations.DpsXml;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The quarterly group 3 declaration as an XML file for M.E.Doc and other software (the Cabinet has no import, ADR-025): form F0103309 (C_DOC F01,
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

        var fields = CabinetForm.Declaration(figures, header, type, filledOn);
        var content = DpsXml.Write(Form, filing, linked, writer => WriteFields(writer, fields));

        return new DeclarationXmlFiles(new DeclarationXml(fileName, content), annex);
    }

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content) => DpsXml.SchemaErrors(content, Schemas.Value);
}
