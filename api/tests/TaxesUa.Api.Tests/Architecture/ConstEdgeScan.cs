using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TaxesUa.Api.Tests.Architecture;

// Finds the const and enum values a source file reads, as `Type.Member` names. The compiler inlines them, so the
// compiled IL shows no edge. Reading the syntax tree, not the text, leaves comments and string literals out,
// follows a chain split over lines, and sees every adjacent pair of a chain, so `A.Type.Member` yields `Type.Member`.
// It is syntax only, with no symbol binding: a `using X = ...` alias is resolved by name, and `using static T;`
// matches a bare identifier by name, so a local that shares a name with a member of T is a false positive.
internal static class ConstEdgeScan
{
    private const string FeaturesPrefix = "TaxesUa.Api.Features.";

    // The feature whose namespace the file declares, or "" when it declares none.
    public static string FeatureOf(SyntaxNode root) =>
        root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .Select(declaration => declaration.Name.ToString())
            .Where(name => name.StartsWith(FeaturesPrefix, StringComparison.Ordinal))
            .Select(name => name[FeaturesPrefix.Length..].Split('.')[0])
            .FirstOrDefault() ?? string.Empty;

    // `literals` holds every `Type.Member` that is a const or an enum member somewhere in the API.
    public static IEnumerable<string> Reads(SyntaxNode root, IReadOnlySet<string> literals)
    {
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Where(directive => directive.Name is not null).ToArray();
        var aliases = usings
            .Where(directive => directive.Alias is not null)
            .GroupBy(directive => directive.Alias!.Name.Identifier.ValueText)
            .ToDictionary(group => group.Key, group => LastSegment(group.First().Name!));
        var staticTypes = usings
            .Where(directive => directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
            .Select(directive => LastSegment(directive.Name!))
            .ToHashSet(StringComparer.Ordinal);

        // Every chain node is visited, so each adjacent pair of a longer chain is read, not only the last two.
        var chained = root.DescendantNodes()
            .SelectMany(node => node switch
            {
                MemberAccessExpressionSyntax access => new[] { (Left: (SyntaxNode)access.Expression, Right: access.Name) },
                QualifiedNameSyntax qualified => [(Left: (SyntaxNode)qualified.Left, Right: (SimpleNameSyntax)qualified.Right)],
                _ => [],
            })
            .Select(pair => (Type: LastSegment(pair.Left), Member: pair.Right.Identifier.ValueText, Head: pair.Left is SimpleNameSyntax))
            .Where(pair => pair.Type is not null)
            .Select(pair => pair.Head && aliases.TryGetValue(pair.Type!, out var target) ? $"{target}.{pair.Member}" : $"{pair.Type}.{pair.Member}");

        var bare = staticTypes.Count == 0
            ? []
            : root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(IsBareUse)
                .SelectMany(name => staticTypes.Select(type => $"{type}.{name.Identifier.ValueText}"));

        return chained.Concat(bare).Where(literals.Contains).Distinct();
    }

    // An identifier that is neither the right side of a chain nor inside a using directive.
    private static bool IsBareUse(IdentifierNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name != name,
            QualifiedNameSyntax qualified => qualified.Left == name,
            MemberBindingExpressionSyntax => false,
            _ => true,
        }
        && !name.Ancestors().OfType<UsingDirectiveSyntax>().Any();

    // The rightmost simple name of a dotted name or member chain: `A.B.C` gives C, `C<T>` gives C.
    private static string? LastSegment(SyntaxNode node) => node switch
    {
        SimpleNameSyntax name => name.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        _ => null,
    };
}
