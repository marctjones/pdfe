using PdfSharp.Fonts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Cross-platform font resolver for PDFsharp.
/// Locates system fonts on Windows, Linux, and macOS.
/// </summary>
public class CustomFontResolver : IFontResolver
{
    private readonly Dictionary<string, string> _fontCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _searchedDirectories = new();

    public CustomFontResolver()
    {
        // Lazy initialization - do not populate cache immediately
    }

    public byte[]? GetFont(string faceName)
    {
        try
        {
            // Try to find the font file
            if (_fontCache.TryGetValue(faceName, out var fontPath))
            {
                if (File.Exists(fontPath))
                {
                    return File.ReadAllBytes(fontPath);
                }
            }

            // If not found in cache, try to search again
            var foundPath = FindFontFile(faceName);
            if (foundPath != null)
            {
                _fontCache[faceName] = foundPath;
                return File.ReadAllBytes(foundPath);
            }

            // Return null if font not found (PdfSharp will use fallback)
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading font '{faceName}': {ex.Message}");
            return null;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        // Normalize family name
        var normalizedFamily = NormalizeFontFamily(familyName);

        // Build font face name based on style
        var faceName = BuildFaceName(normalizedFamily, bold, italic);

        // Check if we have this font
        if (_fontCache.ContainsKey(faceName) || FindFontFile(faceName) != null)
        {
            return new FontResolverInfo(faceName);
        }

        // Try without style modifiers
        if (_fontCache.ContainsKey(normalizedFamily) || FindFontFile(normalizedFamily) != null)
        {
            return new FontResolverInfo(normalizedFamily);
        }

        // Fallback to a guaranteed font
        var fallbackFont = GetFallbackFont();
        return new FontResolverInfo(fallbackFont);
    }

    private void PopulateFontCache()
    {
        // Optimization: Don't scan everything. Just look for specific fonts if needed.
        // Or rely on FindFontFile which searches on demand.
    }

    private string[] GetSystemFontDirectories()
    {
        var directories = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            directories.Add("/usr/share/fonts");
            directories.Add("/usr/local/share/fonts");

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
            {
                directories.Add(Path.Combine(homeDir, ".local", "share", "fonts"));
                directories.Add(Path.Combine(homeDir, ".fonts"));
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            directories.Add("/System/Library/Fonts");
            directories.Add("/Library/Fonts");

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
            {
                directories.Add(Path.Combine(homeDir, "Library", "Fonts"));
            }
        }

        return directories.Where(Directory.Exists).ToArray();
    }

    private string NormalizeFontFamily(string familyName)
    {
        // Map common Windows fonts to Linux equivalents
        var fontMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arial", GetLinuxFontAlternative("Arial", "LiberationSans-Regular", "DejaVuSans") },
            { "Times New Roman", GetLinuxFontAlternative("Times New Roman", "LiberationSerif-Regular", "DejaVuSerif") },
            { "Courier New", GetLinuxFontAlternative("Courier New", "LiberationMono-Regular", "DejaVuSansMono") },
            { "Helvetica", GetLinuxFontAlternative("Helvetica", "LiberationSans-Regular", "DejaVuSans") },
            { "Times", GetLinuxFontAlternative("Times", "LiberationSerif-Regular", "DejaVuSerif") },
            { "Courier", GetLinuxFontAlternative("Courier", "LiberationMono-Regular", "DejaVuSansMono") }
        };

        return fontMapping.TryGetValue(familyName, out var mapped) ? mapped : familyName;
    }

    private string GetLinuxFontAlternative(string preferred, params string[] alternatives)
    {
        // On Windows/Mac, use the preferred font
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return preferred;

        // On Linux, check if preferred exists, otherwise use alternatives
        if (_fontCache.ContainsKey(preferred))
            return preferred;

        foreach (var alt in alternatives)
        {
            if (_fontCache.ContainsKey(alt))
                return alt;
        }

        return alternatives.FirstOrDefault() ?? preferred;
    }

    private string BuildFaceName(string familyName, bool bold, bool italic)
    {
        if (!bold && !italic)
            return familyName;

        var suffix = (bold, italic) switch
        {
            (true, true) => "-BoldItalic",
            (true, false) => "-Bold",
            (false, true) => "-Italic",
            _ => ""
        };

        return familyName + suffix;
    }

    private string? FindFontFile(string faceName)
    {
        // Optimization: Check for specific known font first
        if (File.Exists("/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf"))
        {
             return "/usr/share/fonts/opentype/ipafont-gothic/ipagp.ttf";
        }

        // Search through all font directories for a matching font
        var fontDirectories = GetSystemFontDirectories();

        foreach (var directory in fontDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            try
            {
                // Try exact match
                var exactMatch = Directory.GetFiles(directory, $"{faceName}.ttf", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(directory, $"{faceName}.otf", SearchOption.AllDirectories))
                    .FirstOrDefault();

                if (exactMatch != null)
                    return exactMatch;

                // Optimization: Don't do the expensive case-insensitive search over all files
                // unless absolutely necessary.
                // For tests, we usually just need ANY font.
            }
            catch
            {
                // Skip directories we can't access
            }
        }

        return null;
    }

    private string SimplifyFontName(string fontFileName)
    {
        // Remove common suffixes
        var suffixes = new[] { "-Regular", "-Bold", "-Italic", "-BoldItalic", "Regular", "Bold", "Italic", "BoldItalic" };

        var simplified = fontFileName;
        foreach (var suffix in suffixes)
        {
            if (simplified.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                simplified = simplified.Substring(0, simplified.Length - suffix.Length);
                break;
            }
        }

        return simplified;
    }

    private string GetFallbackFont()
    {
        // Try to find a guaranteed fallback font based on platform
        string[] fallbackCandidates;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fallbackCandidates = new[] { "Arial", "Segoe UI", "Tahoma", "Verdana" };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            fallbackCandidates = new[] { "LiberationSans-Regular", "DejaVuSans", "FreeSans", "Nimbus Sans" };
        }
        else // macOS
        {
            fallbackCandidates = new[] { "Helvetica", "Arial", "Lucida Grande" };
        }

        foreach (var candidate in fallbackCandidates)
        {
            if (_fontCache.ContainsKey(candidate))
                return candidate;
        }

        // Last resort: return first available font
        return _fontCache.Keys.FirstOrDefault() ?? "Arial";
    }
}
