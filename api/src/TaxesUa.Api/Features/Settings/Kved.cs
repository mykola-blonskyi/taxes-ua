namespace TaxesUa.Api.Features.Settings;

/// <summary>
/// The classes of КВЕД ДК 009:2010 and their official names as Держстат publishes them, so the
/// declaration's KVED table carries the name beside the code. <c>KvedClasses.g.cs</c> holds the table,
/// built by <c>api/tools/build-kved.mjs</c>.
/// </summary>
internal static partial class Kved
{
    public static int Count => Names.Count;

    /// <summary>Every class in code order.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Classes => Names.OrderBy(pair => pair.Key, StringComparer.Ordinal);

    public static string? Name(string code) => Names.GetValueOrDefault(code);
}
