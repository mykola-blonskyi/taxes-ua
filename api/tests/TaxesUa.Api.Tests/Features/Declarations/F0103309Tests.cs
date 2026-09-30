using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using TaxesUa.Api.Features.Declarations;
using TaxesUa.Engine;

namespace TaxesUa.Api.Tests.Features.Declarations;

// The golden files pin the writer byte for byte. After a deliberate change to the format, rerun with
// UPDATE_GOLDEN=1 to rewrite them in the source folder, then read the diff before committing it.
public sealed partial class F0103309Tests
{
    private static readonly Encoding Windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private static readonly F0103309.DeclarationHeader Header = new(
        "1234567890",
        26,
        50,
        "ГУ ДПС у м. Києві",
        "ФОП Іваненко Іван Іванович",
        "м. Київ, вул. Хрещатик, 1",
        ["62.01", "62.02"]);

    // Rule 15's 2026 example (receipts of 123,456.78 on 20 January and 98,765.43 on 15 April), carried on
    // with 50,000.00 on 10 August and 100,000.00 on 5 November; its crossing example; and a refund of
    // 23,456.78 in May that takes Q2's cumulative income below Q1's.
    private static readonly Dictionary<string, (DeclarationFigures Figures, DeclarationType Type, DateOnly FilledOn, string FileName)> Cases = new()
    {
        ["2026-q1"] = (
            new(2026, 1, 12_345_678, 0, 617_284, 0, 0, 617_284, 123_457, 0, 123_457, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 4, 20),
            "26501234567890F0103309100000000120320262650.xml"),
        ["2026-q2"] = (
            new(2026, 2, 22_222_221, 0, 1_111_111, 0, 617_284, 493_827, 222_222, 123_457, 98_765, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 7, 20),
            "26501234567890F0103309100000000130620262650.xml"),
        ["2026-q3"] = (
            new(2026, 3, 27_222_221, 0, 1_361_111, 0, 1_111_111, 250_000, 272_222, 222_222, 50_000, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 10, 20),
            "26501234567890F0103309100000000140920262650.xml"),
        ["2026-q4"] = (
            new(2026, 4, 37_222_221, 0, 1_861_111, 0, 1_361_111, 500_000, 372_222, 272_222, 100_000, 12 * 190_234),
            DeclarationType.Reporting,
            new DateOnly(2027, 2, 1),
            "26501234567890F0103309100000000151220262650.xml"),
        ["crossing-q3"] = (
            new(2026, 3, 1_009_104_900, 40_895_100, 50_455_245, 6_134_265, 40_000_000, 16_589_510, 10_500_000, 8_000_000, 2_500_000, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 10, 20),
            "26501234567890F0103309100000000140920262650.xml"),
        ["refund-q2"] = (
            new(2026, 2, 10_000_000, 0, 500_000, 0, 617_284, -117_284, 100_000, 123_457, -23_457, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 7, 20),
            "26501234567890F0103309100000000130620262650.xml"),
        ["clarifying-q2"] = (
            new(2026, 2, 22_222_221, 0, 1_111_111, 0, 617_284, 493_827, 222_222, 123_457, 98_765, null),
            DeclarationType.Clarifying,
            new DateOnly(2026, 9, 30),
            "26501234567890F0103309300000000130620262650.xml"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_file_matches_its_golden_copy_byte_for_byte(string name)
    {
        var (figures, type, filledOn, _) = Cases[name];

        var content = F0103309.Write(figures, Header, type, filledOn).Content;

        var golden = GoldenPath(name);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            var target = SourceGoldenPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            return;
        }

        Assert.True(File.Exists(golden), $"{golden} is missing; run with UPDATE_GOLDEN=1 to write it.");
        Assert.Equal(File.ReadAllBytes(golden), content);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_file_passes_the_vendored_schema(string name)
    {
        var (figures, type, filledOn, _) = Cases[name];

        var content = F0103309.Write(figures, Header, type, filledOn).Content;

        Assert.Empty(F0103309.SchemaErrors(content));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_file_is_windows_1251_with_the_lowercase_declaration_and_nothing_between_elements(string name)
    {
        var (figures, type, filledOn, _) = Cases[name];

        var content = F0103309.Write(figures, Header, type, filledOn).Content;

        var prefix = "<?xml version=\"1.0\" encoding=\"windows-1251\"?>"
            + "<DECLAR xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"F0103309.xsd\">";
        Assert.Equal(Encoding.ASCII.GetBytes(prefix), content.Take(prefix.Length));
        var text = Windows1251.GetString(content);
        Assert.Contains("<HNAME>Іваненко Іван Іванович</HNAME>", text);
        Assert.Contains("<HBOS>Іваненко Іван Іванович</HBOS>", text);
        Assert.DoesNotMatch(@">\s+<", text);
        Assert.DoesNotContain("R021G3", text);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_file_is_named_per_standard_729(string name)
    {
        var (figures, type, filledOn, fileName) = Cases[name];

        var written = F0103309.Write(figures, Header, type, filledOn).FileName;

        Assert.Equal(fileName, written);
        Assert.Matches(DgFilename(), written);
    }

    [Fact]
    public void Lines_07_and_09_appear_only_when_the_limit_is_crossed()
    {
        var under = Text("2026-q3");
        var crossed = Text("crossing-q3");

        Assert.DoesNotContain("R007G3", under);
        Assert.DoesNotContain("R009G3", under);
        Assert.Contains("<R007G3>408951.00</R007G3>", crossed);
        Assert.Contains("<R009G3>61342.65</R009G3>", crossed);
    }

    [Fact]
    public void A_refund_quarter_writes_negative_lines_as_the_arithmetic_gives_them()
    {
        var text = Text("refund-q2");

        Assert.Contains("<R0141G3>-1172.84</R0141G3><R014G3>-1172.84</R014G3>", text);
        Assert.Contains("<R025G3>-234.57</R025G3>", text);
    }

    [Fact]
    public void A_clarifying_declaration_names_the_same_quarter_as_the_period_it_clarifies()
    {
        var text = Text("clarifying-q2");

        Assert.Contains("<C_DOC_STAN>3</C_DOC_STAN>", text);
        Assert.Contains("<HZU>1</HZU><HHY>1</HHY><HZY>2026</HZY><HHYP>1</HHYP><HZYP>2026</HZYP>", text);
    }

    [Theory]
    [InlineData("ФОП Іваненко Іван", "Іваненко Іван")]
    [InlineData("Іваненко Іван", "Іваненко Іван")]
    [InlineData("фоп Іваненко Іван", "Іваненко Іван")]
    [InlineData("ФОП. Іваненко Іван", "Іваненко Іван")]
    [InlineData("ФОП.Іваненко Іван", "Іваненко Іван")]
    [InlineData("  ФОП   Іваненко Іван  ", "Іваненко Іван")]
    [InlineData("ФОПенко Іван", "ФОПенко Іван")]
    public void The_header_name_drops_a_leading_FOP(string sellerNameUk, string expected) =>
        Assert.Equal(expected, F0103309.HeaderName(sellerNameUk));

    [Fact]
    public void Characters_windows_1251_cannot_encode_are_named_per_field()
    {
        var header = Header with { Name = "ФОП Іваненко 😀", Address = "Łódź, вул. Тестова 1" };

        var errors = F0103309.Unwritable(header);

        Assert.Equal(2, errors.Length);
        Assert.StartsWith("name ", errors[0]);
        Assert.Contains("U+1F600", errors[0]);
        Assert.StartsWith("address ", errors[1]);
        Assert.Contains("U+0141", errors[1]);
        Assert.Contains("U+00F3", errors[1]);
    }

    [Fact]
    public void Characters_xml_1_0_cannot_carry_are_named_per_field_and_not_thrown()
    {
        var header = Header with { Address = "вул. Тестова\u0001 1", KvedCodes = ["62.01", "63.\u001F11"] };

        var errors = F0103309.Unwritable(header);

        Assert.Equal(2, errors.Length);
        Assert.StartsWith("address ", errors[0]);
        Assert.Contains("U+0001", errors[0]);
        Assert.StartsWith("kvedCodes[1] ", errors[1]);
        Assert.Contains("U+001F", errors[1]);
        Assert.Contains("XML 1.0", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_modifier_apostrophe_is_written_as_an_ascii_one()
    {
        var header = Header with { Name = "ФОП Марʼяна Іваненко" };
        var (figures, type, filledOn, _) = Cases["2026-q1"];

        var content = F0103309.Write(figures, header, type, filledOn).Content;

        Assert.Empty(F0103309.Unwritable(header));
        Assert.Contains("<HNAME>Мар'яна Іваненко</HNAME>", Windows1251.GetString(content));
        Assert.Empty(F0103309.SchemaErrors(content));
    }

    [Fact]
    public void A_tax_office_code_that_is_no_DPS_office_fails_the_schema()
    {
        var (figures, type, filledOn, _) = Cases["2026-q1"];

        var content = F0103309.Write(figures, Header with { TaxOfficeRegion = 29, TaxOfficeDistrict = 0 }, type, filledOn).Content;

        Assert.Contains(F0103309.SchemaErrors(content), error => error.Contains("C_STI_ORIG", StringComparison.Ordinal));
    }

    [Fact]
    public void An_amount_without_two_decimals_fails_the_schema()
    {
        var tampered = Text("2026-q1").Replace("<R006G3>123456.78</R006G3>", "<R006G3>123456.7</R006G3>", StringComparison.Ordinal);

        Assert.NotEmpty(F0103309.SchemaErrors(Windows1251.GetBytes(tampered)));
    }

    [Fact]
    public void Malformed_xml_is_an_error_rather_than_an_exception()
    {
        Assert.NotEmpty(F0103309.SchemaErrors(Encoding.ASCII.GetBytes("<DECLAR><DECLARHEAD>")));
    }

    private static string Text(string name)
    {
        var (figures, type, filledOn, _) = Cases[name];
        return Windows1251.GetString(F0103309.Write(figures, Header, type, filledOn).Content);
    }

    private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name + ".xml");

    private static string SourceGoldenPath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", name + ".xml");

    // DGFilename from common_types.xsd; XSD patterns match the whole value.
    [GeneratedRegex(@"^([0-9]){4}(([0-9]{10})|(00[АБВГДЕЄЖЗИІКЛМНОПРСТУФХЦЧШЩЮЯ]{2}[0-9]{6}))([JF]((0[1-9])|([1-9][0-9])))(([0-9]{2}[1-9])|([0-9][1-9][0-9])|([1-9][0-9]{2}))(([1-9][0-9])|(0[1-9]))[1-3]([0-9]{2})([0-9]{7})([1-5])((0[1-9])|(1[0-2]))(20[0-9]{2})([0-9]){4}\.[xX][mM][lL]$")]
    private static partial Regex DgFilename();
}
