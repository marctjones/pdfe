using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace PdfEditor.Redaction.Cli.Tests.TestHelpers;

/// <summary>
/// Creates test PDFs with known content for unit testing.
/// </summary>
public static class TestPdfCreator
{
    /// <summary>
    /// Creates a simple PDF with the specified text content.
    /// </summary>
    public static void CreateSimplePdf(string outputPath, params string[] lines)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        double y = 100;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(72, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Creates a PDF with sensitive data patterns for testing.
    /// </summary>
    public static void CreateSensitiveDataPdf(string outputPath)
    {
        CreateSimplePdf(outputPath,
            "EMPLOYEE RECORD - CONFIDENTIAL",
            "",
            "Name: John Smith",
            "SSN: 123-45-6789",
            "Date of Birth: 03/15/1985",
            "Salary: $85,000",
            "Department: Engineering",
            "Phone: (555) 123-4567",
            "Email: john.smith@example.com",
            "",
            "This document contains sensitive PII."
        );
    }

    /// <summary>
    /// Creates a multi-page PDF for testing page-specific operations.
    /// </summary>
    public static void CreateMultiPagePdf(string outputPath, int pageCount)
    {
        using var document = new PdfDocument();
        var font = new XFont("Helvetica", 12);

        for (int i = 1; i <= pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString($"Page {i} of {pageCount}", font, XBrushes.Black, new XPoint(72, 100));
            gfx.DrawString($"SECRET-{i}", font, XBrushes.Black, new XPoint(72, 140));
            gfx.DrawString($"This is content on page {i}", font, XBrushes.Black, new XPoint(72, 180));
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Creates a PDF with various text patterns for regex testing.
    /// </summary>
    public static void CreatePatternTestPdf(string outputPath)
    {
        CreateSimplePdf(outputPath,
            "Pattern Test Document",
            "",
            "SSN Patterns:",
            "  123-45-6789",
            "  987-65-4321",
            "  555-12-3456",
            "",
            "Email Patterns:",
            "  user@example.com",
            "  test.email@domain.org",
            "  admin@company.net",
            "",
            "Phone Patterns:",
            "  (555) 123-4567",
            "  (800) 555-1234",
            "",
            "Date Patterns:",
            "  2024-01-15",
            "  2023-12-31"
        );
    }

    /// <summary>
    /// Creates a PDF with case-sensitive content for testing.
    /// </summary>
    public static void CreateCaseSensitivePdf(string outputPath)
    {
        CreateSimplePdf(outputPath,
            "CONFIDENTIAL Document",
            "Confidential Information",
            "This is confidential data.",
            "SENSITIVE Material",
            "Sensitive Information",
            "This contains sensitive data."
        );
    }

    /// <summary>
    /// Creates an empty PDF (no text content).
    /// </summary>
    public static void CreateEmptyPdf(string outputPath)
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.Save(outputPath);
    }

    /// <summary>
    /// Creates a PDF with Unicode and special characters.
    /// </summary>
    public static void CreateUnicodePdf(string outputPath)
    {
        CreateSimplePdf(outputPath,
            "Unicode Test Document",
            "",
            "ASCII: Hello World",
            "Special: @#$%^&*()",
            "Symbols: +=-_[]{}|",
            "Numbers: 1234567890"
        );
    }
}
