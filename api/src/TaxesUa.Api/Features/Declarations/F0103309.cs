using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Declarations;

/// <summary>
/// The quarterly group 3 declaration as the Electronic Cabinet imports it: form F0103309 (C_DOC F01,
/// C_DOC_SUB 033, C_DOC_VER 9), windows-1251, elements in the XSD's order with nothing between them,
/// named per DPS standard No. 729 (ADR-016). Pure: the same input gives the same bytes.
/// </summary>
internal static partial class F0103309
{
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    private const string ResourcePrefix = "TaxesUa.Api.Schemas.";

    private static readonly Encoding Windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas, LazyThreadSafetyMode.ExecutionAndPublication);

    internal sealed record DeclarationHeader(
        string Rnokpp,
        int TaxOfficeRegion,
        int TaxOfficeDistrict,
        string TaxOfficeName,
        string Name,
        string Address,
        IReadOnlyList<string> KvedCodes);

    internal sealed record DeclarationXml(string FileName, byte[] Content);

    /// <summary>The name without the "ФОП" the invoices carry: the form asks for the person's name.</summary>
    public static string HeaderName(string sellerNameUk) => FopPrefix().Replace(sellerNameUk.Trim(), string.Empty).Trim();

    /// <summary>
    /// One error per header field holding a character XML 1.0 cannot carry (a control character) or
    /// windows-1251 has no byte for, which the file would otherwise fail to write or carry as a character
    /// reference the Cabinet may not read back.
    /// </summary>
    public static string[] Unwritable(DeclarationHeader header)
    {
        (string Field, string Value)[] fields =
        [
            ("rnokpp", header.Rnokpp),
            ("taxOfficeName", header.TaxOfficeName),
            ("name", header.Name),
            ("address", header.Address),
            .. header.KvedCodes.Select((code, i) => ($"kvedCodes[{i}]", code)),
        ];

        return
        [
            .. fields.SelectMany(field => Problems(field.Field, Text(field.Value))),
        ];
    }

    private static IEnumerable<string> Problems(string field, string value)
    {
        var runes = value.EnumerateRunes().Distinct().ToArray();
        var notXml = runes.Where(rune => !IsXmlCharacter(rune)).ToArray();
        if (notXml.Length > 0)
        {
            yield return $"{field} contains {Describe(notXml)}, which XML 1.0 cannot carry.";
        }

        var notEncodable = runes.Where(rune => IsXmlCharacter(rune) && !IsWindows1251(rune)).ToArray();
        if (notEncodable.Length > 0)
        {
            yield return $"{field} contains {Describe(notEncodable)}, which windows-1251 cannot encode.";
        }
    }

    private static string Describe(IEnumerable<Rune> runes) =>
        string.Join(", ", runes.Select(rune => $"'{rune}' (U+{rune.Value:X4})"));

    public static DeclarationXml Write(
        DeclarationFigures figures, DeclarationHeader header, DeclarationType type, DateOnly filledOn)
    {
        var quarter = figures.Quarter;
        var periodMonth = 3 * quarter;
        var periodType = quarter + 1;
        var state = (int)type + 1;
        var taxOffice = header.TaxOfficeRegion * 100 + header.TaxOfficeDistrict;
        var name = HeaderName(header.Name);
        var filled = filledOn.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = Windows1251,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("DECLAR");
            writer.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
            writer.WriteAttributeString("xsi", "noNamespaceSchemaLocation", XsiNamespace, "F0103309.xsd");

            writer.WriteStartElement("DECLARHEAD");
            Element(writer, "TIN", header.Rnokpp);
            Element(writer, "C_DOC", "F01");
            Element(writer, "C_DOC_SUB", "033");
            Element(writer, "C_DOC_VER", "9");
            Element(writer, "C_DOC_TYPE", 0);
            Element(writer, "C_DOC_CNT", 1);
            Element(writer, "C_REG", header.TaxOfficeRegion);
            Element(writer, "C_RAJ", header.TaxOfficeDistrict);
            Element(writer, "PERIOD_MONTH", periodMonth);
            Element(writer, "PERIOD_TYPE", periodType);
            Element(writer, "PERIOD_YEAR", figures.Year);
            Element(writer, "C_STI_ORIG", taxOffice);
            Element(writer, "C_DOC_STAN", state);
            Element(writer, "D_FILL", filled);
            writer.WriteEndElement();

            writer.WriteStartElement("DECLARBODY");
            Element(writer, type switch
            {
                DeclarationType.Reporting => "HZ",
                DeclarationType.NewReporting => "HZN",
                DeclarationType.Clarifying => "HZU",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            }, 1);
            Element(writer, PeriodMark(quarter), 1);
            Element(writer, "HZY", figures.Year);
            if (type == DeclarationType.Clarifying)
            {
                Element(writer, PeriodMark(quarter) + "P", 1);
                Element(writer, "HZYP", figures.Year);
            }

            Element(writer, "HSTI", header.TaxOfficeName);
            Element(writer, "HNAME", name);
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
            Element(writer, "R023G3", Amount(figures.MilitaryLevyKop));
            Element(writer, "R024G3", Amount(figures.PreviousMilitaryLevyKop));
            Element(writer, "R025G3", Amount(figures.MilitaryLevyPayableKop));
            Element(writer, "HFILL", filled);
            Element(writer, "HBOS", name);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{header.TaxOfficeRegion:00}{header.TaxOfficeDistrict:00}{header.Rnokpp.PadLeft(10, '0')}F0103309{state}00{1:0000000}{periodType}{periodMonth:00}{figures.Year:0000}{taxOffice:0000}.xml");
        return new DeclarationXml(fileName, stream.ToArray());
    }

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
            errors.Add($"{e.Exception.LineNumber}:{e.Exception.LinePosition} {e.Message}");

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(content), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            errors.Add($"{exception.LineNumber}:{exception.LinePosition} {exception.Message}");
        }

        return [.. errors];
    }

    private static string PeriodMark(int quarter) => quarter switch
    {
        1 => "H1KV",
        2 => "HHY",
        3 => "H3KV",
        4 => "HY",
        _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, null),
    };

    // Integer arithmetic on kopecks, per the no-decimal rule: sign, hryvnias, a dot and two digits.
    private static string Amount(long kop)
    {
        var magnitude = Math.Abs(kop);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(kop < 0 ? "-" : string.Empty)}{magnitude / 100}.{magnitude % 100:00}");
    }

    // The modifier apostrophe is how Ukrainian names are often typed and has no windows-1251 byte;
    // line breaks would otherwise depend on the platform's newline.
    private static string Text(string value) => value
        .Replace('ʼ', '\'')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace('\t', ' ');

    // The XML 1.0 Char production; a lone surrogate never reaches a Rune.
    private static bool IsXmlCharacter(Rune rune) =>
        rune.Value is 0x9 or 0xA or 0xD
        || rune.Value is >= 0x20 and <= 0xD7FF
        || rune.Value is >= 0xE000 and <= 0xFFFD
        || rune.Value >= 0x10000;

    private static bool IsWindows1251(Rune rune) =>
        Windows1251.GetString(Windows1251.GetBytes(rune.ToString())) == rune.ToString();

    private static void Element(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteString(Text(value));
        writer.WriteFullEndElement();
    }

    private static void Element(XmlWriter writer, string name, int value) =>
        Element(writer, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Row(XmlWriter writer, string name, int rowNumber, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteAttributeString("ROWNUM", rowNumber.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(Text(value));
        writer.WriteFullEndElement();
    }

    private static XmlSchemaSet LoadSchemas()
    {
        // The vendored schemas declare windows-1251, and XmlReader looks encodings up by name, which
        // only finds the code pages once their provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var resolver = new EmbeddedSchemaResolver();
        var schemas = new XmlSchemaSet { XmlResolver = resolver };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = XmlReader.Create(OpenSchema("F0103309.xsd"), settings, EmbeddedSchemaResolver.BaseUri + "F0103309.xsd"))
        {
            schemas.Add(null, reader);
        }

        schemas.Compile();
        return schemas;
    }

    private static Stream OpenSchema(string fileName) =>
        typeof(F0103309).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
        ?? throw new InvalidOperationException($"The embedded schema {fileName} is missing.");

    /// <summary>Serves the xs:include of common_types.xsd from the embedded copy, and nothing else.</summary>
    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public const string BaseUri = "embedded:///";

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            absoluteUri.AbsoluteUri == BaseUri + "common_types.xsd"
                ? OpenSchema("common_types.xsd")
                : throw new XmlException($"The schema refers to {absoluteUri}, which is not vendored.");

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new(new Uri(BaseUri), relativeUri);
    }

    [GeneratedRegex(@"^ФОП(\s+|\s*\.\s*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FopPrefix();
}
