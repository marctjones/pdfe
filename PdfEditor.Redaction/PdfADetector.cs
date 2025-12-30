using System.Text.RegularExpressions;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Redaction;

/// <summary>
/// PDF/A conformance levels.
/// </summary>
public enum PdfALevel
{
    /// <summary>Not a PDF/A document.</summary>
    None,

    /// <summary>PDF/A-1a (ISO 19005-1 Level A conformance - accessible).</summary>
    PdfA_1a,

    /// <summary>PDF/A-1b (ISO 19005-1 Level B conformance - basic).</summary>
    PdfA_1b,

    /// <summary>PDF/A-2a (ISO 19005-2 Level A conformance - accessible).</summary>
    PdfA_2a,

    /// <summary>PDF/A-2b (ISO 19005-2 Level B conformance - basic).</summary>
    PdfA_2b,

    /// <summary>PDF/A-2u (ISO 19005-2 Level U conformance - Unicode).</summary>
    PdfA_2u,

    /// <summary>PDF/A-3a (ISO 19005-3 Level A conformance - accessible with attachments).</summary>
    PdfA_3a,

    /// <summary>PDF/A-3b (ISO 19005-3 Level B conformance - basic with attachments).</summary>
    PdfA_3b,

    /// <summary>PDF/A-3u (ISO 19005-3 Level U conformance - Unicode with attachments).</summary>
    PdfA_3u,

    /// <summary>PDF/A-4 (ISO 19005-4 base conformance).</summary>
    PdfA_4,

    /// <summary>PDF/A-4e (ISO 19005-4 engineering drawings).</summary>
    PdfA_4e,

    /// <summary>PDF/A-4f (ISO 19005-4 with embedded files).</summary>
    PdfA_4f
}

