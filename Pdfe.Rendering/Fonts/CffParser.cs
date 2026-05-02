using CoreCffParser = Pdfe.Core.Fonts.CffParser;

namespace Pdfe.Rendering.Fonts;

/// <summary>
/// Compatibility aliases for CffParser, which has moved to Pdfe.Core.
/// The actual implementation is in Pdfe.Core/Fonts/CffParser.cs.
/// </summary>
internal static class CffParser
{
    // Alias the public types so existing code doesn't break
    public sealed class CffFontInfo
    {
        public int NumGlyphs;
        public short XMin, YMin, XMax, YMax;
        public Dictionary<string, int> GlyphNameToIndex = new();
        public string[] GlyphNames = Array.Empty<string>();
        public bool IsCidKeyed;
        public Dictionary<int, int>? CidToGlyph;

        // Conversion from core to rendering namespace
        internal static CffFontInfo From(CoreCffParser.CffFontInfo core)
        {
            return new CffFontInfo
            {
                NumGlyphs = core.NumGlyphs,
                XMin = core.XMin,
                YMin = core.YMin,
                XMax = core.XMax,
                YMax = core.YMax,
                GlyphNameToIndex = core.GlyphNameToIndex,
                GlyphNames = core.GlyphNames,
                IsCidKeyed = core.IsCidKeyed,
                CidToGlyph = core.CidToGlyph,
            };
        }
    }

    public static CffFontInfo? Parse(byte[] cff)
    {
        var info = CoreCffParser.Parse(cff);
        return info != null ? CffFontInfo.From(info) : null;
    }
}
