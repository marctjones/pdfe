using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;
using System;
using Avalonia.Headless.XUnit;

namespace PdfEditor.Tests.Integration;

[Collection("AvaloniaTests")] // Ensures tests run sequentially to avoid Avalonia platform conflicts
public class OcrIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempFile;
    private readonly string _tessDataSource;
    private readonly string _tessDataTarget;

    public OcrIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempFile = Path.Combine(Path.GetTempPath(), $"ocr_test_{Guid.NewGuid()}.pdf");

        // Initialize Avalonia Platform for PDFtoImage/SkiaSharp which might need it
        // This is done once per test class by the TestAppBuilder and Collection
        PdfEditor.Tests.UI.TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

        // Locate tessdata in the main project
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "../../../.."));
        _tessDataSource = Path.Combine(projectRoot, "tessdata");
        
        // Target is where the service looks for it (AppDomain BaseDirectory + tessdata)
        _tessDataTarget = Path.Combine(baseDir, "tessdata");

        // Ensure tessdata directory exists in the target location
        if (!Directory.Exists(_tessDataTarget))
        {
            _output.WriteLine($"Target tessdata directory '{_tessDataTarget}' does not exist. Attempting to create it.");
            try
            {
                Directory.CreateDirectory(_tessDataTarget);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error creating target tessdata directory: {ex.Message}");
                // Mark test as skipped if we cannot even create the directory
                TestSkipper.SkipTest($"Could not create tessdata target directory: {_tessDataTarget}. Error: {ex.Message}");
            }
        }

        // Check if tessdata (e.g., eng.traineddata) exists in the target location
        var engTrainedDataPath = Path.Combine(_tessDataTarget, "eng.traineddata");
        if (!File.Exists(engTrainedDataPath))
        {
            _output.WriteLine($"'eng.traineddata' not found in target '{_tessDataTarget}'.");
            if (Directory.Exists(_tessDataSource))
            {
                _output.WriteLine($"Source tessdata directory '{_tessDataSource}' found. Attempting to copy 'eng.traineddata'.");
                try
                {
                    var sourceEngTrainedDataPath = Path.Combine(_tessDataSource, "eng.traineddata");
                    if (File.Exists(sourceEngTrainedDataPath))
                    {
                        File.Copy(sourceEngTrainedDataPath, engTrainedDataPath, true);
                        _output.WriteLine($"Successfully copied 'eng.traineddata' from '{_tessDataSource}' to '{_tessDataTarget}'.");
                    }
                    else
                    {
                        TestSkipper.SkipTest($"'eng.traineddata' not found in source '{_tessDataSource}'. Please ensure it is present or downloaded.");
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Error copying 'eng.traineddata': {ex.Message}");
                    TestSkipper.SkipTest($"Failed to copy 'eng.traineddata'. Error: {ex.Message}");
                }
            }
            else
            {
                TestSkipper.SkipTest($"Neither target '{_tessDataTarget}' nor source '{_tessDataSource}' tessdata directories contain 'eng.traineddata'. Please ensure it is present or downloaded.");
            }
        }
        else
        {
            _output.WriteLine($"'eng.traineddata' found in target '{_tessDataTarget}'.");
        }
    }

    [AvaloniaFact(Timeout = 60000)] // Increased timeout to 60 seconds for OCR operations
    public async Task PerformOcr_ShouldExtractText_FromRenderedPdf()
    {
        _output.WriteLine("Starting OCR Test: PerformOcr_ShouldExtractText_FromRenderedPdf...");
        
        // This check ensures we don't proceed if TestSkipper was activated in the constructor
        TestSkipper.ThrowIfSkipped();

        // Arrange
        var expectedText = "The quick brown fox jumps over the lazy dog";
        _output.WriteLine($"Generating test PDF with text: '{expectedText}' at '{_tempFile}'");
        TestPdfGenerator.CreateSimpleTextPdf(_tempFile, expectedText);

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider(_output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _output.WriteLine("Initializing PdfRenderService and PdfOcrService...");
        var renderService = new PdfRenderService(loggerFactory.CreateLogger<PdfRenderService>());
        var ocrService = new PdfOcrService(loggerFactory.CreateLogger<PdfOcrService>(), renderService);

        _output.WriteLine($"Calling PerformOcrAsync for file '{_tempFile}' with language 'eng'...");
        // Act
        string? result = null;
        try
        {
            result = await ocrService.PerformOcrAsync(_tempFile, "eng");
            _output.WriteLine("PerformOcrAsync completed.");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during PerformOcrAsync: {ex.GetType().Name} - {ex.Message}");
            _output.WriteLine(ex.StackTrace);
            Assert.Fail($"PerformOcrAsync failed with exception: {ex.Message}");
        }

        _output.WriteLine($"OCR Result (length: {result?.Length ?? 0}):");
        _output.WriteLine(result ?? "[null]");

        // Assert
        result.Should().NotBeNullOrEmpty("because OCR should produce some text.");
        result.Should().ContainEquivalentOf("quick", "because 'quick' should be extracted.");
        result.Should().ContainEquivalentOf("brown", "because 'brown' should be extracted.");
        result.Should().ContainEquivalentOf("fox", "because 'fox' should be extracted.");
        
        _output.WriteLine("OCR Test: PerformOcr_ShouldExtractText_FromRenderedPdf PASSED.");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }
}
