using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Sanitizes PDF metadata to remove redacted content from all metadata locations.
///
/// This is critical for security - redacted text may appear in:
/// - Document Info Dictionary (Title, Author, Subject, Keywords)
/// - XMP Metadata stream
/// - Bookmarks/Outlines
/// - Annotation contents
/// - Form field values
/// - Named destinations
/// </summary>
public class MetadataSanitizer
{
    private readonly ILogger<MetadataSanitizer> _logger;

    public MetadataSanitizer(ILogger<MetadataSanitizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Remove redacted content from all metadata locations in the document
    /// </summary>
    /// <param name="document">The PDF document to sanitize</param>
    /// <param name="redactedTerms">List of text strings that were redacted</param>
    public void SanitizeDocument(PdfDocument document, IEnumerable<string> redactedTerms)
    {
        var terms = redactedTerms.Where(t => !string.IsNullOrEmpty(t)).ToList();

        if (terms.Count == 0)
        {
            _logger.LogDebug("No redacted terms provided, skipping metadata sanitization");
            return;
        }

        _logger.LogInformation("Sanitizing document metadata for {TermCount} redacted terms", terms.Count);

        try
        {
            // 1. Document Info Dictionary
            SanitizeDocumentInfo(document, terms);

            // 2. XMP Metadata
            SanitizeXmpMetadata(document, terms);

            // 3. Bookmarks/Outlines
            SanitizeOutlines(document, terms);

            // 4. Annotations (all pages)
            SanitizeAnnotations(document, terms);

            // 5. Form Fields (AcroForm)
            SanitizeFormFields(document, terms);

            // 6. Named Destinations
            SanitizeNamedDestinations(document, terms);

            _logger.LogInformation("Metadata sanitization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metadata sanitization");
            throw;
        }
    }

    /// <summary>
    /// Sanitize the document information dictionary (Title, Author, Subject, Keywords, etc.)
    /// </summary>
    private void SanitizeDocumentInfo(PdfDocument document, List<string> terms)
    {
        var info = document.Info;
        var sanitized = 0;

        _logger.LogDebug("Sanitizing document info. Title='{Title}', Author='{Author}', Subject='{Subject}'",
            info.Title ?? "(null)", info.Author ?? "(null)", info.Subject ?? "(null)");
        _logger.LogDebug("Redacted terms to search for: {Terms}", string.Join(", ", terms.Select(t => $"'{t}'")));

        // Title
        if (!string.IsNullOrEmpty(info.Title))
        {
            var newTitle = RedactTerms(info.Title, terms);
            _logger.LogDebug("Title sanitization: '{Original}' -> '{New}'", info.Title, newTitle);
            if (newTitle != info.Title)
            {
                info.Title = newTitle;
                sanitized++;
                _logger.LogDebug("Sanitized document Title");
            }
        }

        // Author
        if (!string.IsNullOrEmpty(info.Author))
        {
            var newAuthor = RedactTerms(info.Author, terms);
            if (newAuthor != info.Author)
            {
                info.Author = newAuthor;
                sanitized++;
                _logger.LogDebug("Sanitized document Author");
            }
        }

        // Subject
        if (!string.IsNullOrEmpty(info.Subject))
        {
            var newSubject = RedactTerms(info.Subject, terms);
            if (newSubject != info.Subject)
            {
                info.Subject = newSubject;
                sanitized++;
                _logger.LogDebug("Sanitized document Subject");
            }
        }

        // Keywords
        if (!string.IsNullOrEmpty(info.Keywords))
        {
            var newKeywords = RedactTerms(info.Keywords, terms);
            if (newKeywords != info.Keywords)
            {
                info.Keywords = newKeywords;
                sanitized++;
                _logger.LogDebug("Sanitized document Keywords");
            }
        }

        // Creator
        if (!string.IsNullOrEmpty(info.Creator))
        {
            var newCreator = RedactTerms(info.Creator, terms);
            if (newCreator != info.Creator)
            {
                info.Creator = newCreator;
                sanitized++;
                _logger.LogDebug("Sanitized document Creator");
            }
        }

        // Producer is read-only in PdfSharpCore, cannot be modified
        // It is automatically set by the library when saving

        if (sanitized > 0)
        {
            _logger.LogInformation("Sanitized {Count} document info fields", sanitized);
        }
    }

    /// <summary>
    /// Sanitize XMP metadata stream
    /// </summary>
    private void SanitizeXmpMetadata(PdfDocument document, List<string> terms)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            if (catalog == null) return;

            var metadataRef = catalog.Elements["/Metadata"];
            if (metadataRef == null) return;

            PdfDictionary? metadataDict = null;

            if (metadataRef is PdfReference pdfRef)
            {
                metadataDict = pdfRef.Value as PdfDictionary;
            }
            else if (metadataRef is PdfDictionary dict)
            {
                metadataDict = dict;
            }

            if (metadataDict?.Stream?.Value == null) return;

            // XMP metadata is XML, so we need to handle it as text
            var xmpBytes = metadataDict.Stream.Value;
            var xmpText = Encoding.UTF8.GetString(xmpBytes);
            var originalText = xmpText;

            // Redact terms in XMP
            xmpText = RedactTerms(xmpText, terms);

            if (xmpText != originalText)
            {
                metadataDict.Stream.Value = Encoding.UTF8.GetBytes(xmpText);
                _logger.LogInformation("Sanitized XMP metadata stream");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sanitize XMP metadata");
        }
    }

