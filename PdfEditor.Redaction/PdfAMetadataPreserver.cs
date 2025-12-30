using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfEditor.Redaction;

/// <summary>
/// Preserves PDF/A identification and synchronizes metadata dates during redaction.
/// Addresses veraPDF clauses 6.7.11 (PDF/A Identification) and 6.7.3 (ModDate match).
/// </summary>
public static class PdfAMetadataPreserver
{
    private static readonly Regex XmpPartRegex = new(
        @"<pdfaid:part[^>]*>(\d+)</pdfaid:part>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XmpConformanceRegex = new(
        @"<pdfaid:conformance[^>]*>([A-Za-z]+)</pdfaid:conformance>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XmpRevRegex = new(
        @"<pdfaid:rev[^>]*>(\d{4})</pdfaid:rev>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex XmpModifyDateRegex = new(
        @"<xmp:ModifyDate[^>]*>([^<]+)</xmp:ModifyDate>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Preserve PDF/A metadata when saving a redacted document.
    ///
    /// IMPORTANT: PDFsharp overwrites XMP metadata on save. For true PDF/A preservation,
    /// use PreserveMetadataInFile() AFTER saving the document.
    ///
    /// This method sets up the document's Info dictionary dates, which PDFsharp does preserve.
    /// </summary>
    /// <param name="document">The PDF document being saved.</param>
    /// <param name="originalPdfALevel">The PDF/A level detected from the original document.</param>
    public static void PreserveMetadata(PdfDocument document, PdfALevel originalPdfALevel)
    {
        if (originalPdfALevel == PdfALevel.None)
            return;

        try
        {
            // Get or create XMP metadata
            var xmpMetadata = ExtractXmpMetadata(document);

            if (string.IsNullOrEmpty(xmpMetadata))
            {
                // Create new XMP metadata with PDF/A identification
                xmpMetadata = CreatePdfAXmpMetadata(originalPdfALevel);
            }
            else
            {
                // Update existing XMP with correct PDF/A identification
                xmpMetadata = UpdatePdfAIdentification(xmpMetadata, originalPdfALevel);
            }

            // Synchronize modification dates
            var modDate = DateTime.Now;
            xmpMetadata = UpdateModifyDate(xmpMetadata, modDate);
            document.Info.ModificationDate = modDate;

            // Write the updated XMP metadata back
            SetXmpMetadata(document, xmpMetadata);
        }
        catch
        {
            // If metadata preservation fails, don't break the redaction
            // The document will still be valid, just potentially not PDF/A compliant
        }
    }

    /// <summary>
    /// Extract XMP metadata string from a PDF document.
    /// </summary>
    public static string? ExtractXmpMetadata(PdfDocument document)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            if (catalog == null)
                return null;

            var metadataRef = catalog.Elements.GetObject("/Metadata");
            if (metadataRef == null)
                return null;

            if (metadataRef is PdfDictionary metadataDict)
            {
                if (metadataDict.Stream?.Value != null)
                {
                    return Encoding.UTF8.GetString(metadataDict.Stream.Value);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create new XMP metadata with PDF/A identification.
    /// </summary>
    private static string CreatePdfAXmpMetadata(PdfALevel level)
    {
        var (part, conformance, rev) = GetPdfAComponents(level);
        var modDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz").Replace("+", "+").Replace("-", "-");

        // Build minimal XMP packet with PDF/A identification
        var xmp = new StringBuilder();
        xmp.AppendLine("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        xmp.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        xmp.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        xmp.AppendLine("    <rdf:Description rdf:about=\"\"");
        xmp.AppendLine("        xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"");
        xmp.AppendLine("        xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\">");
        xmp.AppendLine($"      <pdfaid:part>{part}</pdfaid:part>");
        if (!string.IsNullOrEmpty(conformance))
        {
            xmp.AppendLine($"      <pdfaid:conformance>{conformance}</pdfaid:conformance>");
        }
        if (!string.IsNullOrEmpty(rev))
        {
            xmp.AppendLine($"      <pdfaid:rev>{rev}</pdfaid:rev>");
        }
        xmp.AppendLine($"      <xmp:ModifyDate>{modDate}</xmp:ModifyDate>");
        xmp.AppendLine("    </rdf:Description>");
        xmp.AppendLine("  </rdf:RDF>");
        xmp.AppendLine("</x:xmpmeta>");
        xmp.AppendLine("<?xpacket end=\"w\"?>");

        return xmp.ToString();
    }

    /// <summary>
    /// Update existing XMP with correct PDF/A identification.
    /// </summary>
    private static string UpdatePdfAIdentification(string xmpMetadata, PdfALevel level)
    {
        var (part, conformance, rev) = GetPdfAComponents(level);
        var result = xmpMetadata;

        // Update or add pdfaid:part
        if (XmpPartRegex.IsMatch(result))
        {
            result = XmpPartRegex.Replace(result, $"<pdfaid:part>{part}</pdfaid:part>");
        }
        else if (result.Contains("pdfaid:"))
        {
            // Try to insert after existing pdfaid namespace
            var insertPoint = result.IndexOf("</rdf:Description>", StringComparison.OrdinalIgnoreCase);
            if (insertPoint > 0)
            {
                result = result.Insert(insertPoint, $"\n      <pdfaid:part>{part}</pdfaid:part>");
            }
        }

        // Update or add pdfaid:conformance (only for levels that need it)
        if (!string.IsNullOrEmpty(conformance))
        {
            if (XmpConformanceRegex.IsMatch(result))
            {
                result = XmpConformanceRegex.Replace(result, $"<pdfaid:conformance>{conformance}</pdfaid:conformance>");
            }
            else
            {
                var insertPoint = result.IndexOf("</pdfaid:part>", StringComparison.OrdinalIgnoreCase);
                if (insertPoint > 0)
                {
                    insertPoint = result.IndexOf(">", insertPoint) + 1;
                    result = result.Insert(insertPoint, $"\n      <pdfaid:conformance>{conformance}</pdfaid:conformance>");
                }
            }
        }

        // Update or add pdfaid:rev (only for PDF/A-4)
        if (!string.IsNullOrEmpty(rev))
        {
            if (XmpRevRegex.IsMatch(result))
            {
                result = XmpRevRegex.Replace(result, $"<pdfaid:rev>{rev}</pdfaid:rev>");
            }
            else
            {
                var insertPoint = result.IndexOf("</pdfaid:conformance>", StringComparison.OrdinalIgnoreCase);
                if (insertPoint < 0)
                    insertPoint = result.IndexOf("</pdfaid:part>", StringComparison.OrdinalIgnoreCase);
                if (insertPoint > 0)
                {
                    insertPoint = result.IndexOf(">", insertPoint) + 1;
                    result = result.Insert(insertPoint, $"\n      <pdfaid:rev>{rev}</pdfaid:rev>");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Update the xmp:ModifyDate in XMP metadata.
    /// </summary>
    private static string UpdateModifyDate(string xmpMetadata, DateTime modDate)
    {
        var xmpDateStr = modDate.ToString("yyyy-MM-ddTHH:mm:sszzz");

        if (XmpModifyDateRegex.IsMatch(xmpMetadata))
        {
            return XmpModifyDateRegex.Replace(xmpMetadata, $"<xmp:ModifyDate>{xmpDateStr}</xmp:ModifyDate>");
        }
        else
        {
            // Try to add ModifyDate if it doesn't exist
            var insertPoint = xmpMetadata.IndexOf("</rdf:Description>", StringComparison.OrdinalIgnoreCase);
            if (insertPoint > 0)
            {
                // Check if xmp namespace is declared
                if (!xmpMetadata.Contains("xmlns:xmp="))
                {
                    // Add namespace declaration
                    var descStart = xmpMetadata.IndexOf("<rdf:Description", StringComparison.OrdinalIgnoreCase);
                    if (descStart > 0)
                    {
                        var insertNs = xmpMetadata.IndexOf(">", descStart);
                        if (insertNs > 0 && xmpMetadata[insertNs - 1] != '/')
                        {
                            xmpMetadata = xmpMetadata.Insert(insertNs, "\n        xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
                            // Recalculate insertPoint after modification
                            insertPoint = xmpMetadata.IndexOf("</rdf:Description>", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                return xmpMetadata.Insert(insertPoint, $"      <xmp:ModifyDate>{xmpDateStr}</xmp:ModifyDate>\n    ");
            }
        }
        return xmpMetadata;
    }

    /// <summary>
    /// Set XMP metadata on a PDF document.
    /// </summary>
    private static void SetXmpMetadata(PdfDocument document, string xmpMetadata)
    {
        try
        {
            var xmpBytes = Encoding.UTF8.GetBytes(xmpMetadata);

            // Get or create metadata dictionary
            var catalog = document.Internals.Catalog;
            var metadataRef = catalog.Elements.GetObject("/Metadata");

            PdfDictionary metadataDict;

            if (metadataRef is PdfDictionary existingDict)
            {
                // Update existing stream - remove old and create new
                metadataDict = existingDict;
                // Clear the existing stream by creating a new one with new data
            }
            else
            {
                // Create new metadata stream
                metadataDict = new PdfDictionary(document);
                metadataDict.Elements.SetName("/Type", "/Metadata");
                metadataDict.Elements.SetName("/Subtype", "/XML");
                document.Internals.AddObject(metadataDict);
                catalog.Elements.SetReference("/Metadata", metadataDict);
            }

            // Create/replace the stream with XMP data
            // PDFsharp's CreateStream replaces any existing stream data
            metadataDict.CreateStream(xmpBytes);

            // Ensure the stream is correctly set (workaround for in-memory updates)
            if (metadataDict.Stream != null)
            {
                // Force the stream value to be the new bytes
                metadataDict.Stream.Value = xmpBytes;
            }
        }
        catch
        {
            // Metadata update failed, but don't break the save
        }
    }

    /// <summary>
    /// Get PDF/A components (part, conformance, rev) from level enum.
    /// </summary>
    private static (int part, string conformance, string? rev) GetPdfAComponents(PdfALevel level) => level switch
    {
        PdfALevel.PdfA_1a => (1, "A", null),
        PdfALevel.PdfA_1b => (1, "B", null),
        PdfALevel.PdfA_2a => (2, "A", null),
        PdfALevel.PdfA_2b => (2, "B", null),
        PdfALevel.PdfA_2u => (2, "U", null),
        PdfALevel.PdfA_3a => (3, "A", null),
        PdfALevel.PdfA_3b => (3, "B", null),
        PdfALevel.PdfA_3u => (3, "U", null),
        PdfALevel.PdfA_4 => (4, "", "2020"),
        PdfALevel.PdfA_4e => (4, "E", "2020"),
        PdfALevel.PdfA_4f => (4, "F", "2020"),
        _ => (1, "B", null) // Default to 1b if unknown
    };

    /// <summary>
    /// Synchronize Info dictionary dates with XMP metadata dates.
    /// PDF/A requires these to match exactly.
    /// </summary>
    public static void SynchronizeDates(PdfDocument document)
    {
        try
        {
            var now = DateTime.Now;

            // Set Info dictionary dates
            document.Info.ModificationDate = now;

            // Update XMP metadata
            var xmpMetadata = ExtractXmpMetadata(document);
            if (!string.IsNullOrEmpty(xmpMetadata))
            {
                xmpMetadata = UpdateModifyDate(xmpMetadata, now);
                SetXmpMetadata(document, xmpMetadata);
            }
        }
        catch
        {
            // Date synchronization failed, but don't break the operation
        }
    }

    /// <summary>
    /// Inject PDF/A identification into a saved PDF file.
    ///
    /// This is the recommended method for preserving PDF/A metadata because PDFsharp
    /// overwrites XMP on save. Call this AFTER saving the document with PDFsharp.
    ///
    /// IMPLEMENTATION NOTE: PDFsharp regenerates XMP on every save. To work around this,
    /// we read the file, modify the XMP stream bytes directly, and write back using
    /// PDFsharp's internal API to avoid triggering XMP regeneration.
    /// </summary>
    /// <param name="pdfPath">Path to the saved PDF file.</param>
    /// <param name="pdfALevel">The PDF/A level to inject.</param>
    /// <returns>True if injection succeeded, false otherwise.</returns>
    public static bool PreserveMetadataInFile(string pdfPath, PdfALevel pdfALevel)
    {
        if (pdfALevel == PdfALevel.None || !File.Exists(pdfPath))
            return false;

        try
        {
            // Read the file content
            var fileBytes = File.ReadAllBytes(pdfPath);
            var content = Encoding.Latin1.GetString(fileBytes);

            // PDFsharp creates a NEW XMP stream but may leave the old one intact.
            // We need to find the LAST XMP packet (which is PDFsharp's), as that's
            // the one referenced by the catalog /Metadata entry.
            var xpacketStartIdx = content.LastIndexOf("<?xpacket begin", StringComparison.Ordinal);
            var xpacketEndIdx = content.LastIndexOf("<?xpacket end=\"w\"?>", StringComparison.Ordinal);

            if (xpacketStartIdx < 0 || xpacketEndIdx < 0)
            {
                // No XMP packet found, can't inject
                return false;
            }

            // Ensure we have the right pair (start should come before end)
            if (xpacketStartIdx > xpacketEndIdx)
            {
                // Something's wrong with the XMP structure
                return false;
            }

            xpacketEndIdx += "<?xpacket end=\"w\"?>".Length;

            // Extract the original XMP
            var originalXmp = content.Substring(xpacketStartIdx, xpacketEndIdx - xpacketStartIdx);

            // Inject PDF/A identification AND fix the dates
            var modifiedXmp = InjectPdfAIdentification(originalXmp, pdfALevel);

            // Fix the modification date (PDFsharp sets it to epoch if Info.ModificationDate wasn't set properly)
            modifiedXmp = FixModifyDate(modifiedXmp);

            // Ensure the modified XMP is the same length (pad with spaces before xpacket end)
            // This is required because PDF streams have fixed lengths
            modifiedXmp = AdjustXmpLength(modifiedXmp, originalXmp.Length);

            if (modifiedXmp == null)
            {
                // XMP is too long even after compression
                return false;
            }

            // Replace the XMP in the file content
            var modifiedContent = content.Substring(0, xpacketStartIdx) +
                                  modifiedXmp +
                                  content.Substring(xpacketEndIdx);

            // Write the modified content back
            File.WriteAllBytes(pdfPath, Encoding.Latin1.GetBytes(modifiedContent));

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fix the xmp:ModifyDate and xmp:CreateDate if they're set to epoch (0001-01-01).
    /// </summary>
    private static string FixModifyDate(string xmpMetadata)
    {
        // Check for epoch dates that PDFsharp inserts when dates aren't properly set
        if (xmpMetadata.Contains("0001-01-01") || xmpMetadata.Contains("0001-01-02"))
        {
            var now = DateTime.Now;
            var xmpDateStr = now.ToString("yyyy-MM-ddTHH:mm:sszzz");

            // Fix ModifyDate
            xmpMetadata = Regex.Replace(
                xmpMetadata,
                @"<xmp:ModifyDate>[^<]+</xmp:ModifyDate>",
                $"<xmp:ModifyDate>{xmpDateStr}</xmp:ModifyDate>");

            // Fix CreateDate too if it's epoch
            xmpMetadata = Regex.Replace(
                xmpMetadata,
                @"<xmp:CreateDate>0001-01[^<]+</xmp:CreateDate>",
                $"<xmp:CreateDate>{xmpDateStr}</xmp:CreateDate>");
        }

        return xmpMetadata;
    }

    /// <summary>
    /// Adjust XMP length to match the original stream length by padding or compressing.
    /// </summary>
    private static string? AdjustXmpLength(string modifiedXmp, int targetLength)
    {
        if (modifiedXmp.Length == targetLength)
            return modifiedXmp;

        if (modifiedXmp.Length < targetLength)
        {
            // Add padding spaces before the closing xpacket
            var endTag = "<?xpacket end=\"w\"?>";
            var endTagIdx = modifiedXmp.LastIndexOf(endTag, StringComparison.Ordinal);
            if (endTagIdx > 0)
            {
                var padding = new string(' ', targetLength - modifiedXmp.Length);
                return modifiedXmp.Insert(endTagIdx, padding);
            }
        }
        else
        {
            // XMP is too long - try to compress by removing unnecessary whitespace
            var compressed = modifiedXmp;
            compressed = Regex.Replace(compressed, @"\n\s+<rdf:Description", "\n<rdf:Description");
            compressed = Regex.Replace(compressed, @"\n\s+<pdfaid:", "\n<pdfaid:");
            compressed = Regex.Replace(compressed, @"\n\s+</rdf:Description>", "\n</rdf:Description>");
            compressed = Regex.Replace(compressed, @"\n\s+</rdf:RDF>", "\n</rdf:RDF>");
            compressed = Regex.Replace(compressed, @"\n\s+</x:xmpmeta>", "\n</x:xmpmeta>");

            // Remove any existing padding whitespace before xpacket end
            compressed = Regex.Replace(compressed, @"\s+<\?xpacket end", "<?xpacket end");

            if (compressed.Length <= targetLength)
            {
                // Now pad to exact length
                var endTag = "<?xpacket end=\"w\"?>";
                var endTagIdx = compressed.LastIndexOf(endTag, StringComparison.Ordinal);
                if (endTagIdx > 0)
                {
                    var padding = new string(' ', targetLength - compressed.Length);
                    return compressed.Insert(endTagIdx, padding);
                }
            }

            // Still too long
            return null;
        }

        return modifiedXmp;
    }

    /// <summary>
    /// Inject PDF/A identification into existing XMP metadata.
    /// Adds pdfaid namespace and elements if not present.
    /// </summary>
    private static string InjectPdfAIdentification(string xmpMetadata, PdfALevel level)
    {
        var (part, conformance, rev) = GetPdfAComponents(level);

        // Check if pdfaid namespace is already declared
        if (!xmpMetadata.Contains("xmlns:pdfaid"))
        {
            // Find the first rdf:Description and add pdfaid namespace
            var descMatch = Regex.Match(xmpMetadata, @"<rdf:Description[^>]*>", RegexOptions.IgnoreCase);
            if (descMatch.Success)
            {
                var insertPos = descMatch.Index + descMatch.Length - 1; // Before the '>'
                xmpMetadata = xmpMetadata.Insert(insertPos, " xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"");
            }
        }

        // Now add/update PDF/A identification elements
        // Look for an existing pdfaid:part or add a new Description block
        if (XmpPartRegex.IsMatch(xmpMetadata))
        {
            xmpMetadata = XmpPartRegex.Replace(xmpMetadata, $"<pdfaid:part>{part}</pdfaid:part>");
        }
        else
        {
            // Add PDF/A identification in a new Description block before </rdf:RDF>
            var pdfaBlock = BuildPdfADescriptionBlock(part, conformance, rev);
            var rdfClosePos = xmpMetadata.LastIndexOf("</rdf:RDF>", StringComparison.OrdinalIgnoreCase);
            if (rdfClosePos > 0)
            {
                xmpMetadata = xmpMetadata.Insert(rdfClosePos, pdfaBlock);
            }
        }

        // Update conformance if present
        if (!string.IsNullOrEmpty(conformance))
        {
            if (XmpConformanceRegex.IsMatch(xmpMetadata))
            {
                xmpMetadata = XmpConformanceRegex.Replace(xmpMetadata, $"<pdfaid:conformance>{conformance}</pdfaid:conformance>");
            }
        }

        // Update rev if present (PDF/A-4)
        if (!string.IsNullOrEmpty(rev))
        {
            if (XmpRevRegex.IsMatch(xmpMetadata))
            {
                xmpMetadata = XmpRevRegex.Replace(xmpMetadata, $"<pdfaid:rev>{rev}</pdfaid:rev>");
            }
        }

        return xmpMetadata;
    }

    /// <summary>
    /// Build a new rdf:Description block for PDF/A identification.
    /// </summary>
    private static string BuildPdfADescriptionBlock(int part, string conformance, string? rev)
    {
        var sb = new StringBuilder();
        sb.AppendLine("      <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">");
        sb.AppendLine($"        <pdfaid:part>{part}</pdfaid:part>");
        if (!string.IsNullOrEmpty(conformance))
        {
            sb.AppendLine($"        <pdfaid:conformance>{conformance}</pdfaid:conformance>");
        }
        if (!string.IsNullOrEmpty(rev))
        {
            sb.AppendLine($"        <pdfaid:rev>{rev}</pdfaid:rev>");
        }
        sb.AppendLine("      </rdf:Description>");
        return sb.ToString();
    }
}
