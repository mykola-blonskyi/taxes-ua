using PdfSharp.Fonts;

namespace TaxesUa.Api.Features.Export;

/// <summary>The embedded Noto Sans every PDF the app renders uses, so Cyrillic prints in each of them.</summary>
internal static class PdfFonts
{
    public const string Family = "Noto Sans";

    static PdfFonts()
    {
        // Process-wide and set once: the container has no system fonts, and PDFsharp's Core build
        // never reads them anyway.
        GlobalFontSettings.FontResolver = new NotoSansFontResolver();
    }

    /// <summary>Runs the static constructor; call before building a document.</summary>
    public static void Register()
    {
    }

    // Every family resolves to Noto Sans, so a style that names another font still renders Cyrillic.
    private sealed class NotoSansFontResolver : IFontResolver
    {
        private const string Regular = "NotoSans-Regular";
        private const string Bold = "NotoSans-Bold";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? Bold : Regular);

        public byte[] GetFont(string faceName)
        {
            using var resource = typeof(PdfFonts).Assembly
                .GetManifestResourceStream($"TaxesUa.Api.Fonts.{faceName}.ttf")!;
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
