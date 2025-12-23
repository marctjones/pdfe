using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Redaction.Tests.Utilities;

/// <summary>
/// Generates test PDFs with known content for verifying redaction.
/// </summary>
public static class TestPdfGenerator
{
    /// <summary>
    /// Create a simple PDF with a single line of text using Tj operator.
    /// </summary>
    public static void CreateSimpleTextPdf(string outputPath, string text)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        // Draw at known position (100, 700 in PDF coords = 100, 92 in graphics coords)
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 92));

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with multiple lines of text.
    /// </summary>
    public static void CreateMultiLineTextPdf(string outputPath, params string[] lines)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        double y = 92;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with text at specific positions.
    /// </summary>
    public static void CreateTextAtPositions(string outputPath, params (string text, double x, double y)[] items)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        foreach (var (text, x, y) in items)
        {
            // Convert PDF Y to graphics Y (PDF is bottom-left, graphics is top-left)
            var graphicsY = 792 - y;
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, graphicsY));
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with sensitive data patterns (for redaction testing).
    /// </summary>
    public static void CreateSensitiveDataPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        var lines = new[]
        {
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Date of Birth: 01/15/1990",
            "Address: 123 Main Street",
            "Phone: (555) 123-4567",
            "Email: john.doe@example.com"
        };

        double y = 92;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF that uses TJ array with kerning (more complex text storage).
    /// This requires manually constructing PDF content, which PDFsharp may not produce directly.
    /// For now, this creates a simple PDF - TJ testing requires sample PDFs.
    /// </summary>
    public static void CreateKernedTextPdf(string outputPath, string text)
    {
        // PDFsharp typically generates Tj operators
        // For TJ testing, we'll need actual PDF samples
        CreateSimpleTextPdf(outputPath, text);
    }
}
