using PdfSharp.Fonts;
using System;
using System.IO;

namespace PdfEditor.Redaction.Cli.Tests.TestHelpers;

/// <summary>
/// Minimal font resolver for tests - uses single fallback font.
/// </summary>
public class TestFontResolver : IFontResolver
{
    private static byte[]? _cachedFontData;

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
        if (_cachedFontData == null)
        {
            foreach (var path in FallbackPaths)
            {
                if (File.Exists(path))
                {
                    _cachedFontData = File.ReadAllBytes(path);
                    Console.WriteLine($"TestFontResolver: Using {path}");
                    break;
                }
            }

            if (_cachedFontData == null)
            {
                throw new Exception("No fallback fonts found!");
            }
        }
    }

    public byte[]? GetFont(string faceName) => _cachedFontData;

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
        => new FontResolverInfo("TestFont");
}
