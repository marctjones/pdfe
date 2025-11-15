using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Helper methods for PDF testing
/// </summary>
public static class PdfTestHelpers
{
    /// <summary>
    /// Extracts all text from a PDF using PdfPig
    /// </summary>
    public static string ExtractAllText(string pdfPath)
    {
        var text = new StringBuilder();

        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            foreach (var page in document.GetPages())
            {
                text.AppendLine(page.Text);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Extracts text from a specific page
    /// </summary>
    public static string ExtractTextFromPage(string pdfPath, int pageIndex)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        
        if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
        {
            return string.Empty;
        }
        
        var page = document.GetPage(pageIndex + 1); // PdfPig uses 1-based indexing
        return page.Text;
    }

    /// <summary>
    /// Gets the number of pages in a PDF
    /// </summary>
    public static int GetPageCount(string pdfPath)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>
    /// Checks if a PDF contains specific text
    /// </summary>
    public static bool PdfContainsText(string pdfPath, string searchText)
    {
        var allText = ExtractAllText(pdfPath);
        return allText.Contains(searchText);
    }

    /// <summary>
    /// Gets all individual words from a PDF page
    /// </summary>
    public static List<string> GetWordsFromPage(string pdfPath, int pageIndex)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageIndex + 1);

        return page.GetWords()
            .Select(w => w.Text)
            .ToList();
    }

    /// <summary>
    /// Counts occurrences of a specific word in the PDF
    /// </summary>
    public static int CountWordOccurrences(string pdfPath, string word)
    {
        var allText = ExtractAllText(pdfPath);
        var words = allText.Split(new[] { ' ', '\n', '\r', '\t' }, 
            StringSplitOptions.RemoveEmptyEntries);
        
        return words.Count(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the file size in bytes
    /// </summary>
    public static long GetFileSize(string pdfPath)
    {
        var fileInfo = new System.IO.FileInfo(pdfPath);
        return fileInfo.Length;
    }

    /// <summary>
    /// Validates that a PDF is readable and not corrupted
    /// </summary>
    public static bool IsValidPdf(string pdfPath)
    {
        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return document.PageCount > 0;
        }
        catch
        {
            return false;
        }
    }
}