/// <summary>
/// Detects PDF/A conformance level from PDF documents.
/// </summary>
public static class PdfADetector
{
    // Element syntax: <pdfaid:part>1</pdfaid:part>
    private static readonly Regex XmpPartElementRegex = new(
        @"<pdfaid:part[^>]*>(\d+)</pdfaid:part>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XmpConformanceElementRegex = new(
        @"<pdfaid:conformance[^>]*>([A-Za-z]+)</pdfaid:conformance>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Attribute syntax: pdfaid:part="1" pdfaid:conformance="B"
    private static readonly Regex XmpPartAttributeRegex = new(
        @"pdfaid:part\s*=\s*[""'](\d+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XmpConformanceAttributeRegex = new(
        @"pdfaid:conformance\s*=\s*[""']([A-Za-z]+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Detect PDF/A level from a file path.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <returns>The detected PDF/A level, or None if not PDF/A.</returns>
    public static PdfALevel Detect(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            return PdfALevel.None;

        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return Detect(document);
        }
        catch
        {
            return PdfALevel.None;
        }
    }

    /// <summary>
    /// Detect PDF/A level from a PdfSharp document.
    /// </summary>
    /// <param name="document">The PDF document.</param>
    /// <returns>The detected PDF/A level, or None if not PDF/A.</returns>
    public static PdfALevel Detect(PdfDocument document)
    {
        try
        {
            // Get XMP metadata from the catalog
            var xmpMetadata = ExtractXmpMetadata(document);
            if (string.IsNullOrEmpty(xmpMetadata))
                return PdfALevel.None;

            return ParsePdfALevel(xmpMetadata);
        }
        catch
        {
            return PdfALevel.None;
        }
    }

    /// <summary>
    /// Detect PDF/A level from XMP metadata string.
    /// </summary>
    /// <param name="xmpMetadata">XMP metadata XML string.</param>
    /// <returns>The detected PDF/A level, or None if not PDF/A.</returns>
    public static PdfALevel ParseFromXmp(string xmpMetadata)
    {
        if (string.IsNullOrEmpty(xmpMetadata))
            return PdfALevel.None;

        return ParsePdfALevel(xmpMetadata);
    }

    private static string? ExtractXmpMetadata(PdfDocument document)
    {
        try
        {
            // Check for /Metadata in catalog
            var catalog = document.Internals.Catalog;
            if (catalog == null)
                return null;

            var metadataRef = catalog.Elements.GetObject("/Metadata");
            if (metadataRef == null)
                return null;

            // The metadata is a stream with XMP XML
            if (metadataRef is PdfDictionary metadataDict)
            {
                if (metadataDict.Stream?.Value != null)
                {
                    return System.Text.Encoding.UTF8.GetString(metadataDict.Stream.Value);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static PdfALevel ParsePdfALevel(string xmpMetadata)
    {
        // Look for PDF/A identification namespace
        if (!xmpMetadata.Contains("pdfaid", StringComparison.OrdinalIgnoreCase))
            return PdfALevel.None;

        // Extract part (1, 2, 3, or 4) - try element syntax first, then attribute syntax
        var partMatch = XmpPartElementRegex.Match(xmpMetadata);
        if (!partMatch.Success)
            partMatch = XmpPartAttributeRegex.Match(xmpMetadata);

        if (!partMatch.Success)
            return PdfALevel.None;

        if (!int.TryParse(partMatch.Groups[1].Value, out int part))
            return PdfALevel.None;

        // Extract conformance level (A, B, U, E, F) - try element syntax first, then attribute syntax
        var conformanceMatch = XmpConformanceElementRegex.Match(xmpMetadata);
        if (!conformanceMatch.Success)
            conformanceMatch = XmpConformanceAttributeRegex.Match(xmpMetadata);

        string conformance = conformanceMatch.Success
            ? conformanceMatch.Groups[1].Value.ToUpperInvariant()
            : "";

        // Map part + conformance to enum
        return (part, conformance) switch
        {
            (1, "A") => PdfALevel.PdfA_1a,
            (1, "B") => PdfALevel.PdfA_1b,
            (1, _) => PdfALevel.PdfA_1b, // Default to 1b if conformance not specified

            (2, "A") => PdfALevel.PdfA_2a,
            (2, "B") => PdfALevel.PdfA_2b,
            (2, "U") => PdfALevel.PdfA_2u,
            (2, _) => PdfALevel.PdfA_2b, // Default to 2b if conformance not specified

            (3, "A") => PdfALevel.PdfA_3a,
            (3, "B") => PdfALevel.PdfA_3b,
            (3, "U") => PdfALevel.PdfA_3u,
            (3, _) => PdfALevel.PdfA_3b, // Default to 3b if conformance not specified

            (4, "") => PdfALevel.PdfA_4,
            (4, "E") => PdfALevel.PdfA_4e,
            (4, "F") => PdfALevel.PdfA_4f,
            (4, _) => PdfALevel.PdfA_4, // Default to 4 if conformance not recognized

            _ => PdfALevel.None
        };
    }

    /// <summary>
    /// Get a human-readable name for a PDF/A level.
    /// </summary>
    public static string GetDisplayName(PdfALevel level) => level switch
    {
        PdfALevel.None => "Not PDF/A",
        PdfALevel.PdfA_1a => "PDF/A-1a",
        PdfALevel.PdfA_1b => "PDF/A-1b",
        PdfALevel.PdfA_2a => "PDF/A-2a",
        PdfALevel.PdfA_2b => "PDF/A-2b",
        PdfALevel.PdfA_2u => "PDF/A-2u",
        PdfALevel.PdfA_3a => "PDF/A-3a",
        PdfALevel.PdfA_3b => "PDF/A-3b",
        PdfALevel.PdfA_3u => "PDF/A-3u",
        PdfALevel.PdfA_4 => "PDF/A-4",
        PdfALevel.PdfA_4e => "PDF/A-4e",
        PdfALevel.PdfA_4f => "PDF/A-4f",
        _ => "Unknown"
    };

    /// <summary>
    /// Get the ISO standard number for a PDF/A level.
    /// </summary>
    public static string GetIsoStandard(PdfALevel level) => level switch
    {
        PdfALevel.PdfA_1a or PdfALevel.PdfA_1b => "ISO 19005-1",
        PdfALevel.PdfA_2a or PdfALevel.PdfA_2b or PdfALevel.PdfA_2u => "ISO 19005-2",
        PdfALevel.PdfA_3a or PdfALevel.PdfA_3b or PdfALevel.PdfA_3u => "ISO 19005-3",
        PdfALevel.PdfA_4 or PdfALevel.PdfA_4e or PdfALevel.PdfA_4f => "ISO 19005-4",
        _ => "N/A"
    };
}
