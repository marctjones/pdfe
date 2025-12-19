using Xunit;
using Tesseract;
using System.IO;
using System;

namespace PdfEditor.Tests.Integration;

public class OcrDependencyTest
{
    [Fact]
    public void VerifyTesseractDependenciesLoad()
    {
        // This test simply verifies that the native libraries can be loaded
        // and the engine can be initialized. It does NOT do any heavy OCR.
        
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var tessDataPath = Path.Combine(baseDir, "tessdata");
        
        // Ensure directory exists to avoid directory-not-found error, 
        // even though we mainly care about the DLL loading.
        if (!Directory.Exists(tessDataPath))
        {
            Directory.CreateDirectory(tessDataPath);
        }

        // We don't need valid language data just to check if the DLLs load.
        // TesseractEngine constructor might check for data, but the DllNotFoundException
        // would happen before that. 
        // To be safe, we wrap in try-catch. We only fail on DllNotFoundException.
        
        try
        {
            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
        }
        catch (DllNotFoundException ex)
        {
            Assert.Fail($"OCR Dependencies missing: {ex.Message}");
        }
        catch (Exception)
        {
            // Other exceptions (like missing tessdata) are fine for this specific 
            // dependency-check test. We just want to ensure the native libs are found.
        }
    }
}
