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
/// Integration tests that execute automation scripts to validate GUI functionality.
/// These tests run the .csx files from automation-scripts/ directory.
/// </summary>
public class AutomationScriptTests
{
    private readonly ITestOutputHelper _output;
    private readonly string _scriptsDir;

    public AutomationScriptTests(ITestOutputHelper output)
    {
        _output = output;

        // Find the automation-scripts directory
        var currentDir = Directory.GetCurrentDirectory();
        var repoRoot = FindRepositoryRoot(currentDir);

        if (repoRoot == null)
        {
            throw new InvalidOperationException("Could not find repository root directory");
        }

        _scriptsDir = Path.Combine(repoRoot, "automation-scripts");

        if (!Directory.Exists(_scriptsDir))
        {
            throw new DirectoryNotFoundException($"Automation scripts directory not found: {_scriptsDir}");
        }

        _output.WriteLine($"Automation scripts directory: {_scriptsDir}");
    }

    /// <summary>
    /// Helper to execute an automation script and validate it succeeds.
    /// </summary>
    private async Task<ScriptExecutionResult> RunAutomationScriptAsync(
        string scriptName,
        MainWindowViewModel? viewModel = null)
    {
        viewModel ??= new MainWindowViewModel();

        var scriptPath = Path.Combine(_scriptsDir, scriptName);

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Automation script not found: {scriptPath}");
        }

        _output.WriteLine($"Executing automation script: {scriptName}");
        _output.WriteLine($"Script path: {scriptPath}");

        var scriptingService = new ScriptingService(viewModel);
        var result = await scriptingService.ExecuteFileAsync(scriptPath);

        _output.WriteLine($"Execution completed: {(result.Success ? "SUCCESS" : "FAILED")}");

        if (!result.Success)
        {
            _output.WriteLine($"Error: {result.ErrorMessage}");
        }
        else
        {
            _output.WriteLine($"Return value: {result.ReturnValue}");
        }

        return result;
    }

    /// <summary>
    /// Helper to find the repository root directory.
    /// </summary>
    private string? FindRepositoryRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);

        while (dir != null)
        {
            // Look for .git directory or automation-scripts directory
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                Directory.Exists(Path.Combine(dir.FullName, "automation-scripts")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [Fact(Skip = "Requires GUI integration (#59) - LoadDocumentCommand not implemented yet")]
    public async Task AutomationScript_LoadDocument_ExecutesSuccessfully()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await RunAutomationScriptAsync("test-load-document.csx", viewModel);

        // Assert
        result.Success.Should().BeTrue("script should compile and execute");

        // Script returns 0 on success, 1 on failure
        var exitCode = Convert.ToInt32(result.ReturnValue);
        exitCode.Should().Be(0, "script should pass all assertions");
    }

    [Fact(Skip = "Requires GUI integration (#59) - RedactTextCommand not implemented yet")]
    public async Task AutomationScript_RedactText_ExecutesSuccessfully()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await RunAutomationScriptAsync("test-redact-text.csx", viewModel);

        // Assert
        result.Success.Should().BeTrue("script should compile and execute");

        var exitCode = Convert.ToInt32(result.ReturnValue);
        exitCode.Should().Be(0, "complete redaction workflow should succeed");
    }

    [Fact(Skip = "Requires GUI integration (#59) - Birth certificate workflow not implemented yet")]
    public async Task AutomationScript_BirthCertificate_ExecutesSuccessfully()
    {
        // This is the CORNERSTONE TEST for v1.3.0 milestone
        // If this passes, the milestone is complete

        // Arrange
        var viewModel = new MainWindowViewModel();

        // Act
        var result = await RunAutomationScriptAsync("test-birth-certificate.csx", viewModel);

        // Assert
        result.Success.Should().BeTrue("script should compile and execute");

        var exitCode = Convert.ToInt32(result.ReturnValue);
        exitCode.Should().Be(0, "birth certificate redaction should succeed");

        _output.WriteLine("\n✅ v1.3.0 MILESTONE COMPLETE");
        _output.WriteLine("Birth certificate redaction works end-to-end via GUI");
    }

    [Fact]
    public void AutomationScripts_AllScriptsExist()
    {
        // Verify all required automation scripts are present

        var requiredScripts = new[]
        {
            "test-load-document.csx",
            "test-redact-text.csx",
            "test-birth-certificate.csx",
        };

        foreach (var scriptName in requiredScripts)
        {
            var scriptPath = Path.Combine(_scriptsDir, scriptName);
            File.Exists(scriptPath).Should().BeTrue($"{scriptName} should exist at {scriptPath}");
        }
    }

    [Fact]
    public async Task AutomationScripts_ValidateAllScriptSyntax()
    {
        // Validate that all automation scripts have valid C# syntax
        // This doesn't execute them, just checks for compilation errors

        var viewModel = new MainWindowViewModel();
        var scriptingService = new ScriptingService(viewModel);

        var scriptFiles = Directory.GetFiles(_scriptsDir, "*.csx");

        _output.WriteLine($"Found {scriptFiles.Length} automation script(s) to validate");

        foreach (var scriptPath in scriptFiles)
        {
            _output.WriteLine($"\nValidating: {Path.GetFileName(scriptPath)}");

            var scriptCode = await File.ReadAllTextAsync(scriptPath);
            var errors = scriptingService.ValidateScript(scriptCode);

            if (errors.Count > 0)
            {
                _output.WriteLine($"  ❌ Compilation errors:");
                foreach (var error in errors)
                {
                    _output.WriteLine($"    - {error}");
                }
            }
            else
            {
                _output.WriteLine($"  ✅ Valid syntax");
            }

            errors.Should().BeEmpty($"{Path.GetFileName(scriptPath)} should have valid C# syntax");
        }
    }
}
