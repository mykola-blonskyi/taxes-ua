using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Architecture;

// The API is one assembly, so `internal` keeps no feature from another. This test does: a feature reaches
// another only along an edge listed here, the listed edges form no cycle, and shared code reaches no
// feature. Shared code is every file of the API outside Features/. Adding an edge is a decision made here,
// in review. Edges are read from the compiled IL, plus a scan of the sources (ConstEdgeScan) for const and
// enum values, which the compiler inlines as plain numbers.
public sealed class FeatureBoundaryTests
{
    private const string FeaturesPrefix = "TaxesUa.Api.Features.";

    private const string ApiNamespace = "TaxesUa.Api";

    // Program wires every feature, and EF Core needs one DbContext that maps every feature's entities.
    private static readonly string[] SharedExemptFiles = ["Program.cs", Path.Combine("Data", "AppDbContext.cs")];

    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Audit"] = ["Auth", "Declarations", "Invoices", "Notifications", "Payments", "Settings", "TaxYears", "Transactions"],
        ["Auth"] = [],
        ["Backup"] = ["Audit", "Auth", "Banking", "Declarations", "Fx", "Invoices", "Monobank", "Notifications", "Payments", "Settings", "Transactions"],
        ["Banking"] = ["Fx"],
        ["Calendar"] = ["Auth", "Notifications", "Periods", "Settings", "TaxYears"],
        ["Clients"] = ["Auth", "Fx", "Invoices", "Transactions"],
        ["Dashboard"] = ["Auth", "Declarations", "Invoices", "Monobank", "Payments", "Periods", "Settings", "TaxYears", "Transactions"],
        ["DatabaseBackups"] = [],
        ["Declarations"] = ["Auth", "Payments", "Periods", "Settings", "TaxYears", "Transactions"],
        ["Export"] = ["Auth", "Fx", "Settings", "Transactions"],
        ["Fx"] = [],
        ["Invoices"] = ["Auth", "Export", "Fx", "Settings", "Transactions"],
        ["Monobank"] = ["Auth", "Banking", "Fx", "Payments", "Settings", "Transactions"],
        ["Notifications"] = ["Auth", "Declarations", "Export", "Periods", "Settings"],
        ["Payments"] = ["Auth", "Banking", "Settings", "TaxYears"],
        ["Periods"] = ["Auth", "Payments", "Settings", "TaxYears", "Transactions"],
        ["Settings"] = ["Auth", "Banking", "Fx", "TaxYears"],
        ["TaxYears"] = ["Auth"],
        ["Transactions"] = ["Auth", "Banking", "Fx", "Settings", "TaxYears"],
    };

    private static readonly Type[] ApiTypes = typeof(AppDbContext).Assembly.GetTypes();

    private static string? FeatureOf(Type type) =>
        type.Namespace is { } name && name.StartsWith(FeaturesPrefix, StringComparison.Ordinal)
            ? name[FeaturesPrefix.Length..].Split('.')[0]
            : null;

    private static IEnumerable<(Type From, Type To)> FeatureReferences(Type type) =>
        CompiledReferences.Of(type)
            .SelectMany(CompiledReferences.Types)
            .Where(target => FeatureOf(target) is not null)
            .Distinct()
            .Select(target => (type, target));

    private static Dictionary<string, (Type From, Type To)> MeasuredEdges()
    {
        var edges = new Dictionary<string, (Type, Type)>(StringComparer.Ordinal);
        foreach (var (from, to) in ApiTypes.Where(type => FeatureOf(type) is not null).SelectMany(FeatureReferences))
        {
            if (FeatureOf(from) != FeatureOf(to))
            {
                edges.TryAdd($"{FeatureOf(from)} -> {FeatureOf(to)}", (from, to));
            }
        }

        return edges;
    }

    private static IEnumerable<string> ListedEdges() =>
        Allowed.SelectMany(pair => pair.Value.Select(to => $"{pair.Key} -> {to}"));

    [Fact]
    public void Features_reach_each_other_only_along_the_listed_edges()
    {
        var listed = ListedEdges().ToHashSet(StringComparer.Ordinal);
        var unlisted = MeasuredEdges()
            .Where(edge => !listed.Contains(edge.Key))
            .OrderBy(edge => edge.Key, StringComparer.Ordinal)
            .Select(edge => $"{edge.Key}: {edge.Value.From.FullName} uses {edge.Value.To.FullName}")
            .ToArray();

        Assert.True(unlisted.Length == 0, string.Join(Environment.NewLine, unlisted));
    }

    [Fact]
    public void Const_and_enum_reads_follow_the_listed_edges()
    {
        var listed = ListedEdges().ToHashSet(StringComparer.Ordinal);
        var unlisted = ConstReads()
            .Where(read => read.From.Length > 0 && !listed.Contains($"{read.From} -> {read.To}"))
            .Select(read => $"{read.From} -> {read.To}: {read.Literal} in {read.File}")
            .Distinct()
            .ToArray();

        Assert.True(unlisted.Length == 0, string.Join(Environment.NewLine, unlisted));
    }

    // Each const or enum value a source file reads from a feature other than its own. From is the file's
    // feature by its folder, or "" for a shared file.
    private static IEnumerable<(string From, string To, string Literal, string File)> ConstReads()
    {
        const BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var owners = ApiTypes
            .Where(type => FeatureOf(type) is not null)
            .SelectMany(type => type.GetFields(anyStatic)
                .Where(field => field.IsLiteral)
                .Select(field => (Name: $"{type.Name}.{field.Name}", Feature: FeatureOf(type)!)))
            .ToLookup(literal => literal.Name, literal => literal.Feature);
        var literals = owners.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);

        return SourceFiles().SelectMany(file =>
        {
            var from = FolderFeature(file);

            return ConstEdgeScan.Reads(Parse(file), literals)
                .SelectMany(read => owners[read]
                    .Where(to => to != from && !owners[read].Contains(from))
                    .Select(to => (from, to, read, file)));
        });
    }

    // The checks attribute a type by its namespace and a source file by its folder, so the two must agree.
    [Fact]
    public void Every_source_file_declares_the_namespace_of_its_folder()
    {
        var misplaced = SourceFiles()
            .Where(file => ConstEdgeScan.FeatureOf(Parse(file)) != FolderFeature(file))
            .ToArray();

        Assert.True(misplaced.Length == 0, string.Join(Environment.NewLine, misplaced));
    }

    private static readonly string ApiSources =
        Path.Combine(Above(Path.Combine("src", "TaxesUa.Api", "Program.cs")), "src", "TaxesUa.Api");

    // Every source file of the API, relative to src/TaxesUa.Api.
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ApiSources, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(ApiSources, path))
            .Where(file => file.Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj"))
            .Order(StringComparer.Ordinal);

    private static SyntaxNode Parse(string file) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ApiSources, file))).GetRoot();

    private static string FolderFeature(string file) =>
        file.Split(Path.DirectorySeparatorChar) is ["Features", var feature, _, ..] ? feature : string.Empty;

    [Fact]
    public void Every_listed_edge_is_still_used()
    {
        var unused = ListedEdges().Except(MeasuredEdges().Keys, StringComparer.Ordinal).ToArray();

        Assert.True(unused.Length == 0, string.Join(Environment.NewLine, unused));
    }

    [Fact]
    public void Every_feature_folder_is_listed()
    {
        var features = ApiTypes.Select(FeatureOf).OfType<string>().Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(features, Allowed.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_listed_edges_form_no_cycle()
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        string? CycleFrom(string feature)
        {
            if (path.Contains(feature))
            {
                return string.Join(" -> ", path.SkipWhile(step => step != feature).Append(feature));
            }

            if (!done.Add(feature))
            {
                return null;
            }

            path.Add(feature);
            var cycle = Allowed[feature].Select(CycleFrom).FirstOrDefault(found => found is not null);
            path.RemoveAt(path.Count - 1);

            return cycle;
        }

        Assert.Null(Allowed.Keys.Select(CycleFrom).FirstOrDefault(found => found is not null));
    }

    [Fact]
    public void The_dependency_graph_document_shows_the_listed_edges()
    {
        string[] rows =
        [
            "| Feature | Layer | Reaches |",
            "| --- | --- | --- |",
            .. Allowed.Keys
                .OrderBy(feature => Layer(feature, []))
                .ThenBy(feature => feature, StringComparer.Ordinal)
                .Select(feature => $"| {feature} | {Layer(feature, [])} | {(Allowed[feature] is [] ? "nothing" : string.Join(", ", Allowed[feature]))} |"),
        ];
        var table = string.Join('\n', rows);
        var document = File.ReadAllText(Path.Combine(Above(Path.Combine("graph", "dependencies.md")), "graph", "dependencies.md"))
            .ReplaceLineEndings("\n");

        Assert.True(document.Contains(table, StringComparison.Ordinal), $"Replace the table in graph/dependencies.md with:\n{table}");
    }

    private static int Layer(string feature, HashSet<string> seen) =>
        seen.Add(feature) ? Allowed[feature].Select(to => Layer(to, [.. seen])).DefaultIfEmpty(-1).Max() + 1 : 0;

    // The directory above the test binaries that holds `relative`: the repository root or api/.
    private static string Above(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, relative)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{relative} is not above the test binaries.");
    }

    // Shared code holds what every feature may use (Problems, TextRules, Limits, Incident, OwnerLock). A
    // feature reached from there would let one feature reach another unseen.
    [Fact]
    public void Shared_code_reaches_no_feature()
    {
        var compiled = ApiTypes
            .Where(IsShared)
            .SelectMany(FeatureReferences)
            .Select(reference => $"{reference.From.FullName} uses {reference.To.FullName}");
        var inlined = ConstReads()
            .Where(read => read.From.Length == 0 && !SharedExemptFiles.Contains(read.File))
            .Select(read => $"{Path.GetFileNameWithoutExtension(read.File)} -> {read.To}: {read.Literal} in {read.File}");
        var offenders = compiled.Concat(inlined).Distinct().ToArray();

        Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
    }

    // Any type under the API's namespace outside the features; Every_source_file_declares_the_namespace_of_its_folder
    // keeps that the same as being outside Features/. Program is in the global namespace.
    private static bool IsShared(Type type) =>
        FeatureOf(type) is null
        && type.Namespace is { } name
        && (name == ApiNamespace || name.StartsWith(ApiNamespace + ".", StringComparison.Ordinal))
        && type != typeof(AppDbContext) && type.DeclaringType != typeof(AppDbContext);
}
