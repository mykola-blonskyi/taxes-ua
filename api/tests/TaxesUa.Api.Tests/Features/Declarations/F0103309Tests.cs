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

    private static readonly DeclarationHeader Header = new(
        "1234567890",
        26,
        50,
        "ГУ ДПС у м. Києві",
        "ФОП Іваненко Іван Іванович",
        "м. Київ, вул. Хрещатик, 1",
        ["62.01", "62.02"],
        "fop@example.com",
        "+380501234567");

    private const long MinWageKop = 864_700;

    private const int EsvRateBp = 2_200;

    // 2026 with the minimum wage of 8,647.00 and ESV at 22%. Registered on 10 March 2026, March owes the
    // full minimum like every other month.
    private static readonly EsvAnnex FullYear = Annex(new DateOnly(2026, 1, 1), 4, 1, false);

    private static readonly EsvAnnex FirstYear = Annex(new DateOnly(2026, 3, 10), 4, 3, false);

    private static readonly EsvAnnex CrossedInQ3 = Annex(new DateOnly(2026, 1, 1), 3, 1, true);

    // Rule 15's 2026 example (receipts of 123,456.78 on 20 January and 98,765.43 on 15 April), carried on
    // with 50,000.00 on 10 August and 100,000.00 on 5 November; its crossing example; a refund of
    // 23,456.78 in May that takes Q2's cumulative income below Q1's; and a first year registered on
    // 10 March 2026 with 40,000.00 of income.
    private static readonly Dictionary<string, (DeclarationFigures Figures, DeclarationType Type, DateOnly FilledOn, string FileName, string? AnnexFileName)> Cases = new()
    {
        ["2026-q1"] = (
            new(2026, 1, 12_345_678, 0, 617_284, 0, 0, 617_284, 123_457, 0, 123_457, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 4, 20),
            "26501234567890F0103309100000000120320262650.xml",
            null),
        ["2026-q2"] = (
            new(2026, 2, 22_222_221, 0, 1_111_111, 0, 617_284, 493_827, 222_222, 123_457, 98_765, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 7, 20),
            "26501234567890F0103309100000000130620262650.xml",
            null),
        ["2026-q3"] = (
            new(2026, 3, 27_222_221, 0, 1_361_111, 0, 1_111_111, 250_000, 272_222, 222_222, 50_000, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 10, 20),
            "26501234567890F0103309100000000140920262650.xml",
            null),
        ["2026-q4"] = (
            new(2026, 4, 37_222_221, 0, 1_861_111, 0, 1_361_111, 500_000, 372_222, 272_222, 100_000, FullYear),
            DeclarationType.Reporting,
            new DateOnly(2027, 2, 1),
            "26501234567890F0103309100000000151220262650.xml",
            "26501234567890F0133109100000000151220262650.xml"),
        ["first-year-q4"] = (
            new(2026, 4, 4_000_000, 0, 200_000, 0, 200_000, 0, 40_000, 40_000, 0, FirstYear),
            DeclarationType.Reporting,
            new DateOnly(2027, 2, 1),
            "26501234567890F0103309100000000151220262650.xml",
            "26501234567890F0133109100000000151220262650.xml"),
        ["crossing-q3"] = (
            new(2026, 3, 1_009_104_900, 40_895_100, 50_455_245, 6_134_265, 40_000_000, 16_589_510, 10_500_000, 8_000_000, 2_500_000, CrossedInQ3),
            DeclarationType.Reporting,
            new DateOnly(2026, 10, 20),
            "26501234567890F0103309100000000140920262650.xml",
            "26501234567890F0133109100000000140920262650.xml"),
        ["refund-q2"] = (
            new(2026, 2, 10_000_000, 0, 500_000, 0, 617_284, -117_284, 100_000, 123_457, -23_457, null),
            DeclarationType.Reporting,
            new DateOnly(2026, 7, 20),
            "26501234567890F0103309100000000130620262650.xml",
            null),
        ["clarifying-q2"] = (
            new(2026, 2, 22_222_221, 0, 1_111_111, 0, 617_284, 493_827, 222_222, 123_457, 98_765, null),
            DeclarationType.Clarifying,
            new DateOnly(2026, 9, 30),
            "26501234567890F0103309300000000130620262650.xml",
            null),
        ["clarifying-q4"] = (
            new(2026, 4, 37_222_221, 0, 1_861_111, 0, 1_361_111, 500_000, 372_222, 272_222, 100_000, FullYear),
            DeclarationType.Clarifying,
            new DateOnly(2027, 3, 15),
            "26501234567890F0103309300000000151220262650.xml",
            "26501234567890F0133109300000000151220262650.xml"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    public static TheoryData<string> AnnexCaseNames => [.. Cases.Where(item => item.Value.AnnexFileName is not null).Select(item => item.Key)];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_files_match_their_golden_copies_byte_for_byte(string name)
    {
        var files = Write(name);

        AssertGolden(name, files.Declaration.Content);
        if (files.Annex is { } annex)
        {
            AssertGolden(name + "-annex", annex.Content);
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_files_pass_the_vendored_schemas(string name)
    {
        var files = Write(name);

        Assert.Empty(F0103309.SchemaErrors(files.Declaration.Content));
        if (files.Annex is { } annex)
        {
            Assert.Empty(F0133109.SchemaErrors(annex.Content));
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_files_are_windows_1251_with_the_lowercase_declaration_and_nothing_between_elements(string name)
    {
        var files = Write(name);

        AssertEncoding(files.Declaration.Content, "F0103309");
        var text = Windows1251.GetString(files.Declaration.Content);
        Assert.Contains("<HNAME>Іваненко Іван Іванович</HNAME>", text);
        Assert.Contains("<HBOS>Іван ІВАНЕНКО</HBOS>", text);
        if (files.Annex is { } annex)
        {
            AssertEncoding(annex.Content, "F0133109");
            var annexText = Windows1251.GetString(annex.Content);
            Assert.Contains("<HNAME>Іваненко Іван Іванович</HNAME>", annexText);
            Assert.Contains("<HBOS>Іван ІВАНЕНКО</HBOS>", annexText);
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_files_are_named_per_standard_729(string name)
    {
        var (_, _, _, fileName, annexFileName) = Cases[name];

        var files = Write(name);

        Assert.Equal(fileName, files.Declaration.FileName);
        Assert.Matches(DgFilename(), files.Declaration.FileName);
        Assert.Equal(annexFileName, files.Annex?.FileName);
        if (annexFileName is not null)
        {
            Assert.Matches(DgFilename(), annexFileName);
        }
    }

    [Theory]
    [MemberData(nameof(AnnexCaseNames))]
    public void The_declaration_and_its_annex_link_each_other(string name)
    {
        var files = Write(name);
        var state = Cases[name].Type == DeclarationType.Clarifying ? 3 : 1;

        var declaration = Windows1251.GetString(files.Declaration.Content);
        var annex = Windows1251.GetString(files.Annex!.Content);

        Assert.Contains(
            $"<C_DOC_STAN>{state}</C_DOC_STAN><LINKED_DOCS><DOC NUM=\"1\" TYPE=\"1\"><C_DOC>F01</C_DOC><C_DOC_SUB>331</C_DOC_SUB>"
            + $"<C_DOC_VER>9</C_DOC_VER><C_DOC_TYPE>0</C_DOC_TYPE><C_DOC_CNT>1</C_DOC_CNT><C_DOC_STAN>{state}</C_DOC_STAN>"
            + $"<FILENAME>{files.Annex.FileName}</FILENAME></DOC></LINKED_DOCS><D_FILL>",
            declaration);
        Assert.Contains("<HD1>1</HD1><HFILL>", declaration);
        Assert.Contains(
            $"<C_DOC_STAN>{state}</C_DOC_STAN><LINKED_DOCS><DOC NUM=\"1\" TYPE=\"2\"><C_DOC>F01</C_DOC><C_DOC_SUB>033</C_DOC_SUB>"
            + $"<C_DOC_VER>9</C_DOC_VER><C_DOC_TYPE>0</C_DOC_TYPE><C_DOC_CNT>1</C_DOC_CNT><C_DOC_STAN>{state}</C_DOC_STAN>"
            + $"<FILENAME>{files.Declaration.FileName}</FILENAME></DOC></LINKED_DOCS><D_FILL>",
            annex);
    }

    [Theory]
    [MemberData(nameof(AnnexCaseNames))]
    public void Line_21_is_the_annex_total_and_each_month_is_its_base_at_the_rate(string name)
    {
        var files = Write(name);
        var declaration = Windows1251.GetString(files.Declaration.Content);
        var annex = Windows1251.GetString(files.Annex!.Content);

        var total = Regex.Match(annex, "<R09G4>([^<]+)</R09G4>").Groups[1].Value;
        Assert.Contains($"<R021G3>{total}</R021G3>", declaration);
        var months = Regex.Matches(annex, @"<R09(\d+)G2>(\d+)\.(\d\d)</R09\1G2><R09\1G3>22\.00</R09\1G3><R09\1G4>(\d+)\.(\d\d)</R09\1G4>");
        Assert.Equal(Cases[name].Figures.EsvAnnex!.Months.Count, months.Count);
        foreach (Match month in months)
        {
            var baseKop = long.Parse(month.Groups[2].Value + month.Groups[3].Value);
            var esvKop = long.Parse(month.Groups[4].Value + month.Groups[5].Value);
            Assert.Equal(Money.ApplyBp(baseKop, EsvRateBp), esvKop);
        }

        Assert.Equal(
            months.Sum(month => long.Parse(month.Groups[4].Value + month.Groups[5].Value)),
            long.Parse(total.Replace(".", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_full_year_reports_twelve_months_of_the_minimum_wage_from_1_january()
    {
        var annex = AnnexText("2026-q4");

        Assert.Contains("<HZ>1</HZ><HTIN>1234567890</HTIN><HNAME>Іваненко Іван Іванович</HNAME><HY>1</HY><HZY>2026</HZY><HKVED>62.01</HKVED>", annex);
        Assert.Contains("<R08G1D>01012026</R08G1D><R08G2D>31122026</R08G2D><R081G1>6</R081G1><R091G2>8647.00</R091G2><R091G3>22.00</R091G3><R091G4>1902.34</R091G4>", annex);
        Assert.Contains("<R0912G4>1902.34</R0912G4><R09G2>103764.00</R09G2><R09G4>22828.08</R09G4><HBOS>", annex);
        Assert.Contains("<R021G3>22828.08</R021G3>", DeclarationText("2026-q4"));
    }

    [Fact]
    public void A_first_year_starts_at_registration_with_the_registration_month_at_the_full_minimum()
    {
        var annex = AnnexText("first-year-q4");

        Assert.Contains("<R08G1D>10032026</R08G1D><R08G2D>31122026</R08G2D><R081G1>6</R081G1><R093G2>8647.00</R093G2><R093G3>22.00</R093G3><R093G4>1902.34</R093G4><R094G2>", annex);
        Assert.DoesNotContain("<R091G2>", annex);
        Assert.DoesNotContain("<R092G2>", annex);
        Assert.Contains("<R09G2>86470.00</R09G2><R09G4>19023.40</R09G4>", annex);
        Assert.Contains("<R021G3>19023.40</R021G3>", DeclarationText("first-year-q4"));
    }

    [Fact]
    public void The_crossing_quarter_carries_the_annex_for_the_months_before_the_switch()
    {
        var annex = AnnexText("crossing-q3");

        Assert.Contains("<H3KV>1</H3KV><HZY>2026</HZY><H03>1</H03><HKVED>62.01</HKVED><R08G1D>01012026</R08G1D><R08G2D>30092026</R08G2D>", annex);
        Assert.Contains("<R099G4>1902.34</R099G4><R09G2>77823.00</R09G2><R09G4>17121.06</R09G4>", annex);
        Assert.DoesNotContain("<R0910G2>", annex);
        Assert.Contains("<R021G3>17121.06</R021G3>", DeclarationText("crossing-q3"));
        Assert.DoesNotContain("<H03>", AnnexText("2026-q4"));
    }

    [Fact]
    public void A_clarifying_annex_names_the_same_period_as_the_one_it_clarifies()
    {
        var annex = AnnexText("clarifying-q4");

        Assert.Contains("<HZU>1</HZU><HTIN>1234567890</HTIN><HNAME>Іваненко Іван Іванович</HNAME><HY>1</HY><HZY>2026</HZY><HYP>1</HYP><HZYP>2026</HZYP><HKVED>", annex);
    }

    [Theory]
    [InlineData("2026-q1")]
    [InlineData("2026-q3")]
    [InlineData("refund-q2")]
    [InlineData("clarifying-q2")]
    public void A_declaration_without_an_annex_has_no_line_21_link_or_annex_flag(string name)
    {
        var files = Write(name);
        var text = Windows1251.GetString(files.Declaration.Content);

        Assert.Null(files.Annex);
        Assert.DoesNotContain("R021G3", text);
        Assert.DoesNotContain("LINKED_DOCS", text);
        Assert.DoesNotContain("HD1", text);
    }

    [Fact]
    public void Lines_07_and_09_appear_only_when_the_limit_is_crossed()
    {
        var under = DeclarationText("2026-q3");
        var crossed = DeclarationText("crossing-q3");

        Assert.DoesNotContain("R007G3", under);
        Assert.DoesNotContain("R009G3", under);
        Assert.Contains("<R007G3>408951.00</R007G3>", crossed);
        Assert.Contains("<R009G3>61342.65</R009G3>", crossed);
    }

    [Fact]
    public void A_refund_quarter_writes_negative_lines_as_the_arithmetic_gives_them()
    {
        var text = DeclarationText("refund-q2");

        Assert.Contains("<R0141G3>-1172.84</R0141G3><R014G3>-1172.84</R014G3>", text);
        Assert.Contains("<R025G3>-234.57</R025G3>", text);
    }

    [Fact]
    public void A_clarifying_declaration_names_the_same_quarter_as_the_period_it_clarifies()
    {
        var text = DeclarationText("clarifying-q2");

        Assert.Contains("<C_DOC_STAN>3</C_DOC_STAN>", text);
        Assert.Contains("<HZU>1</HZU><HHY>1</HHY><HZY>2026</HZY><HHYP>1</HHYP><HZYP>2026</HZYP>", text);
    }

    [Fact]
    public void The_header_carries_the_email_and_phone_after_the_address_and_names_each_KVED()
    {
        var text = DeclarationText("2026-q3");

        Assert.Contains(
            "<HLOC>м. Київ, вул. Хрещатик, 1</HLOC><HEMAIL>fop@example.com</HEMAIL><HTEL>+380501234567</HTEL><HTIN>1234567890</HTIN>",
            text);
        Assert.Contains(
            "<T1RXXXXG1S ROWNUM=\"1\">62.01</T1RXXXXG1S><T1RXXXXG1S ROWNUM=\"2\">62.02</T1RXXXXG1S>"
            + "<T1RXXXXG2S ROWNUM=\"1\">Комп'ютерне програмування</T1RXXXXG2S>"
            + "<T1RXXXXG2S ROWNUM=\"2\">Консультування з питань інформатизації</T1RXXXXG2S>",
            text);
    }

    [Fact]
    public void Without_an_email_or_phone_the_header_leaves_both_out_and_still_passes_the_schemas()
    {
        var (figures, type, filledOn, _, _) = Cases["2026-q4"];

        var files = F0103309.Write(figures, Header with { Email = null, Phone = null }, type, filledOn);
        var text = Windows1251.GetString(files.Declaration.Content);

        Assert.Contains("<HLOC>м. Київ, вул. Хрещатик, 1</HLOC><HTIN>1234567890</HTIN>", text);
        Assert.DoesNotContain("HEMAIL", text);
        Assert.DoesNotContain("HTEL", text);
        Assert.Empty(F0103309.SchemaErrors(files.Declaration.Content));
        Assert.Empty(F0133109.SchemaErrors(files.Annex!.Content));
    }

    [Fact]
    public void A_KVED_code_outside_the_list_is_written_with_an_empty_name()
    {
        var (figures, type, filledOn, _, _) = Cases["2026-q3"];

        var files = F0103309.Write(figures, Header with { KvedCodes = ["62.01", "01.11"] }, type, filledOn);
        var text = Windows1251.GetString(files.Declaration.Content);

        Assert.Contains("<T1RXXXXG2S ROWNUM=\"2\"></T1RXXXXG2S>", text);
        Assert.Empty(F0103309.SchemaErrors(files.Declaration.Content));
    }

    [Fact]
    public void The_filing_date_is_the_day_the_file_is_written_in_both_the_head_and_the_footer()
    {
        var text = DeclarationText("2026-q3");

        Assert.Contains("<D_FILL>20102026</D_FILL>", text);
        Assert.Contains("<HFILL>20102026</HFILL>", text);
    }

    [Theory]
    [InlineData("63.99", "Надання інших інформаційних послуг, н.в.і.у.")]
    [InlineData("74.90", "Інша професійна, наукова та технічна діяльність, н.в.і.у.")]
    [InlineData("82.99", "Надання інших допоміжних комерційних послуг, н.в.і.у.")]
    [InlineData("85.59", "Інші види освіти, н.в.і.у.")]
    [InlineData("62.01", "Комп'ютерне програмування")]
    public void A_KVED_name_is_written_as_the_classifier_spells_it(string code, string name) =>
        Assert.Equal(name, Kved.Name(code));

    [Theory]
    [InlineData("Іваненко Іван Іванович", "Іван ІВАНЕНКО")]
    [InlineData("Іваненко Іван", "Іван ІВАНЕНКО")]
    [InlineData("Мар'яненко-Їжак Єва Ґалиївна", "Єва МАР'ЯНЕНКО-ЇЖАК")]
    [InlineData("Іваненко", "ІВАНЕНКО")]
    public void The_signature_is_the_given_name_and_the_surname_in_capitals(string name, string expected) =>
        Assert.Equal(expected, DpsXml.Signature(name));

    [Theory]
    [InlineData("ФОП Іваненко Іван", "Іваненко Іван")]
    [InlineData("Іваненко Іван", "Іваненко Іван")]
    [InlineData("фоп Іваненко Іван", "Іваненко Іван")]
    [InlineData("ФОП. Іваненко Іван", "Іваненко Іван")]
    [InlineData("ФОП.Іваненко Іван", "Іваненко Іван")]
    [InlineData("  ФОП   Іваненко Іван  ", "Іваненко Іван")]
    [InlineData("ФОПенко Іван", "ФОПенко Іван")]
    public void The_header_name_drops_a_leading_FOP(string sellerNameUk, string expected) =>
        Assert.Equal(expected, DpsXml.HeaderName(sellerNameUk));

    [Fact]
    public void Characters_windows_1251_cannot_encode_are_named_per_field()
    {
        var header = Header with { Name = "ФОП Іваненко 😀", Address = "Łódź, вул. Тестова 1" };

        var errors = DpsXml.Unwritable(header);

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

        var errors = DpsXml.Unwritable(header);

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
        var (figures, type, filledOn, _, _) = Cases["2026-q4"];

        var files = F0103309.Write(figures, header, type, filledOn);

        Assert.Empty(DpsXml.Unwritable(header));
        Assert.Contains("<HNAME>Мар'яна Іваненко</HNAME>", Windows1251.GetString(files.Declaration.Content));
        Assert.Contains("<HNAME>Мар'яна Іваненко</HNAME>", Windows1251.GetString(files.Annex!.Content));
        Assert.Empty(F0103309.SchemaErrors(files.Declaration.Content));
        Assert.Empty(F0133109.SchemaErrors(files.Annex.Content));
    }

    [Fact]
    public void A_tax_office_code_that_is_no_DPS_office_fails_both_schemas()
    {
        var (figures, type, filledOn, _, _) = Cases["2026-q4"];

        var files = F0103309.Write(figures, Header with { TaxOfficeRegion = 29, TaxOfficeDistrict = 0 }, type, filledOn);

        Assert.Contains(F0103309.SchemaErrors(files.Declaration.Content), error => error.Contains("C_STI_ORIG", StringComparison.Ordinal));
        Assert.Contains(F0133109.SchemaErrors(files.Annex!.Content), error => error.Contains("C_STI_ORIG", StringComparison.Ordinal));
    }

    [Fact]
    public void A_main_KVED_the_annex_schema_refuses_fails_it()
    {
        var (figures, type, filledOn, _, _) = Cases["2026-q4"];

        var files = F0103309.Write(figures, Header with { KvedCodes = ["6201"] }, type, filledOn);

        Assert.Contains(F0133109.SchemaErrors(files.Annex!.Content), error => error.Contains("HKVED", StringComparison.Ordinal));
    }

    [Fact]
    public void An_amount_without_two_decimals_fails_the_schema()
    {
        var tampered = DeclarationText("2026-q1").Replace("<R006G3>123456.78</R006G3>", "<R006G3>123456.7</R006G3>", StringComparison.Ordinal);
        var tamperedAnnex = AnnexText("2026-q4").Replace("<R09G4>22828.08</R09G4>", "<R09G4>22828.1</R09G4>", StringComparison.Ordinal);

        Assert.NotEmpty(F0103309.SchemaErrors(Windows1251.GetBytes(tampered)));
        Assert.NotEmpty(F0133109.SchemaErrors(Windows1251.GetBytes(tamperedAnnex)));
    }

    [Fact]
    public void Each_schema_refuses_the_other_forms_file()
    {
        var files = Write("2026-q4");

        Assert.NotEmpty(F0103309.SchemaErrors(files.Annex!.Content));
        Assert.NotEmpty(F0133109.SchemaErrors(files.Declaration.Content));
    }

    [Fact]
    public void Malformed_xml_is_an_error_rather_than_an_exception()
    {
        Assert.NotEmpty(F0103309.SchemaErrors(Encoding.ASCII.GetBytes("<DECLAR><DECLARHEAD>")));
        Assert.NotEmpty(F0133109.SchemaErrors(Encoding.ASCII.GetBytes("<DECLAR><DECLARHEAD>")));
    }

    private static EsvAnnex Annex(DateOnly from, int quarter, int firstMonth, bool leavesGroup3) => new(
        2026,
        quarter,
        from,
        new DateOnly(2026, 3 * quarter, 1).AddMonths(1).AddDays(-1),
        [.. Enumerable.Range(firstMonth, 3 * quarter - firstMonth + 1).Select(month => new EsvMonth(month, MinWageKop, EsvRateBp))],
        leavesGroup3);

    private static DeclarationXmlFiles Write(string name)
    {
        var (figures, type, filledOn, _, _) = Cases[name];
        return F0103309.Write(figures, Header, type, filledOn);
    }

    private static string DeclarationText(string name) => Windows1251.GetString(Write(name).Declaration.Content);

    private static string AnnexText(string name) => Windows1251.GetString(Write(name).Annex!.Content);

    private static void AssertEncoding(byte[] content, string form)
    {
        var prefix = "<?xml version=\"1.0\" encoding=\"windows-1251\"?>"
            + $"<DECLAR xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"{form}.xsd\">";
        Assert.Equal(Encoding.ASCII.GetBytes(prefix), content.Take(prefix.Length));
        Assert.DoesNotMatch(@">\s+<", Windows1251.GetString(content));
    }

    private static void AssertGolden(string name, byte[] content)
    {
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            var target = SourceGoldenPath(name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            return;
        }

        var golden = Path.Combine(AppContext.BaseDirectory, "Golden", name + ".xml");
        Assert.True(File.Exists(golden), $"{golden} is missing; run with UPDATE_GOLDEN=1 to write it.");
        Assert.Equal(File.ReadAllBytes(golden), content);
    }

    private static string SourceGoldenPath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", name + ".xml");

    // DGFilename from common_types.xsd; XSD patterns match the whole value.
    [GeneratedRegex(@"^([0-9]){4}(([0-9]{10})|(00[АБВГДЕЄЖЗИІКЛМНОПРСТУФХЦЧШЩЮЯ]{2}[0-9]{6}))([JF]((0[1-9])|([1-9][0-9])))(([0-9]{2}[1-9])|([0-9][1-9][0-9])|([1-9][0-9]{2}))(([1-9][0-9])|(0[1-9]))[1-3]([0-9]{2})([0-9]{7})([1-5])((0[1-9])|(1[0-2]))(20[0-9]{2})([0-9]){4}\.[xX][mM][lL]$")]
    private static partial Regex DgFilename();
}
