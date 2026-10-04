namespace TaxesUa.Api;

/// <summary>Bounds that several features check, so each reads one value instead of its own copy.</summary>
internal static class Limits
{
    /// <summary>The first and last year a dated row, a tax year or a rate lookup may name.</summary>
    public const int MinYear = 2000;

    public const int MaxYear = 2100;

    /// <summary>A client's or a payee's name, wherever it is stored.</summary>
    public const int MaxClientNameLength = 200;
}
