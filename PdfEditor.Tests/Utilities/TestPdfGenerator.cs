using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System.IO;

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Generates simple test PDFs with known content for testing
/// </summary>
public static class TestPdfGenerator
{
    /// <summary>
    /// Creates a simple single-page PDF with text at known positions
    /// </summary>
    public static string CreateSimpleTextPdf(string outputPath, string text = "Test Content")
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Draw text at known position (100, 100)
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 100));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with multiple text blocks at known positions
    /// </summary>
    public static string CreateMultiTextPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Text blocks at different positions
        gfx.DrawString("CONFIDENTIAL", font, XBrushes.Black, new XPoint(100, 100));
        gfx.DrawString("Public Information", font, XBrushes.Black, new XPoint(100, 200));
        gfx.DrawString("Secret Data", font, XBrushes.Black, new XPoint(100, 300));
        gfx.DrawString("Normal Text", font, XBrushes.Black, new XPoint(100, 400));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with text and graphics (rectangles)
    /// </summary>
    public static string CreateTextWithGraphicsPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Draw rectangles
        gfx.DrawRectangle(XBrushes.LightBlue, new XRect(50, 50, 200, 50));
        gfx.DrawRectangle(XBrushes.LightGreen, new XRect(50, 150, 200, 50));
        
        // Draw text over rectangles
        gfx.DrawString("Text in Blue Box", font, XBrushes.Black, new XPoint(60, 75));
        gfx.DrawString("Text in Green Box", font, XBrushes.Black, new XPoint(60, 175));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with transformed (rotated/scaled) text
    /// </summary>
    public static string CreateTransformedTextPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Normal text
        gfx.DrawString("Normal Text", font, XBrushes.Black, new XPoint(100, 100));
        
        // Rotated text
        gfx.Save();
        gfx.RotateAtTransform(45, new XPoint(200, 200));
        gfx.DrawString("Rotated Text", font, XBrushes.Black, new XPoint(200, 200));
        gfx.Restore();
        
        // Scaled text
        gfx.Save();
        gfx.ScaleTransform(1.5, 1.5);
        gfx.DrawString("Scaled Text", font, XBrushes.Black, new XPoint(100, 200));
        gfx.Restore();
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a multi-page PDF
    /// </summary>
    public static string CreateMultiPagePdf(string outputPath, int pageCount = 3)
    {
        var document = new PdfDocument();
        
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            
            gfx.DrawString($"Page {i + 1} Content", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString($"Secret on Page {i + 1}", font, XBrushes.Black, new XPoint(100, 200));
        }
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Cleans up test PDF file
    /// </summary>
    public static void CleanupTestFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
