using System;
using System.IO;
using Avalonia;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Benchmarks;

[MemoryDiagnoser]
public class RedactionBenchmarks
{
    private string _pdfPath = string.Empty;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private RedactionService _redactionService = default!;

    [GlobalSetup]
    public void Setup()
    {
        _pdfPath = Path.Combine(Path.GetTempPath(), $"benchmark_doc_{Guid.NewGuid():N}.pdf");
        CreateSamplePdf(_pdfPath);
        _loggerFactory = LoggerFactory.Create(builder => builder.AddFilter(level => level >= LogLevel.Warning));
        _redactionService = new RedactionService(NullLogger<RedactionService>.Instance, _loggerFactory);
    }

    [Benchmark]
    public void ApplySingleRedaction()
    {
        using var document = PdfReader.Open(_pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(80, 80, 160, 40), renderDpi: 72);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (File.Exists(_pdfPath))
            {
                File.Delete(_pdfPath);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void CreateSamplePdf(string path)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 14);
        gfx.DrawString("Benchmark confidential block", font, XBrushes.Black, new XRect(80, 80, 400, 40));
        gfx.DrawString("Public information", font, XBrushes.Black, new XRect(80, 200, 400, 40));
        document.Save(path);
    }
}
