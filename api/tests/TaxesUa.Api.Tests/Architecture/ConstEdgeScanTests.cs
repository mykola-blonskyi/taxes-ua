using Microsoft.CodeAnalysis.CSharp;

namespace TaxesUa.Api.Tests.Architecture;

// Probes for the const and enum scan. Each source is a string, never compiled, so a probe may name any type.
public sealed class ConstEdgeScanTests
{
    private static readonly IReadOnlySet<string> Literals = new HashSet<string>(StringComparer.Ordinal)
    {
        "SyncHealthState.Stale",
        "Limits.Max",
    };

    private static string[] ReadsOf(string source) =>
        [.. ConstEdgeScan.Reads(CSharpSyntaxTree.ParseText(source).GetRoot(), Literals)];

    private static string[] ReadsInClass(string usings, string member) =>
        ReadsOf($$"""
            {{usings}}
            namespace TaxesUa.Api.Features.Probe;
            public static class Probe { public static object Run() => {{member}}; }
            """);

    [Fact]
    public void A_plain_read_is_found() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("", "SyncHealthState.Stale"));

    [Fact]
    public void A_using_alias_is_resolved() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("using S = TaxesUa.Api.Features.Monobank.SyncHealthState;", "S.Stale"));

    [Fact]
    public void A_partly_qualified_chain_is_found() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("", "Monobank.SyncHealthState.Stale"));

    [Fact]
    public void A_fully_qualified_chain_is_found() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("", "global::TaxesUa.Api.Features.Monobank.SyncHealthState.Stale"));

    [Fact]
    public void A_chain_split_across_whitespace_and_lines_is_found() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("", "Monobank\n    .SyncHealthState  \n .  Stale"));

    [Fact]
    public void A_using_static_bare_member_is_found() =>
        Assert.Equal(["SyncHealthState.Stale"], ReadsInClass("using static TaxesUa.Api.Features.Monobank.SyncHealthState;", "Stale"));

    [Fact]
    public void A_using_static_does_not_match_the_right_side_of_another_chain() =>
        Assert.Empty(ReadsInClass("using static TaxesUa.Api.Features.Monobank.SyncHealthState;", "other.Stale"));

    [Fact]
    public void A_trailing_comment_is_ignored() =>
        Assert.Empty(ReadsInClass("", "1 // SyncHealthState.Stale"));

    [Fact]
    public void A_block_comment_is_ignored() =>
        Assert.Empty(ReadsInClass("", "1 /* Monobank.SyncHealthState.Stale\n   Limits.Max */"));

    [Fact]
    public void A_doc_comment_is_ignored() =>
        Assert.Empty(ReadsOf("""
            namespace TaxesUa.Api.Features.Probe;
            /// <summary>Limits.Max</summary>
            public sealed class Probe;
            """));

    [Theory]
    [InlineData("\"SyncHealthState.Stale\"")]
    [InlineData("@\"Monobank.SyncHealthState.Stale\"")]
    [InlineData("\"\"\"Limits.Max\"\"\"")]
    [InlineData("'.'")]
    public void A_string_or_char_literal_is_ignored(string literal) =>
        Assert.Empty(ReadsInClass("", literal));

    [Fact]
    public void A_read_inside_an_interpolated_string_is_found() =>
        Assert.Equal(["Limits.Max"], ReadsInClass("", "$\"max {Limits.Max}\""));

    [Fact]
    public void A_namespace_is_read_from_the_syntax_not_the_text()
    {
        var root = CSharpSyntaxTree.ParseText("""
            // namespace TaxesUa.Api.Features.Fake;
            namespace TaxesUa.Api.Features.Probe.Nested { }
            """).GetRoot();

        Assert.Equal("Probe", ConstEdgeScan.FeatureOf(root));
    }
}
