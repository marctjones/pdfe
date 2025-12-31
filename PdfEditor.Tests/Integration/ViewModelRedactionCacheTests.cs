using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PdfEditor.ViewModels;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using Avalonia;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests to verify that the ViewModel correctly updates state after redaction,
/// specifically testing for stale text selection data.
///
/// This tests the actual ViewModel behavior, not just the services.
/// </summary>
public class ViewModelRedactionCacheTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public ViewModelRedactionCacheTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = NullLoggerFactory.Instance;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Test that after applying redactions and saving via ViewModel,
    /// the ViewModel's text extraction correctly reflects the redacted state.
    /// </summary>
    [AvaloniaFact]
    public async Task ViewModel_AfterRedactionAndSave_TextExtractionReturnsRedactedContent()
    {
        // Arrange - Skip if PDF not available
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"vm_redact_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(outputPath);

        // Create ViewModel with services
        var documentService = new PdfDocumentService(NullLogger<PdfDocumentService>.Instance);
        var renderService = new PdfRenderService(NullLogger<PdfRenderService>.Instance);
        var redactionService = new RedactionService(NullLogger<RedactionService>.Instance, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(NullLogger<PdfTextExtractionService>.Instance);
        var searchService = new PdfSearchService(NullLogger<PdfSearchService>.Instance);
        var ocrService = new PdfOcrService(NullLogger<PdfOcrService>.Instance, renderService);
        var signatureService = new SignatureVerificationService(NullLogger<SignatureVerificationService>.Instance);
        var verifier = new RedactionVerifier(NullLogger<RedactionVerifier>.Instance, _loggerFactory);
        var filenameSuggestionService = new FilenameSuggestionService();

        var viewModel = new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            _loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier,
            filenameSuggestionService);

        // Act - Load document using the script API
        await viewModel.LoadDocumentCommand(originalPath);
        _output.WriteLine($"Loaded document: {originalPath}");

        // Verify we can get text before redaction
        var textBefore = viewModel.ExtractAllText();
        textBefore.Should().Contain("TORRINGTON", "Document should contain TORRINGTON before redaction");
        _output.WriteLine($"Text before redaction contains TORRINGTON: TRUE");

        // Apply redaction via script API
        await viewModel.RedactTextCommand("TORRINGTON");
        _output.WriteLine("Redaction applied");

        // Save to new file
        await viewModel.SaveDocumentCommand(outputPath);
        _output.WriteLine($"Saved to: {outputPath}");

        // Now test: Extract text again - should NOT contain TORRINGTON
        // This simulates user selecting text after save
        var textAfter = viewModel.ExtractAllText();
        _output.WriteLine($"Text after redaction contains TORRINGTON: {textAfter.Contains("TORRINGTON")}");

        // Assert
        textAfter.Should().NotContain("TORRINGTON",
            "After redaction and save, text extraction should NOT contain redacted text. " +
            "If this fails, the ViewModel has stale state.");
    }

    /// <summary>
    /// Test the specific scenario: redact, save as new file, then try to copy text.
    /// This simulates the exact user workflow that was reported as buggy.
    /// </summary>
    [AvaloniaFact]
    public async Task ViewModel_SaveAsNewFile_TextExtractionUsesNewFile()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var newFilePath = Path.Combine(Path.GetTempPath(), $"vm_saveas_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(newFilePath);

        // Create ViewModel
        var documentService = new PdfDocumentService(NullLogger<PdfDocumentService>.Instance);
        var renderService = new PdfRenderService(NullLogger<PdfRenderService>.Instance);
        var redactionService = new RedactionService(NullLogger<RedactionService>.Instance, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(NullLogger<PdfTextExtractionService>.Instance);
        var searchService = new PdfSearchService(NullLogger<PdfSearchService>.Instance);
        var ocrService = new PdfOcrService(NullLogger<PdfOcrService>.Instance, renderService);
        var signatureService = new SignatureVerificationService(NullLogger<SignatureVerificationService>.Instance);
        var verifier = new RedactionVerifier(NullLogger<RedactionVerifier>.Instance, _loggerFactory);
        var filenameSuggestionService = new FilenameSuggestionService();

        var viewModel = new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            _loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier,
            filenameSuggestionService);

        // Act - Load, redact, save as
        await viewModel.LoadDocumentCommand(originalPath);
        await viewModel.RedactTextCommand("TORRINGTON");
        await viewModel.SaveDocumentCommand(newFilePath);

        // Check what file path the ViewModel thinks is current
        var currentDoc = viewModel.DocumentName;
        _output.WriteLine($"Current document name after SaveAs: {currentDoc}");

        // The ViewModel should now be using the new file
        // Extract text - should read from new file, not original
        var extractedText = viewModel.ExtractAllText();

        // Assert
        extractedText.Should().NotContain("TORRINGTON",
            "After SaveAs to new file, ViewModel should extract text from new file");
    }

    /// <summary>
    /// Test that the ViewModel properly updates its internal file path after SaveAs.
    /// </summary>
    [AvaloniaFact]
    public async Task ViewModel_AfterSaveAs_InternalFilePathUpdated()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var newFilePath = Path.Combine(Path.GetTempPath(), $"vm_filepath_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(newFilePath);

        var documentService = new PdfDocumentService(NullLogger<PdfDocumentService>.Instance);
        var renderService = new PdfRenderService(NullLogger<PdfRenderService>.Instance);
        var redactionService = new RedactionService(NullLogger<RedactionService>.Instance, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(NullLogger<PdfTextExtractionService>.Instance);
        var searchService = new PdfSearchService(NullLogger<PdfSearchService>.Instance);
        var ocrService = new PdfOcrService(NullLogger<PdfOcrService>.Instance, renderService);
        var signatureService = new SignatureVerificationService(NullLogger<SignatureVerificationService>.Instance);
        var verifier = new RedactionVerifier(NullLogger<RedactionVerifier>.Instance, _loggerFactory);
        var filenameSuggestionService = new FilenameSuggestionService();

        var viewModel = new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            _loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier,
            filenameSuggestionService);

        // Act
        await viewModel.LoadDocumentCommand(originalPath);
        var docNameBefore = viewModel.DocumentName;
        _output.WriteLine($"Document name before save: {docNameBefore}");

        await viewModel.RedactTextCommand("CERTIFICATE");
        await viewModel.SaveDocumentCommand(newFilePath);

        var docNameAfter = viewModel.DocumentName;
        _output.WriteLine($"Document name after save: {docNameAfter}");

        // Assert - Document name should reflect the new file
        docNameAfter.Should().Be(Path.GetFileName(newFilePath),
            "After SaveAs, DocumentName should reflect the new file path");
    }
}
