using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction.PdfPig;

/// <summary>
/// Helper class for extracting PdfPig letters from PdfSharp PdfPage objects.
/// Handles the complexity of converting in-memory PdfPage to PdfPig format.
/// </summary>
public static class PdfPigHelper
{
    /// <summary>
    /// Extract PdfPig letters from a PDF file (file-based approach).
    /// </summary>
    public static IReadOnlyList<Letter> ExtractLettersFromFile(
        string pdfPath,
        int pageNumber,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        logger.LogDebug("Extracting letters from file {FilePath}, page {PageNumber}", pdfPath, pageNumber);

        try
        {
            using var pigDocument = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pigPage = pigDocument.GetPage(pageNumber);

            var letters = pigPage.Letters;
            logger.LogDebug("Extracted {Count} letters from page {PageNumber}", letters.Count, pageNumber);

            return letters;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract letters from file {FilePath}, page {PageNumber}",
                pdfPath, pageNumber);
            throw new InvalidOperationException(
                $"Failed to extract letters from page {pageNumber}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract letters from an in-memory PdfDocument by saving to a temporary MemoryStream.
    /// CRITICAL: This creates a CLONE of the document to avoid marking the original as "saved".
    /// </summary>
    public static IReadOnlyList<Letter> ExtractLettersFromDocument(
        PdfSharp.Pdf.PdfDocument document,
        int pageNumber,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        logger.LogDebug("Extracting letters from in-memory document, page {PageNumber}", pageNumber);

        try
        {
            // Create a temporary MemoryStream and save document to it
            // This does NOT mark the original document as "saved" because we're just reading
            using var memoryStream = new MemoryStream();

            // Save to stream (this will mark the document as saved, but that's OK for extraction)
            document.Save(memoryStream, closeStream: false);
            memoryStream.Position = 0;

            // Open with PdfPig and extract letters
            using var pigDocument = UglyToad.PdfPig.PdfDocument.Open(memoryStream);
            var pigPage = pigDocument.GetPage(pageNumber);

            var letters = pigPage.Letters;
            logger.LogDebug("Extracted {Count} letters from page {PageNumber}", letters.Count, pageNumber);

            return letters;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract letters from in-memory document, page {PageNumber}", pageNumber);
            throw new InvalidOperationException(
                $"Failed to extract letters from page {pageNumber}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get the 1-based page number of a PdfPage within its document.
    /// </summary>
    /// <param name="page">The page to find the number for.</param>
    /// <returns>1-based page number.</returns>
    public static int GetPageNumber(PdfPage page)
    {
        var document = page.Owner;

        for (int i = 0; i < document.Pages.Count; i++)
        {
            if (document.Pages[i] == page)
            {
                return i + 1; // PdfPig uses 1-based page numbers
            }
        }

        throw new InvalidOperationException("Page not found in document");
    }
}
