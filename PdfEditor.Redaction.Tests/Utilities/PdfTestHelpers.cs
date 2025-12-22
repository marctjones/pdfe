using System.Text;
using UglyToad.PdfPig;

namespace PdfEditor.Redaction.Tests.Utilities;

/// <summary>
/// Helper methods for PDF testing and verification.
/// </summary>
public static class PdfTestHelpers
{
    /// <summary>
    /// Extract all text from a PDF file.
    /// </summary>
    public static string ExtractAllText(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract text from a specific page.
    /// </summary>
    public static string ExtractPageText(string pdfPath, int pageNumber)
    {
        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageNumber);
        return page.Text;
    }

    /// <summary>
    /// Check if a PDF contains specific text.
    /// </summary>
    public static bool ContainsText(string pdfPath, string searchText, bool caseSensitive = true)
    {
        var text = ExtractAllText(pdfPath);
        return caseSensitive
            ? text.Contains(searchText)
            : text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Count occurrences of text in a PDF.
    /// </summary>
    public static int CountTextOccurrences(string pdfPath, string searchText, bool caseSensitive = true)
    {
        var text = ExtractAllText(pdfPath);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(searchText, index, comparison)) >= 0)
        {
            count++;
            index += searchText.Length;
        }

        return count;
    }

    /// <summary>
    /// Get page count of a PDF.
    /// </summary>
    public static int GetPageCount(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return document.NumberOfPages;
    }

    /// <summary>
    /// Get page dimensions in points.
    /// </summary>
    public static (double Width, double Height) GetPageSize(string pdfPath, int pageNumber = 1)
    {
        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageNumber);
        return (page.Width, page.Height);
    }

    /// <summary>
    /// Verify PDF is valid and can be opened.
    /// </summary>
    public static bool IsValidPdf(string pdfPath)
    {
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            return document.NumberOfPages > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get letter positions from a PDF page for debugging.
    /// </summary>
    public static IReadOnlyList<(string Character, double Left, double Bottom, double Right, double Top)> GetLetterPositions(
        string pdfPath, int pageNumber = 1)
    {
        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageNumber);

        return page.Letters
            .Select(l => (
                l.Value,
                l.GlyphRectangle.Left,
                l.GlyphRectangle.Bottom,
                l.GlyphRectangle.Right,
                l.GlyphRectangle.Top))
            .ToList();
    }
}
