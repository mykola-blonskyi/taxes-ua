using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using TaxesUa.Engine;

namespace TaxesUa.Api.Features.Declarations;

internal sealed record DeclarationHeader(
    string Rnokpp,
    int TaxOfficeRegion,
    int TaxOfficeDistrict,
    string TaxOfficeName,
    string Name,
    string Address,
    IReadOnlyList<string> KvedCodes);

internal sealed record DeclarationXml(string FileName, byte[] Content);

/// <summary>The declaration's file and, on the year's last group 3 declaration, its annex 1.</summary>
internal sealed record DeclarationXmlFiles(DeclarationXml Declaration, DeclarationXml? Annex);

/// <summary>
/// What every DPS form file shares (ADR-016): windows-1251, elements with nothing between them, amounts
/// with two decimals, the header's period and state codes, the file name per standard No. 729, and
/// validation against the vendored schemas.
/// </summary>
internal static partial class DpsXml
{
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    private const string ResourcePrefix = "TaxesUa.Api.Schemas.";

    private static readonly Encoding Windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    /// <summary>C_DOC, C_DOC_SUB and C_DOC_VER, which name a form and its schema.</summary>
    internal sealed record Form(string Doc, string DocSub, int DocVer)
    {
        public string Code => string.Create(CultureInfo.InvariantCulture, $"{Doc}{DocSub}{DocVer:00}");
    }

    /// <summary>The header values a declaration and its annex share, derived once for both.</summary>
    internal sealed record Filing(DeclarationHeader Header, DeclarationType Type, int Year, int Quarter, DateOnly FilledOn)
    {
        public int PeriodMonth => 3 * Quarter;

        public int PeriodType => Quarter + 1;

        public int State => (int)Type + 1;

        public int TaxOffice => Header.TaxOfficeRegion * 100 + Header.TaxOfficeDistrict;

        public string Filled => Date(FilledOn);

        public string Name => HeaderName(Header.Name);

        public string FileName(Form form) => string.Create(
            CultureInfo.InvariantCulture,
            $"{Header.TaxOfficeRegion:00}{Header.TaxOfficeDistrict:00}{Header.Rnokpp.PadLeft(10, '0')}{form.Code}{State}00{1:0000000}{PeriodType}{PeriodMonth:00}{Year:0000}{TaxOffice:0000}.xml");
    }

    /// <summary>A form this file names in LINKED_DOCS: <c>Type</c> 1 for an annex, 2 for the main form.</summary>
    internal sealed record LinkedDoc(Form Form, int Type, string FileName);

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

    /// <summary>
    /// The document through DECLARHEAD's C_DOC_STAN and LINKED_DOCS, then D_FILL; <paramref name="body"/>
    /// writes DECLARBODY's children.
    /// </summary>
    public static byte[] Write(Form form, Filing filing, LinkedDoc? linked, Action<XmlWriter> body)
    {
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
            writer.WriteAttributeString("xsi", "noNamespaceSchemaLocation", XsiNamespace, $"{form.Code}.xsd");

            writer.WriteStartElement("DECLARHEAD");
            Element(writer, "TIN", filing.Header.Rnokpp);
            Element(writer, "C_DOC", form.Doc);
            Element(writer, "C_DOC_SUB", form.DocSub);
            Element(writer, "C_DOC_VER", form.DocVer);
            Element(writer, "C_DOC_TYPE", 0);
            Element(writer, "C_DOC_CNT", 1);
            Element(writer, "C_REG", filing.Header.TaxOfficeRegion);
            Element(writer, "C_RAJ", filing.Header.TaxOfficeDistrict);
            Element(writer, "PERIOD_MONTH", filing.PeriodMonth);
            Element(writer, "PERIOD_TYPE", filing.PeriodType);
            Element(writer, "PERIOD_YEAR", filing.Year);
            Element(writer, "C_STI_ORIG", filing.TaxOffice);
            Element(writer, "C_DOC_STAN", filing.State);
            if (linked is not null)
            {
                writer.WriteStartElement("LINKED_DOCS");
                writer.WriteStartElement("DOC");
                writer.WriteAttributeString("NUM", "1");
                writer.WriteAttributeString("TYPE", linked.Type.ToString(CultureInfo.InvariantCulture));
                Element(writer, "C_DOC", linked.Form.Doc);
                Element(writer, "C_DOC_SUB", linked.Form.DocSub);
                Element(writer, "C_DOC_VER", linked.Form.DocVer);
                Element(writer, "C_DOC_TYPE", 0);
                Element(writer, "C_DOC_CNT", 1);
                Element(writer, "C_DOC_STAN", filing.State);
                Element(writer, "FILENAME", linked.FileName);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            Element(writer, "D_FILL", filing.Filled);
            writer.WriteEndElement();

            writer.WriteStartElement("DECLARBODY");
            body(writer);
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>HZ, HZN or HZU.</summary>
    public static string TypeMark(DeclarationType type) => type switch
    {
        DeclarationType.Reporting => "HZ",
        DeclarationType.NewReporting => "HZN",
        DeclarationType.Clarifying => "HZU",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>H1KV, HHY, H3KV or HY; a clarifying form adds <c>P</c> for the period it clarifies.</summary>
    public static string PeriodMark(int quarter) => quarter switch
    {
        1 => "H1KV",
        2 => "HHY",
        3 => "H3KV",
        4 => "HY",
        _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, null),
    };

    /// <summary>Every schema error and warning, with its line and position; empty when the file is valid.</summary>
    public static string[] SchemaErrors(byte[] content, XmlSchemaSet schemas)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
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

    /// <summary>
    /// A form's schema with the common_types.xsd it includes, each form in a set of its own: every DPS
    /// schema declares the same global DECLAR element.
    /// </summary>
    public static XmlSchemaSet LoadSchemas(Form form)
    {
        // The vendored schemas declare windows-1251, and XmlReader looks encodings up by name, which
        // only finds the code pages once their provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var fileName = form.Code + ".xsd";
        var schemas = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var reader = XmlReader.Create(OpenSchema(fileName), settings, EmbeddedSchemaResolver.BaseUri + fileName))
        {
            schemas.Add(null, reader);
        }

        schemas.Compile();
        return schemas;
    }

    // Integer arithmetic on kopecks, per the no-decimal rule: sign, hryvnias, a dot and two digits.
    public static string Amount(long kop)
    {
        var magnitude = Math.Abs(kop);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(kop < 0 ? "-" : string.Empty)}{magnitude / 100}.{magnitude % 100:00}");
    }

    public static string Date(DateOnly date) => date.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

    /// <summary>The body's fields in list order; a field with no value has no element.</summary>
    public static void WriteFields(XmlWriter writer, IEnumerable<CabinetField> fields)
    {
        foreach (var field in fields.Where(field => field.Value is not null))
        {
            if (field.Row > 0)
            {
                Row(writer, field.Element, field.Row, field.Value!);
            }
            else
            {
                Element(writer, field.Element, field.Value!);
            }
        }
    }

    public static void Element(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteString(Text(value));
        writer.WriteFullEndElement();
    }

    public static void Element(XmlWriter writer, string name, int value) =>
        Element(writer, name, value.ToString(CultureInfo.InvariantCulture));

    public static void Row(XmlWriter writer, string name, int rowNumber, string value)
    {
        writer.WriteStartElement(name);
        writer.WriteAttributeString("ROWNUM", rowNumber.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(Text(value));
        writer.WriteFullEndElement();
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

    private static Stream OpenSchema(string fileName) =>
        typeof(DpsXml).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
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
