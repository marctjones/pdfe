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
    /// Creates a PDF with content at known grid positions for precise testing
    /// </summary>
    public static string CreateGridContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);
        var smallFont = new XFont("Arial", 8);

        // Create a grid of text elements at known positions
        // Format: "Cell(X,Y)" at position (X, Y)
        for (int x = 100; x <= 500; x += 100)
        {
            for (int y = 100; y <= 700; y += 100)
            {
                gfx.DrawString($"Cell({x},{y})", font, XBrushes.Black, new XPoint(x, y));
            }
        }

        // Add some graphics elements (rectangles) at known positions
        gfx.DrawRectangle(XPens.Blue, XBrushes.LightBlue, new XRect(150, 150, 80, 40));
        gfx.DrawRectangle(XPens.Green, XBrushes.LightGreen, new XRect(350, 350, 80, 40));
        gfx.DrawRectangle(XPens.Red, XBrushes.LightPink, new XRect(250, 550, 80, 40));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with specific content items at known exact positions
    /// Returns a dictionary mapping content to positions for verification
    /// </summary>
    public static (string path, Dictionary<string, (double x, double y, double width, double height)> contentMap)
        CreateMappedContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        var contentMap = new Dictionary<string, (double, double, double, double)>();

        // Text item 1: "CONFIDENTIAL" at top
        var text1 = "CONFIDENTIAL";
        contentMap[text1] = (100, 100, 120, 20);
        gfx.DrawString(text1, font, XBrushes.Black, new XPoint(100, 100));

        // Text item 2: "PUBLIC" at middle left
        var text2 = "PUBLIC";
        contentMap[text2] = (100, 300, 80, 20);
        gfx.DrawString(text2, font, XBrushes.Black, new XPoint(100, 300));

        // Text item 3: "SECRET" at middle right
        var text3 = "SECRET";
        contentMap[text3] = (400, 300, 80, 20);
        gfx.DrawString(text3, font, XBrushes.Black, new XPoint(400, 300));

        // Text item 4: "PRIVATE" at bottom
        var text4 = "PRIVATE";
        contentMap[text4] = (100, 500, 80, 20);
        gfx.DrawString(text4, font, XBrushes.Black, new XPoint(100, 500));

        // Text item 5: "INTERNAL" at bottom right
        var text5 = "INTERNAL";
        contentMap[text5] = (400, 500, 90, 20);
        gfx.DrawString(text5, font, XBrushes.Black, new XPoint(400, 500));

        // Graphics: Blue rectangle
        contentMap["BLUE_BOX"] = (50, 150, 100, 50);
        gfx.DrawRectangle(XBrushes.LightBlue, new XRect(50, 150, 100, 50));

        // Graphics: Green rectangle
        contentMap["GREEN_BOX"] = (350, 350, 100, 50);
        gfx.DrawRectangle(XBrushes.LightGreen, new XRect(350, 350, 100, 50));

        document.Save(outputPath);
        document.Dispose();

        return (outputPath, contentMap);
    }

    /// <summary>
    /// Creates a complex PDF with mixed content types
    /// </summary>
    public static string CreateComplexContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var normalFont = new XFont("Arial", 12);
        var smallFont = new XFont("Arial", 8);

        // Title
        gfx.DrawString("Confidential Document", titleFont, XBrushes.Black, new XPoint(50, 50));

        // Header section with background
        gfx.DrawRectangle(XBrushes.LightGray, new XRect(50, 70, 500, 40));
        gfx.DrawString("Classification: TOP SECRET", normalFont, XBrushes.Red, new XPoint(60, 95));

        // Body text
        gfx.DrawString("Subject: Redaction Testing", normalFont, XBrushes.Black, new XPoint(50, 150));
        gfx.DrawString("This document contains sensitive information.", normalFont, XBrushes.Black, new XPoint(50, 180));
        gfx.DrawString("Some parts must be redacted before release.", normalFont, XBrushes.Black, new XPoint(50, 210));

        // Sensitive data section
        gfx.DrawRectangle(XPens.Red, new XRect(50, 240, 500, 120));
        gfx.DrawString("SENSITIVE DATA:", normalFont, XBrushes.Red, new XPoint(60, 265));
        gfx.DrawString("Account: 1234-5678-9012-3456", normalFont, XBrushes.Black, new XPoint(60, 295));
        gfx.DrawString("SSN: 123-45-6789", normalFont, XBrushes.Black, new XPoint(60, 325));
        gfx.DrawString("Password: SuperSecret123!", normalFont, XBrushes.Black, new XPoint(60, 355));

        // Public information section
        gfx.DrawRectangle(XPens.Green, new XRect(50, 380, 500, 80));
        gfx.DrawString("PUBLIC INFORMATION:", normalFont, XBrushes.Green, new XPoint(60, 405));
        gfx.DrawString("Company: ACME Corporation", normalFont, XBrushes.Black, new XPoint(60, 435));

        // Footer
        gfx.DrawString("Page 1 of 1", smallFont, XBrushes.Gray, new XPoint(50, 750));
        gfx.DrawString("Document ID: DOC-2025-001", smallFont, XBrushes.Gray, new XPoint(400, 750));

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
