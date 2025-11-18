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

    /// <summary>
    /// Gets detailed text content with position information
    /// </summary>
    public static List<(string text, double x, double y)> GetTextWithPositions(string pdfPath, int pageIndex = 0)
    {
        var result = new List<(string, double, double)>();

        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
            {
                return result;
            }

            var page = document.GetPage(pageIndex + 1);
            var words = page.GetWords();

            foreach (var word in words)
            {
                result.Add((word.Text, word.BoundingBox.Left, word.BoundingBox.Bottom));
            }
        }

        return result;
    }

    /// <summary>
    /// Verifies that specific text items exist in the PDF
    /// </summary>
    public static bool ContainsAllText(string pdfPath, params string[] requiredTexts)
    {
        var allText = ExtractAllText(pdfPath);
        return requiredTexts.All(text => allText.Contains(text));
    }

    /// <summary>
    /// Verifies that specific text items do NOT exist in the PDF
    /// </summary>
    public static bool ContainsNoneOfText(string pdfPath, params string[] forbiddenTexts)
    {
        var allText = ExtractAllText(pdfPath);
        return forbiddenTexts.All(text => !allText.Contains(text));
    }

    /// <summary>
    /// Gets a list of all unique words in the PDF
    /// </summary>
    public static List<string> GetAllUniqueWords(string pdfPath)
    {
        var allText = ExtractAllText(pdfPath);
        var words = allText.Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ':', ';' },
            StringSplitOptions.RemoveEmptyEntries);

        return words.Distinct().OrderBy(w => w).ToList();
    }

    /// <summary>
    /// Compares content between two PDFs
    /// </summary>
    public static (List<string> onlyInFirst, List<string> onlyInSecond, List<string> inBoth)
        CompareContent(string pdfPath1, string pdfPath2)
    {
        var words1 = new HashSet<string>(GetAllUniqueWords(pdfPath1));
        var words2 = new HashSet<string>(GetAllUniqueWords(pdfPath2));

        var onlyInFirst = words1.Except(words2).ToList();
        var onlyInSecond = words2.Except(words1).ToList();
        var inBoth = words1.Intersect(words2).ToList();

        return (onlyInFirst, onlyInSecond, inBoth);
    }

    /// <summary>
    /// Extracts content stream bytes from a page (for low-level verification)
    /// </summary>
    public static byte[] GetPageContentStream(string pdfPath, int pageIndex = 0)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            return Array.Empty<byte>();
        }

        var page = document.Pages[pageIndex];
        var contentStream = page.Contents.Elements.FirstOrDefault();

        if (contentStream is PdfSharp.Pdf.PdfDictionary dict && dict.Stream != null)
        {
            return dict.Stream.Value;
        }

        return Array.Empty<byte>();
    }
}
