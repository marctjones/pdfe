using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.UI;

/// <summary>
/// GUI tests using Roslyn C# scripting to automate user interactions.
/// These tests validate the complete GUI workflow by executing scripts
/// that interact with MainWindowViewModel exactly as a user would.
/// </summary>
public class ScriptedGuiTests
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDataDir;

    public ScriptedGuiTests(ITestOutputHelper output)
    {
        _output = output;
        _testDataDir = Path.Combine(Path.GetTempPath(), "pdfe_scripted_tests");
        Directory.CreateDirectory(_testDataDir);
    }

    /// <summary>
    /// Helper to execute a script and return the result.
    /// </summary>
    private async Task<ScriptExecutionResult> ExecuteScriptAsync(
        MainWindowViewModel viewModel,
        string scriptCode)
    {
        var scriptingService = new ScriptingService(viewModel);
        var result = await scriptingService.ExecuteAsync(scriptCode);

        _output.WriteLine($"Script execution: {(result.Success ? "SUCCESS" : "FAILED")}");
        if (!result.Success)
        {
            _output.WriteLine($"Error: {result.ErrorMessage}");
        }
        else if (result.ReturnValue != null)
        {
            _output.WriteLine($"Return value: {result.ReturnValue}");
        }

        return result;
    }

    [Fact]
    public async Task Script_CanAccessViewModel_ReturnsExpectedValue()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await ExecuteScriptAsync(viewModel, @"
            // Simple script that returns a value
            return 42;
        ");

        // Assert
        result.Success.Should().BeTrue();
        result.ReturnValue.Should().Be(42);
    }

    [Fact]
    public async Task Script_InvalidSyntax_ReturnsCompilationError()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await ExecuteScriptAsync(viewModel, @"
            // Invalid syntax
            this is not valid C# code {
        ");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Compilation error");
    }

    [Fact]
    public async Task Script_AccessViewModelProperties_Works()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await ExecuteScriptAsync(viewModel, @"
            // Access ViewModel properties
            var hasPendingRedactions = PendingRedactions.Count > 0;
            var hasDocument = CurrentDocument != null;

            return new {
                PendingRedactions = hasPendingRedactions,
                HasDocument = hasDocument
            };
        ");

        // Assert
        result.Success.Should().BeTrue();
        result.ReturnValue.Should().NotBeNull();
    }

    [Fact(Skip = "Requires GUI integration (#59) - LoadDocumentCommand not yet implemented")]
    public async Task Script_LoadDocument_UpdatesViewModel()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var testPdf = CreateTestPdf();

        // Act
        var result = await ExecuteScriptAsync(viewModel, $@"
            // Load a PDF document
            await LoadDocumentCommand.Execute(""{testPdf}"");

            // Verify document is loaded
            var isLoaded = CurrentDocument != null;
            var filePath = CurrentDocument?.FilePath;

            return new {{
                IsLoaded = isLoaded,
                FilePath = filePath
            }};
        ");

        // Assert
        result.Success.Should().BeTrue();
        // TODO: Add assertions once LoadDocumentCommand exists
    }

    [Fact(Skip = "Requires GUI integration (#59) - RedactTextCommand not yet implemented")]
    public async Task Script_RedactText_CreatesRedactionArea()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var testPdf = CreateTestPdf();

        // Act
        var result = await ExecuteScriptAsync(viewModel, $@"
            // Load document and redact text
            await LoadDocumentCommand.Execute(""{testPdf}"");
            await RedactTextCommand.Execute(""SECRET"");

            // Check pending redactions
            var count = PendingRedactions.Count;

            return count;
        ");

        // Assert
        result.Success.Should().BeTrue();
        // TODO: Verify redaction count once RedactTextCommand exists
    }

    [Fact(Skip = "Requires GUI integration (#59) - Full workflow not yet implemented")]
    public async Task Script_CompleteRedactionWorkflow_EndToEnd()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var sourcePdf = CreateTestPdf();
        var outputPdf = Path.Combine(_testDataDir, "redacted_output.pdf");

        // Act - Complete workflow: Load → Redact → Save
        var result = await ExecuteScriptAsync(viewModel, $@"
            using System.IO;

            // 1. Load document
            await LoadDocumentCommand.Execute(""{sourcePdf}"");
            Console.WriteLine($""Loaded: {{CurrentDocument.FilePath}}"");

            // 2. Perform redactions
            await RedactTextCommand.Execute(""SECRET"");
            await RedactTextCommand.Execute(""CONFIDENTIAL"");
            Console.WriteLine($""Pending redactions: {{PendingRedactions.Count}}"");

            // 3. Apply redactions
            await ApplyRedactionsCommand.Execute();
            Console.WriteLine(""Redactions applied"");

            // 4. Save document
            await SaveDocumentCommand.Execute(""{outputPdf}"");
            Console.WriteLine($""Saved to: {outputPdf}"");

            // 5. Verify file exists
            var outputExists = File.Exists(""{outputPdf}"");

            return new {{
                Success = true,
                OutputExists = outputExists,
                PendingCount = PendingRedactions.Count
            }};
        ");

        // Assert
        result.Success.Should().BeTrue();
        // TODO: Verify output PDF and redaction once commands exist
    }

    [Fact(Skip = "Requires GUI integration (#59) - Birth certificate test")]
    public async Task Script_BirthCertificateRedaction_Success()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var birthCertPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        var outputPdf = Path.Combine(_testDataDir, "birth-cert-redacted.pdf");

        // Skip if birth certificate not available
        if (!File.Exists(birthCertPath))
        {
            _output.WriteLine($"Skipping: Birth certificate not found at {birthCertPath}");
            return;
        }

        // Act - Birth certificate redaction workflow
        var result = await ExecuteScriptAsync(viewModel, $@"
            using System.IO;

            // Load birth certificate
            await LoadDocumentCommand.Execute(""{birthCertPath}"");
            Console.WriteLine(""Birth certificate loaded"");

            // Redact sensitive information
            var termsToRedact = new[] {{
                ""TORRINGTON"",
                ""CERTIFICATE"",
                ""BIRTH"",
                ""CITY CLERK""
            }};

            foreach (var term in termsToRedact)
            {{
                await RedactTextCommand.Execute(term);
                Console.WriteLine($""Redacting: {{term}}"");
            }}

            Console.WriteLine($""Total pending redactions: {{PendingRedactions.Count}}"");

            // Apply all redactions
            await ApplyRedactionsCommand.Execute();

            // Save redacted document
            await SaveDocumentCommand.Execute(""{outputPdf}"");

            return new {{
                Success = File.Exists(""{outputPdf}""),
                RedactionCount = PendingRedactions.Count
            }};
        ");

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPdf).Should().BeTrue("redacted PDF should be created");

        // TODO: Verify text removal using external tool (pdfer verify)
    }

    [Fact(Skip = "Requires GUI integration (#59) - Error handling test")]
    public async Task Script_LoadNonexistentFile_HandlesError()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var nonexistentFile = "/tmp/does-not-exist.pdf";

        // Act
        var result = await ExecuteScriptAsync(viewModel, $@"
            try
            {{
                await LoadDocumentCommand.Execute(""{nonexistentFile}"");
                return false; // Should not reach here
            }}
            catch (FileNotFoundException ex)
            {{
                Console.WriteLine($""Caught expected error: {{ex.Message}}"");
                return true;
            }}
        ");

        // Assert
        result.Success.Should().BeTrue();
        result.ReturnValue.Should().Be(true, "script should catch FileNotFoundException");
    }

    [Fact(Skip = "Requires GUI integration (#59) - Multi-page test")]
    public async Task Script_RedactMultiplePages_AllPagesProcessed()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var multiPagePdf = CreateMultiPageTestPdf();
        var outputPdf = Path.Combine(_testDataDir, "multipage-redacted.pdf");

        // Act
        var result = await ExecuteScriptAsync(viewModel, $@"
            // Load multi-page document
            await LoadDocumentCommand.Execute(""{multiPagePdf}"");
            var pageCount = CurrentDocument.PageCount;
            Console.WriteLine($""Document has {{pageCount}} pages"");

            // Redact text that appears on multiple pages
            await RedactTextCommand.Execute(""CONFIDENTIAL"");

            var redactionCount = PendingRedactions.Count;
            Console.WriteLine($""Created {{redactionCount}} redaction areas"");

            // Apply and save
            await ApplyRedactionsCommand.Execute();
            await SaveDocumentCommand.Execute(""{outputPdf}"");

            return new {{
                PageCount = pageCount,
                RedactionCount = redactionCount
            }};
        ");

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPdf).Should().BeTrue();
    }

    /// <summary>
    /// Helper to create a simple test PDF.
    /// </summary>
    private string CreateTestPdf()
    {
        var pdfPath = Path.Combine(_testDataDir, "test_simple.pdf");

        // TODO: Use TestPdfGenerator to create PDF with known content
        // For now, return placeholder path
        return pdfPath;
    }

    /// <summary>
    /// Helper to create a multi-page test PDF.
    /// </summary>
    private string CreateMultiPageTestPdf()
    {
        var pdfPath = Path.Combine(_testDataDir, "test_multipage.pdf");

        // TODO: Use TestPdfGenerator to create multi-page PDF
        return pdfPath;
    }
}
