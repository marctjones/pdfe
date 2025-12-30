using Xunit;
using PdfEditor.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;
using System;
using SkiaSharp;
using Avalonia.Headless.XUnit;

namespace PdfEditor.Tests.Integration;

[Collection("AvaloniaTests")] // Ensures tests run sequentially to avoid Avalonia platform conflicts
public class RenderIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public RenderIntegrationTest(ITestOutputHelper output)
    {
        _output = output;

        // Ensure Avalonia is initialized for PDFtoImage/SkiaSharp which might need it
        PdfEditor.Tests.UI.TestAppBuilder.EnsureInitialized();
    }

    [AvaloniaFact(Timeout = 15000)] // Use AvaloniaFact and add a timeout
    public async Task RenderPage_ShouldComplete()
    {
        _output.WriteLine("Starting Render Test...");
        
        var tempFile = Path.Combine(Path.GetTempPath(), $"render_test_{Guid.NewGuid()}.pdf");
        
        try 
        {
            // Create a simple PDF
            _output.WriteLine("Creating PDF...");
            Utilities.TestPdfGenerator.CreateSimpleTextPdf(tempFile, "Render Test");
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new XUnitLoggerProvider(_output)));
            var renderService = new PdfRenderService(loggerFactory.CreateLogger<PdfRenderService>());

            _output.WriteLine("Calling RenderPageAsync...");
            
            var skBitmap = await renderService.RenderPageAsync(tempFile, 0, 72);
            
            _output.WriteLine("Render complete.");
            Assert.NotNull(skBitmap);
            
            _output.WriteLine($"Bitmap size: {skBitmap.Width}x{skBitmap.Height}");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
