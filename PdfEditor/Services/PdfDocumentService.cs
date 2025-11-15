using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfEditor.Services;

/// <summary>
/// Service for PDF document manipulation (page removal, addition, merging)
/// Uses PdfSharpCore (MIT License)
/// </summary>
public class PdfDocumentService
{
    private readonly ILogger<PdfDocumentService> _logger;
    private PdfDocument? _currentDocument;
    private string? _currentFilePath;

    public int PageCount => _currentDocument?.PageCount ?? 0;
    public bool IsDocumentLoaded => _currentDocument != null;

    public PdfDocumentService(ILogger<PdfDocumentService> logger)
    {
        _logger = logger;
        _logger.LogDebug("PdfDocumentService instance created");
    }

    /// <summary>
    /// Load a PDF document from file
    /// </summary>
    public void LoadDocument(string filePath)
    {
        _logger.LogInformation("Loading PDF document from: {FilePath}", filePath);

        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("File not found: {FilePath}", filePath);
                throw new FileNotFoundException($"PDF file not found: {filePath}");
            }

            _logger.LogDebug("Disposing previous document if exists");
            _currentDocument?.Dispose();

            _logger.LogDebug("Opening PDF in Modify mode");
            _currentDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify);
            _currentFilePath = filePath;

            _logger.LogInformation("PDF loaded successfully. Pages: {PageCount}, File: {FileName}",
                PageCount, Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load PDF from {FilePath}", filePath);
            throw new Exception($"Failed to load PDF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Save the current document to a file
    /// </summary>
    public void SaveDocument(string? filePath = null)
    {
        _logger.LogInformation("Saving PDF document");

        if (_currentDocument == null)
        {
            _logger.LogError("Cannot save: No document loaded");
            throw new InvalidOperationException("No document loaded");
        }

        var savePath = filePath ?? _currentFilePath;
        if (string.IsNullOrEmpty(savePath))
        {
            _logger.LogError("Cannot save: No file path specified");
            throw new ArgumentException("File path is required");
        }

        _logger.LogDebug("Saving to: {FilePath}, Pages: {PageCount}", savePath, PageCount);

        try
        {
            _currentDocument.Save(savePath);
            _logger.LogInformation("PDF saved successfully to: {FilePath}", savePath);

            // After saving, PdfSharp marks the document as read-only
            // We need to reload it to allow further modifications
            _logger.LogDebug("Reloading document after save to allow further modifications");
            _currentDocument.Dispose();
            _currentDocument = PdfReader.Open(savePath, PdfDocumentOpenMode.Modify);
            _currentFilePath = savePath;
            _logger.LogDebug("Document reloaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save PDF to {FilePath}", savePath);
            throw;
        }
    }

    /// <summary>
    /// Remove a page from the document
    /// </summary>
    public void RemovePage(int pageIndex)
    {
        _logger.LogInformation("Removing page {PageIndex}", pageIndex);

        if (_currentDocument == null)
        {
            _logger.LogError("Cannot remove page: No document loaded");
            throw new InvalidOperationException("No document loaded");
        }

        if (pageIndex < 0 || pageIndex >= _currentDocument.PageCount)
        {
            _logger.LogError("Invalid page index: {PageIndex}, Total pages: {PageCount}",
                pageIndex, _currentDocument.PageCount);
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        _currentDocument.Pages.RemoveAt(pageIndex);
        _logger.LogInformation("Page {PageIndex} removed successfully. Remaining pages: {PageCount}",
            pageIndex, PageCount);
    }

    /// <summary>
    /// Remove multiple pages from the document
    /// </summary>
    public void RemovePages(IEnumerable<int> pageIndices)
    {
        // Remove in descending order to avoid index shifting issues
        foreach (var index in pageIndices.OrderByDescending(i => i))
        {
            RemovePage(index);
        }
    }

    /// <summary>
    /// Add pages from another PDF document
    /// </summary>
    public void AddPagesFromPdf(string sourcePdfPath, IEnumerable<int>? pageIndices = null)
    {
        _logger.LogInformation("Adding pages from source PDF: {SourcePath}", sourcePdfPath);

        if (_currentDocument == null)
        {
            _logger.LogError("Cannot add pages: No document loaded");
            throw new InvalidOperationException("No document loaded");
        }

        using var sourceDocument = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Import);
        _logger.LogDebug("Source PDF opened. Pages: {SourcePageCount}", sourceDocument.PageCount);

        var indices = pageIndices?.ToList() ?? Enumerable.Range(0, sourceDocument.PageCount).ToList();
        _logger.LogDebug("Will add {PageCount} pages", indices.Count);

        int addedCount = 0;
        foreach (var index in indices)
        {
            if (index >= 0 && index < sourceDocument.PageCount)
            {
                _currentDocument.AddPage(sourceDocument.Pages[index]);
                addedCount++;
                _logger.LogDebug("Added page {PageIndex} from source", index);
            }
            else
            {
                _logger.LogWarning("Skipped invalid page index: {PageIndex}", index);
            }
        }

        _logger.LogInformation("Added {AddedCount} pages. Total pages now: {TotalPages}",
            addedCount, PageCount);
    }

    /// <summary>
    /// Insert pages from another PDF at a specific position
    /// </summary>
    public void InsertPagesFromPdf(string sourcePdfPath, int insertAtIndex, IEnumerable<int>? pageIndices = null)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        using var sourceDocument = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Import);
        
        var indices = pageIndices?.ToList() ?? Enumerable.Range(0, sourceDocument.PageCount).ToList();
        var currentInsertIndex = insertAtIndex;

        foreach (var index in indices)
        {
            if (index >= 0 && index < sourceDocument.PageCount)
            {
                _currentDocument.Pages.Insert(currentInsertIndex, sourceDocument.Pages[index]);
                currentInsertIndex++;
            }
        }
    }

    /// <summary>
    /// Get the current document (for advanced operations)
    /// </summary>
    public PdfDocument? GetCurrentDocument() => _currentDocument;

    /// <summary>
    /// Get the current document as a memory stream (for rendering without saving to disk)
    /// </summary>
    public MemoryStream? GetCurrentDocumentAsStream()
    {
        if (_currentDocument == null)
            return null;

        try
        {
            var stream = new MemoryStream();
            _currentDocument.Save(stream, false); // false = don't close the stream
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save document to memory stream");
            return null;
        }
    }

    /// <summary>
    /// Close the current document
    /// </summary>
    public void CloseDocument()
    {
        _logger.LogInformation("Closing document");

        if (_currentDocument != null)
        {
            _logger.LogDebug("Disposing document: {FileName}", Path.GetFileName(_currentFilePath ?? "unknown"));
            _currentDocument.Dispose();
        }

        _currentDocument = null;
        _currentFilePath = null;

        _logger.LogInformation("Document closed successfully");
    }
}
