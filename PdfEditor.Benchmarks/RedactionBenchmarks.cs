using System;
using System.IO;
using Avalonia;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction;
using RedactionParser = PdfEditor.Redaction.ContentStream.Parsing.ContentStreamParser;
using TextOperation = PdfEditor.Redaction.TextOperation;
using PdfEditor.Redaction.GlyphLevel;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Content;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace PdfEditor.Benchmarks;

/// <summary>
/// Comprehensive benchmarks for PDF redaction operations.
/// Run with: dotnet run -c Release
/// For quick test: dotnet run -c Release -- --job short
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class RedactionBenchmarks
{
    private string _simplePdfPath = string.Empty;
    private string _complexPdfPath = string.Empty;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private RedactionService _redactionService = default!;
    private RedactionParser _contentStreamParser = default!;
    private LetterFinder _letterFinder = default!;
    private TextRedactor _textRedactor = default!;

    [GlobalSetup]
    public void Setup()
    {
        // Create simple PDF (2 text lines)
        _simplePdfPath = Path.Combine(Path.GetTempPath(), $"benchmark_simple_{Guid.NewGuid():N}.pdf");
        CreateSimplePdf(_simplePdfPath);

        // Create complex PDF (many text lines, different fonts)
        _complexPdfPath = Path.Combine(Path.GetTempPath(), $"benchmark_complex_{Guid.NewGuid():N}.pdf");
        CreateComplexPdf(_complexPdfPath);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddFilter(level => level >= LogLevel.Warning));
        _redactionService = new RedactionService(NullLogger<RedactionService>.Instance, _loggerFactory);
        _contentStreamParser = new RedactionParser();
        _letterFinder = new LetterFinder();
        _textRedactor = new TextRedactor();
    }

    #region Content Stream Parsing Benchmarks

    [Benchmark(Description = "Parse simple PDF content stream")]
    public int ParseSimpleContentStream()
    {
        using var document = PdfReader.Open(_simplePdfPath, PdfDocumentOpenMode.Import);
        var operations = _contentStreamParser.ParsePage(document.Pages[0]);
        return operations.Count;
    }

    [Benchmark(Description = "Parse complex PDF content stream")]
    public int ParseComplexContentStream()
    {
        using var document = PdfReader.Open(_complexPdfPath, PdfDocumentOpenMode.Import);
        var operations = _contentStreamParser.ParsePage(document.Pages[0]);
        return operations.Count;
    }

    #endregion

    #region Letter Extraction Benchmarks

    [Benchmark(Description = "Extract letters with PdfPig (simple)")]
    public int ExtractLettersSimple()
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(_simplePdfPath);
        var letters = doc.GetPage(1).Letters;
        return letters.Count;
    }

    [Benchmark(Description = "Extract letters with PdfPig (complex)")]
    public int ExtractLettersComplex()
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(_complexPdfPath);
        var letters = doc.GetPage(1).Letters;
        return letters.Count;
    }

    #endregion

    #region Letter Finding Benchmarks

    [Benchmark(Description = "Find letters for text operation")]
    public int FindLettersForOperation()
    {
        using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(_simplePdfPath);
        var letters = pdfPigDoc.GetPage(1).Letters.ToList();

        using var pdfSharpDoc = PdfReader.Open(_simplePdfPath, PdfDocumentOpenMode.Import);
        var operations = _contentStreamParser.ParsePage(pdfSharpDoc.Pages[0]);

        int totalMatches = 0;
        foreach (var op in operations.OfType<TextOperation>())
        {
            var matches = _letterFinder.FindOperationLetters(op, letters);
            totalMatches += matches.Count;
        }
        return totalMatches;
    }

    #endregion

    #region Redaction Benchmarks

    [Benchmark(Description = "Redact single area (GUI service)")]
    public void RedactSingleAreaGui()
    {
        using var document = PdfReader.Open(_simplePdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(80, 80, 160, 40), _simplePdfPath, renderDpi: 72);
    }

    [Benchmark(Description = "Redact text with library API")]
    public bool RedactTextWithLibrary()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"redacted_{Guid.NewGuid():N}.pdf");
        try
        {
            var result = _textRedactor.RedactText(_simplePdfPath, outputPath, "confidential");
            return result.Success;
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Benchmark(Description = "Redact multiple areas sequentially")]
    public void RedactMultipleAreas()
    {
        using var document = PdfReader.Open(_simplePdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Simulate 5 redactions on same page
        for (int i = 0; i < 5; i++)
        {
            _redactionService.RedactArea(page, new Rect(80, 80 + i * 50, 160, 30), _simplePdfPath, renderDpi: 72);
        }
    }

    #endregion

    #region Full Pipeline Benchmarks

    [Benchmark(Description = "Full pipeline: load, redact, save")]
    public bool FullPipelineRedaction()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"pipeline_{Guid.NewGuid():N}.pdf");
        try
        {
            // This exercises the complete redaction flow
            var result = _textRedactor.RedactText(_complexPdfPath, outputPath, "Lorem");
            return result.Success;
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        TryDelete(_simplePdfPath);
        TryDelete(_complexPdfPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static void CreateSimplePdf(string path)
    {
        using var document = new PdfSharpDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 14);
        gfx.DrawString("This is confidential information that should be redacted.", font, XBrushes.Black, new XRect(80, 80, 500, 40));
        gfx.DrawString("Public information can remain visible.", font, XBrushes.Black, new XRect(80, 200, 500, 40));
        document.Save(path);
    }

    private static void CreateComplexPdf(string path)
    {
        using var document = new PdfSharpDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);

        var fonts = new[]
        {
            new XFont("Arial", 12),
            new XFont("Arial", 14, XFontStyleEx.Bold),
            new XFont("Times New Roman", 12),
            new XFont("Courier New", 10)
        };

        // Generate Lorem ipsum-like content
        var paragraphs = new[]
        {
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
            "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
            "Duis aute irure dolor in reprehenderit in voluptate velit esse.",
            "Excepteur sint occaecat cupidatat non proident, sunt in culpa.",
            "Confidential: Account Number 1234-5678-9012-3456",
            "SSN: 123-45-6789 (REDACT THIS)",
            "Name: John Smith, Address: 123 Main Street",
            "Email: john.smith@example.com, Phone: (555) 123-4567",
            "This document contains sensitive information.",
            "Additional public content follows below.",
            "More text for parsing performance testing.",
            "Final line of the complex test document."
        };

        double y = 60;
        for (int i = 0; i < paragraphs.Length; i++)
        {
            var font = fonts[i % fonts.Length];
            gfx.DrawString(paragraphs[i], font, XBrushes.Black, new XRect(50, y, 500, 30));
            y += 45;
        }

        document.Save(path);
    }
}
