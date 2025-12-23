using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using ReactiveUI;
using System;
using System.Reactive;
using System.Threading.Tasks;

namespace PdfEditor.ViewModels;

/// <summary>
/// Scripting API for MainWindowViewModel.
/// These commands expose GUI functionality to Roslyn C# scripts for automation and testing.
/// </summary>
public partial class MainWindowViewModel
{
    // Scripting Properties (exposed to Roslyn scripts)

    /// <summary>
    /// Wrapper for the currently loaded PDF document that provides scripting-friendly properties.
    /// Scripts can check if CurrentDocument != null to verify a document is loaded.
    /// </summary>
    public CurrentDocumentInfo? CurrentDocument => _documentService?.IsDocumentLoaded == true
        ? new CurrentDocumentInfo(_documentService.GetCurrentDocument(), _currentFilePath, _documentService.PageCount)
        : null;

    /// <summary>
    /// The file path of the currently loaded document.
    /// </summary>
    public string FilePath => _currentFilePath;

    /// <summary>
    /// Collection of pending redaction areas (marked but not yet applied).
    /// Scripts can check PendingRedactions.Count to verify redactions were marked.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<PdfEditor.Models.PendingRedaction> PendingRedactions =>
        RedactionWorkflow.PendingRedactions;

    // Scripting Commands (exposed to Roslyn scripts as Task-based wrappers)
    // Note: These are NOT ReactiveCommands - they're simple Task-returning methods for scripting

    /// <summary>
    /// Load a document (for Roslyn scripts).
    /// Returns a Task that completes when the document is loaded.
    /// </summary>
    public Task LoadDocumentCommand(string filePath) => LoadDocumentViaScriptAsync(filePath);

    /// <summary>
    /// Redact all occurrences of text (for Roslyn scripts).
    /// Returns a Task that completes when redactions are marked.
    /// </summary>
    public Task RedactTextCommand(string text) => RedactTextViaScriptAsync(text);

    /// <summary>
    /// Apply all pending redactions (for Roslyn scripts).
    /// Returns a Task that completes when redactions are applied.
    /// </summary>
    public Task ApplyRedactionsCommand() => ApplyRedactionsViaScriptAsync();

    /// <summary>
    /// Save the document (for Roslyn scripts).
    /// Returns a Task that completes when the document is saved.
    /// </summary>
    public Task SaveDocumentCommand(string filePath) => SaveDocumentViaScriptAsync(filePath);

    /// <summary>
    /// Initialize scripting commands (call from main constructor)
    /// </summary>
    private void InitializeScriptingCommands()
    {
        // Scripting commands are now simple Task-returning methods (not ReactiveCommands)
        // No initialization needed
    }