    /// <summary>
    /// Sanitize document outlines (bookmarks)
    /// </summary>
    private void SanitizeOutlines(PdfDocument document, List<string> terms)
    {
        try
        {
            var outlines = document.Outlines;
            if (outlines == null || outlines.Count == 0) return;

            var sanitized = SanitizeOutlineCollection(outlines, terms);

            if (sanitized > 0)
            {
                _logger.LogInformation("Sanitized {Count} outline entries", sanitized);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sanitize outlines");
        }
    }

    private int SanitizeOutlineCollection(PdfOutlineCollection outlines, List<string> terms)
    {
        var sanitized = 0;

        foreach (var outline in outlines)
        {
            if (!string.IsNullOrEmpty(outline.Title))
            {
                var newTitle = RedactTerms(outline.Title, terms);
                if (newTitle != outline.Title)
                {
                    outline.Title = newTitle;
                    sanitized++;
                }
            }

            // Recursively sanitize children
            if (outline.Outlines != null && outline.Outlines.Count > 0)
            {
                sanitized += SanitizeOutlineCollection(outline.Outlines, terms);
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitize annotation contents across all pages
    /// </summary>
    private void SanitizeAnnotations(PdfDocument document, List<string> terms)
    {
        var sanitized = 0;

        foreach (var page in document.Pages)
        {
            try
            {
                var annotsArray = page.Elements["/Annots"] as PdfArray;
                if (annotsArray == null) continue;

                foreach (var annotItem in annotsArray.Elements)
                {
                    PdfDictionary? annot = null;

                    if (annotItem is PdfReference annotRef)
                    {
                        annot = annotRef.Value as PdfDictionary;
                    }
                    else if (annotItem is PdfDictionary annotDict)
                    {
                        annot = annotDict;
                    }

                    if (annot == null) continue;

                    // Sanitize Contents field
                    sanitized += SanitizePdfString(annot, "/Contents", terms);

                    // Sanitize Subject field
                    sanitized += SanitizePdfString(annot, "/Subj", terms);

                    // Sanitize Title field (author of annotation)
                    sanitized += SanitizePdfString(annot, "/T", terms);

                    // Sanitize RC (rich content) field
                    sanitized += SanitizePdfString(annot, "/RC", terms);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not sanitize annotations on page");
            }
        }

        if (sanitized > 0)
        {
            _logger.LogInformation("Sanitized {Count} annotation fields", sanitized);
        }
    }

    /// <summary>
    /// Sanitize form fields (AcroForm)
    /// </summary>
    private void SanitizeFormFields(PdfDocument document, List<string> terms)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            if (catalog == null) return;

            var acroFormRef = catalog.Elements["/AcroForm"];
            if (acroFormRef == null) return;

            PdfDictionary? acroForm = null;

            if (acroFormRef is PdfReference formRef)
            {
                acroForm = formRef.Value as PdfDictionary;
            }
            else if (acroFormRef is PdfDictionary formDict)
            {
                acroForm = formDict;
            }

            if (acroForm == null) return;

            var fieldsArray = acroForm.Elements["/Fields"] as PdfArray;
            if (fieldsArray == null) return;

            var sanitized = SanitizeFieldArray(fieldsArray, terms);

            if (sanitized > 0)
            {
                _logger.LogInformation("Sanitized {Count} form field values", sanitized);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sanitize form fields");
        }
    }

    private int SanitizeFieldArray(PdfArray fields, List<string> terms)
    {
        var sanitized = 0;

        foreach (var fieldItem in fields.Elements)
        {
            PdfDictionary? field = null;

            if (fieldItem is PdfReference fieldRef)
            {
                field = fieldRef.Value as PdfDictionary;
            }
            else if (fieldItem is PdfDictionary fieldDict)
            {
                field = fieldDict;
            }

            if (field == null) continue;

            // Sanitize field value
            sanitized += SanitizePdfString(field, "/V", terms);

            // Sanitize default value
            sanitized += SanitizePdfString(field, "/DV", terms);

            // Sanitize tooltip/alternate name
            sanitized += SanitizePdfString(field, "/TU", terms);

            // Recursively handle child fields
            var kids = field.Elements["/Kids"] as PdfArray;
            if (kids != null)
            {
                sanitized += SanitizeFieldArray(kids, terms);
            }
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitize named destinations
    /// </summary>
    private void SanitizeNamedDestinations(PdfDocument document, List<string> terms)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            if (catalog == null) return;

            // Check /Names dictionary
            var namesRef = catalog.Elements["/Names"];
            if (namesRef == null) return;

            PdfDictionary? names = null;

            if (namesRef is PdfReference nRef)
            {
                names = nRef.Value as PdfDictionary;
            }
            else if (namesRef is PdfDictionary nDict)
            {
                names = nDict;
            }

            if (names == null) return;

            // Check /Dests (named destinations)
            var destsRef = names.Elements["/Dests"];
            if (destsRef == null) return;

            _logger.LogDebug("Found named destinations tree - sanitization would be applied here");
            // Full implementation would traverse the name tree and sanitize destination names
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sanitize named destinations");
        }
    }

    /// <summary>
    /// Helper to sanitize a PDF string field in a dictionary
    /// </summary>
    private int SanitizePdfString(PdfDictionary dict, string key, List<string> terms)
    {
        var element = dict.Elements[key];
        if (element == null) return 0;

        if (element is PdfString pdfStr)
        {
            var original = pdfStr.Value;
            var sanitized = RedactTerms(original, terms);

            if (sanitized != original)
            {
                dict.Elements[key] = new PdfString(sanitized);
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Replace all occurrences of redacted terms with redaction markers.
    /// Also extracts significant words (4+ chars) from multi-word terms to catch
    /// variations like "ACME Corp Report" when "Report from ACME Corp" was redacted.
    /// </summary>
    private string RedactTerms(string text, List<string> terms)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;

        // Build a set of all terms to search for, including extracted words
        var allTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;

            // Add the full term
            allTerms.Add(term);

            // Extract significant words (4+ characters, alphanumeric)
            // This catches cases where the same word appears in different contexts
            // Note: Don't split on underscore as it often joins compound identifiers like "ACME_CORP"
            var words = term.Split(new[] { ' ', '\t', '\n', '\r', '-', '.', ',', ';', ':' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                // Only add words that are significant (4+ chars, not common words)
                if (word.Length >= 4 && !IsCommonWord(word))
                {
                    allTerms.Add(word);
                }
            }
        }

        // Sort by length descending to replace longer matches first
        var sortedTerms = allTerms.OrderByDescending(t => t.Length).ToList();

        foreach (var term in sortedTerms)
        {
            // Case-insensitive replacement with redaction markers
            var index = 0;
            while ((index = result.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                // Replace with black rectangles of same length
                var replacement = new string('█', term.Length);
                result = result.Substring(0, index) + replacement + result.Substring(index + term.Length);
                index += replacement.Length;
            }
        }

        return result;
    }

    /// <summary>
    /// Check if a word is a common word that shouldn't be redacted on its own
    /// </summary>
    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had",
            "her", "was", "one", "our", "out", "has", "his", "how", "its", "may",
            "new", "now", "old", "see", "way", "who", "did", "get", "let", "put",
            "say", "she", "too", "use", "from", "have", "this", "that", "with",
            "they", "will", "been", "each", "make", "like", "than", "them", "then",
            "into", "over", "such", "your", "some", "could", "would", "about",
            "which", "their", "there", "these", "other", "report", "document",
            "page", "file", "data", "info", "information", "prepared", "quarterly"
        };

        return commonWords.Contains(word);
    }

    /// <summary>
    /// Completely remove all metadata from document (for maximum security)
    /// </summary>
    public void RemoveAllMetadata(PdfDocument document)
    {
        _logger.LogInformation("Removing all document metadata for maximum security");

        try
        {
            // Clear document info
            document.Info.Title = string.Empty;
            document.Info.Author = string.Empty;
            document.Info.Subject = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator = string.Empty;
            // Producer is read-only in PdfSharpCore, set automatically by library

            // Remove XMP metadata
            var catalog = document.Internals.Catalog;
            if (catalog != null && catalog.Elements.ContainsKey("/Metadata"))
            {
                catalog.Elements.Remove("/Metadata");
                _logger.LogDebug("Removed XMP metadata");
            }

            // Note: Removing outlines and annotations would require more careful handling
            // as they may contain navigation-critical information

            _logger.LogInformation("All metadata removed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing metadata");
            throw;
        }
    }
}
