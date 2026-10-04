using System.Reflection;
using System.Text.RegularExpressions;
using TaxesUa.Api.Data;

namespace TaxesUa.Api.Tests.Architecture;

// The API is one assembly, so `internal` keeps no feature from another. This test does: a feature reaches
// another only along an edge listed here, the listed edges form no cycle, and shared code reaches no
// feature. Adding an edge is a decision made here, in review. Edges are read from the compiled IL, plus a
// scan of the sources for const and enum values, which the compiler inlines as plain numbers.
public sealed class FeatureBoundaryTests
{
    private const string FeaturesPrefix = "TaxesUa.Api.Features.";

    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Audit"] = ["Auth", "Declarations", "Invoices", "Payments", "Settings", "TaxYears", "Transactions"],
        ["Auth"] = [],
        ["Backup"] = ["Audit", "Auth", "Banking", "Declarations", "Fx", "Invoices", "Monobank", "Notifications", "Payments", "Periods", "Settings", "TaxYears", "Transactions"],
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
        ["Payments"] = ["Auth", "Banking", "Settings"],
        ["Periods"] = ["Auth", "Payments", "Settings", "TaxYears", "Transactions"],
        ["Settings"] = ["Auth", "Banking", "Fx", "TaxYears"],
        ["TaxYears"] = [],
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
        const BindingFlags anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var owners = ApiTypes
            .Where(type => FeatureOf(type) is not null)
            .SelectMany(type => type.GetFields(anyStatic)
                .Where(field => field.IsLiteral)
                .Select(field => (Name: $"{type.Name}.{field.Name}", Feature: FeatureOf(type)!)))
            .ToLookup(literal => literal.Name, literal => literal.Feature);
        var listed = ListedEdges().ToHashSet(StringComparer.Ordinal);
        var features = Path.Combine(Above(Path.Combine("src", "TaxesUa.Api", "Program.cs")), "src", "TaxesUa.Api", "Features");

        var unlisted = Directory.EnumerateFiles(features, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                var from = Regex.Match(source, @"^namespace TaxesUa\.Api\.Features\.(\w+)", RegexOptions.Multiline).Groups[1].Value;
                var code = Regex.Replace(source, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

                return Regex.Matches(code, @"\b(\w+)\.(\w+)\b")
                    .SelectMany(read => owners[read.Value]
                        .Where(to => to != from && !owners[read.Value].Contains(from) && !listed.Contains($"{from} -> {to}"))
                        .Select(to => $"{from} -> {to}: {read.Value} in {Path.GetFileName(path)}"));
            })
            .Distinct()
            .ToArray();

        Assert.True(unlisted.Length == 0, string.Join(Environment.NewLine, unlisted));
    }

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

    // TaxesUa.Api and TaxesUa.Api.Data hold what every feature may use (Problems, TextRules, Limits, Incident,
    // OwnerLock). A feature reached from there would let one feature reach another unseen. AppDbContext
    // maps every feature's entities, so it is the exception.
    [Fact]
    public void Shared_code_reaches_no_feature()
    {
        var offenders = ApiTypes
            .Where(type => type.Namespace is "TaxesUa.Api" or "TaxesUa.Api.Data")
            .Where(type => type != typeof(AppDbContext) && type.DeclaringType != typeof(AppDbContext))
            .SelectMany(FeatureReferences)
            .Select(reference => $"{reference.From.FullName} uses {reference.To.FullName}")
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
    }
}