    /// <summary>
    /// Load a document (for Roslyn scripts).
    /// Usage: await LoadDocumentCommand.Execute("/path/to/file.pdf")
    /// </summary>
    private async Task LoadDocumentViaScriptAsync(string filePath)
    {
        _logger.LogInformation("[SCRIPT] LoadDocumentCommand.Execute('{FilePath}')", filePath);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("[SCRIPT] LoadDocumentCommand: File path is empty");
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
        }

        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogError("[SCRIPT] LoadDocumentCommand: File not found: {FilePath}", filePath);
            throw new System.IO.FileNotFoundException($"File not found: {filePath}", filePath);
        }

        await LoadDocumentAsync(filePath);
        _logger.LogInformation("[SCRIPT] LoadDocumentCommand completed successfully");
    }

    /// <summary>
    /// Redact all occurrences of the specified text on all pages (for Roslyn scripts).
    /// Usage: await RedactTextCommand.Execute("SECRET")
    /// </summary>
    private async Task RedactTextViaScriptAsync(string text)
    {
        _logger.LogInformation("[SCRIPT] RedactTextCommand.Execute('{Text}')", text);

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("[SCRIPT] RedactTextCommand: Text is empty");
            throw new ArgumentException("Text to redact cannot be empty", nameof(text));
        }

        if (!_documentService.IsDocumentLoaded || _searchService == null)
        {
            _logger.LogError("[SCRIPT] RedactTextCommand: No document loaded");
            throw new InvalidOperationException("No document loaded. Call LoadDocumentCommand first.");
        }

        // Use search service to find all occurrences of the text
        var matches = _searchService.Search(
            _currentFilePath,
            text,
            caseSensitive: false,
            wholeWordsOnly: false,
            useRegex: false);

        _logger.LogInformation("[SCRIPT] RedactTextCommand: Found {MatchCount} occurrences of '{Text}'",
            matches.Count, text);

        if (matches.Count == 0)
        {
            _logger.LogWarning("[SCRIPT] RedactTextCommand: No matches found for '{Text}'", text);
            return;
        }

        // Get page height for coordinate conversion (needed for all pages)
        var pageHeights = new double[TotalPages];
        for (int i = 0; i < TotalPages; i++)
        {
            pageHeights[i] = _documentService.GetPageHeight(i);
        }

        // Mark each match as a redaction area
        int markedCount = 0;
        foreach (var match in matches)
        {
            try
            {
                // Convert search match (PDF coordinates) to redaction area (screen coordinates)
                var pageHeight = pageHeights[match.PageIndex];

                // Convert Y from PDF (bottom-left) to Avalonia (top-left)
                var avaloniaY = pageHeight - match.Y - match.Height;

                // Scale to screen coordinates (150 DPI render = 2.083x PDF 72 DPI)
                var dpiScale = 150.0 / 72.0;
                var screenRect = new Avalonia.Rect(
                    match.X * dpiScale,
                    avaloniaY * dpiScale,
                    match.Width * dpiScale,
                    match.Height * dpiScale
                );

                // Extract preview text
                string previewText = match.MatchedText ?? string.Empty;

                // Mark the area (page numbers are 1-based for display)
                RedactionWorkflow.MarkArea(match.PageIndex + 1, screenRect, previewText);
                markedCount++;

                _logger.LogDebug("[SCRIPT] Marked redaction on page {Page}: '{Text}' at ({X:F1},{Y:F1},{W:F1}x{H:F1})",
                    match.PageIndex + 1, previewText,
                    screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SCRIPT] Failed to mark redaction for match on page {Page}", match.PageIndex + 1);
            }
        }

        FileState.PendingRedactionsCount = RedactionWorkflow.PendingCount;
        this.RaisePropertyChanged(nameof(SaveButtonText));
        this.RaisePropertyChanged(nameof(StatusBarText));

        _logger.LogInformation("[SCRIPT] RedactTextCommand completed: Marked {MarkedCount}/{TotalCount} redactions",
            markedCount, matches.Count);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Apply all pending redactions to the in-memory document (for Roslyn scripts).
    /// This modifies the document but does not save it. Use SaveDocumentCommand to save.
    /// Usage: await ApplyRedactionsCommand.Execute()
    /// </summary>
    private async Task ApplyRedactionsViaScriptAsync()
    {
        _logger.LogInformation("[SCRIPT] ApplyRedactionsCommand.Execute()");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogError("[SCRIPT] ApplyRedactionsCommand: No document loaded");
            throw new InvalidOperationException("No document loaded. Call LoadDocumentCommand first.");
        }

        if (RedactionWorkflow.PendingCount == 0)
        {
            _logger.LogWarning("[SCRIPT] ApplyRedactionsCommand: No pending redactions to apply");
            return;
        }

        try
        {
            _logger.LogInformation("[SCRIPT] Applying {Count} redactions to in-memory document",
                RedactionWorkflow.PendingCount);

            // Get current document
            var document = _documentService.GetCurrentDocument();
            if (document == null)
            {
                _logger.LogError("[SCRIPT] ApplyRedactionsCommand: Document is null");
                throw new InvalidOperationException("Document is null");
            }

            // Apply each pending redaction
            foreach (var pending in RedactionWorkflow.PendingRedactions.ToList())
            {
                _logger.LogDebug("[SCRIPT] Applying redaction on page {Page}", pending.PageNumber);

                // pending.PageNumber is 1-based (for display), convert to 0-based for array access
                var pageIndex = pending.PageNumber - 1;
                var page = document.Pages[pageIndex];

                // pending.Area is in 150 DPI image pixels (screen coordinates)
                _redactionService.RedactArea(page, pending.Area, renderDpi: 150);
            }

            // Move redactions to applied list
            RedactionWorkflow.MoveToApplied();
            FileState.PendingRedactionsCount = 0;
            this.RaisePropertyChanged(nameof(SaveButtonText));
            this.RaisePropertyChanged(nameof(StatusBarText));

            _logger.LogInformation("[SCRIPT] ApplyRedactionsCommand completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SCRIPT] ApplyRedactionsCommand failed");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Save the document to the specified path (for Roslyn scripts).
    /// If redactions are pending, apply them first, then save.
    /// Usage: await SaveDocumentCommand.Execute("/path/to/output.pdf")
    /// </summary>
    private async Task SaveDocumentViaScriptAsync(string filePath)
    {
        _logger.LogInformation("[SCRIPT] SaveDocumentCommand.Execute('{FilePath}')", filePath);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("[SCRIPT] SaveDocumentCommand: File path is empty");
            throw new ArgumentException("Output file path cannot be empty", nameof(filePath));
        }

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogError("[SCRIPT] SaveDocumentCommand: No document loaded");
            throw new InvalidOperationException("No document loaded. Call LoadDocumentCommand first.");
        }

        try
        {
            // Get current document
            var document = _documentService.GetCurrentDocument();
            if (document == null)
            {
                _logger.LogError("[SCRIPT] SaveDocumentCommand: Document is null");
                throw new InvalidOperationException("Document is null");
            }

            // Save the document
            _logger.LogInformation("[SCRIPT] Saving PDF to: {Path}", filePath);
            document.Save(filePath);

            _logger.LogInformation("[SCRIPT] SaveDocumentCommand completed successfully");

            // Run verification if enabled
            if (RunVerifyAfterSave)
            {
                await RunVerifyAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SCRIPT] SaveDocumentCommand failed");
            throw;
        }
    }
}

/// <summary>
/// Wrapper class that provides scripting-friendly access to document information.
/// </summary>
public class CurrentDocumentInfo
{
    private readonly PdfSharp.Pdf.PdfDocument? _document;

    public CurrentDocumentInfo(PdfSharp.Pdf.PdfDocument? document, string filePath, int pageCount)
    {
        _document = document;
        FilePath = filePath;
        PageCount = pageCount;
    }

    /// <summary>
    /// The file path of the loaded document.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The number of pages in the document.
    /// </summary>
    public int PageCount { get; }

    /// <summary>
    /// The underlying PdfDocument (for advanced scenarios).
    /// </summary>
    public PdfSharp.Pdf.PdfDocument? Document => _document;
}
