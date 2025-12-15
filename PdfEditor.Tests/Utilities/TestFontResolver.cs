using PdfSharp.Fonts;
using System;
using System.IO;

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Minimal font resolver for tests - uses single fallback font.
/// Dramatically faster than CustomFontResolver which scans 2,967 system fonts.
/// </summary>
public class TestFontResolver : IFontResolver
{
    private static byte[]? _cachedFontData;
    private static string? _fontPath;
    
    private static readonly string[] FallbackPaths = new[]
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "C:\\Windows\\Fonts\\arial.ttf"
    };

    public TestFontResolver()
    {
        // Load font ONCE using static field (shared across all test instances)
        if (_cachedFontData == null)
        {
            foreach (var path in FallbackPaths)
            {
                if (File.Exists(path))
                {
                    _cachedFontData = File.ReadAllBytes(path);
                    _fontPath = path;
                    Console.WriteLine($"TestFontResolver: Using {path} ({_cachedFontData.Length / 1024}KB)");
                    break;
                }
            }

            if (_cachedFontData == null)
            {
                throw new Exception("No fallback fonts found! Checked: " + string.Join(", ", FallbackPaths));
            }
        }
    }

    public byte[]? GetFont(string faceName)
    {
        // Always return the same font, regardless of what's requested
        // This is fine for tests - we just need a valid font, not the exact one
        return _cachedFontData;
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Always return the same typeface
        // PdfSharp will use the same font data for all requests
        return new FontResolverInfo("TestFont");
    }
}
