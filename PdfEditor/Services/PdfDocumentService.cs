using Microsoft.Extensions.Logging;
using Pdfe.Core.Document;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfEditor.Services;

/// <summary>
/// Owns the currently-loaded <see cref="PdfDocument"/> and exposes
/// load / save / page-manipulation / rotation operations to the GUI.
/// Pure Pdfe.Core — no other PDF library.
/// </summary>
public class PdfDocumentService
{
    private readonly ILogger<PdfDocumentService> _logger;
    private PdfDocument? _currentDocument;
    private string? _currentFilePath;

    public int PageCount => _currentDocument?.PageCount ?? 0;
    public bool IsDocumentLoaded => _currentDocument != null;

    /// <summary>
    /// Current document's declared PDF version (e.g. "1.7"). Empty when
    /// no document is loaded.
    /// </summary>
    public string PdfVersion => _currentDocument?.Version ?? string.Empty;

    /// <summary>Width of page <paramref name="pageIndex"/> in points. Falls back to Letter.</summary>
    public double GetPageWidth(int pageIndex)
    {
        if (_currentDocument == null || pageIndex < 0 || pageIndex >= PageCount)
            return 612;
        return _currentDocument.GetPage(pageIndex + 1).Width;
    }

    /// <summary>Height of page <paramref name="pageIndex"/> in points. Falls back to Letter.</summary>
    public double GetPageHeight(int pageIndex)
    {
        if (_currentDocument == null || pageIndex < 0 || pageIndex >= PageCount)
            return 792;
        return _currentDocument.GetPage(pageIndex + 1).Height;
    }

    public PdfDocumentService(ILogger<PdfDocumentService> logger)
    {
        _logger = logger;
    }

    /// <summary>Load a PDF from disk. Replaces any previously-loaded document.</summary>
    public void LoadDocument(string filePath)
    {
        _logger.LogInformation("Loading PDF document from: {FilePath}", filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        _currentDocument?.Dispose();
        // Open from bytes so the file is not held open — matches the
        // previous file-based behavior that kept the file freely writable.
        _currentDocument = PdfDocument.Open(File.ReadAllBytes(filePath));
        _currentFilePath = filePath;

        _logger.LogInformation(
            "PDF loaded. Pages: {PageCount}, Version: {Version}, File: {FileName}",
            PageCount, PdfVersion, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Save the current document. If <paramref name="filePath"/> is null,
    /// saves back to the file the document was loaded from.
    /// </summary>
    public void SaveDocument(string? filePath = null)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        var savePath = filePath ?? _currentFilePath
            ?? throw new ArgumentException("File path is required");

        _currentDocument.Save(savePath);
        _logger.LogInformation("PDF saved to: {FilePath}", savePath);

        // Reload to reset in-memory state from the persisted bytes.
        _currentDocument.Dispose();
        _currentDocument = PdfDocument.Open(File.ReadAllBytes(savePath));
        _currentFilePath = savePath;
    }

    /// <summary>Remove a single page by 0-based index.</summary>
    public void RemovePage(int pageIndex)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        _currentDocument.Pages.RemoveAt(pageIndex);
        _logger.LogInformation("Page {PageIndex} removed; remaining: {Count}", pageIndex, PageCount);
    }

    /// <summary>Remove multiple pages by 0-based indices (in any order).</summary>
    public void RemovePages(IEnumerable<int> pageIndices)
    {
        foreach (var index in pageIndices.OrderByDescending(i => i))
            RemovePage(index);
    }

    /// <summary>
    /// Append pages from another PDF file to the end of the current
    /// document. If <paramref name="pageIndices"/> is null, all pages
    /// from the source are appended.
    /// </summary>
    public void AddPagesFromPdf(string sourcePdfPath, IEnumerable<int>? pageIndices = null)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        using var sourceDocument = PdfDocument.Open(File.ReadAllBytes(sourcePdfPath));
        var indices = pageIndices?.ToList() ?? Enumerable.Range(0, sourceDocument.PageCount).ToList();

        foreach (var index in indices)
        {
            if (index < 0 || index >= sourceDocument.PageCount) continue;
            _currentDocument.Pages.Add(sourceDocument.GetPage(index + 1));
        }

        _logger.LogInformation("Added {Count} page(s); total now: {Total}", indices.Count, PageCount);
    }

    /// <summary>Insert pages from another PDF at a specific 0-based position.</summary>
    public void InsertPagesFromPdf(string sourcePdfPath, int insertAtIndex, IEnumerable<int>? pageIndices = null)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        using var sourceDocument = PdfDocument.Open(File.ReadAllBytes(sourcePdfPath));
        var indices = pageIndices?.ToList() ?? Enumerable.Range(0, sourceDocument.PageCount).ToList();

        var cursor = insertAtIndex;
        foreach (var index in indices)
        {
            if (index < 0 || index >= sourceDocument.PageCount) continue;
            _currentDocument.Pages.Insert(cursor, sourceDocument.GetPage(index + 1));
            cursor++;
        }
    }

    /// <summary>Get the current document for advanced operations. Null when unloaded.</summary>
    public PdfDocument? GetCurrentDocument() => _currentDocument;

    /// <summary>
    /// Serialize the current document to a fresh <see cref="MemoryStream"/>
    /// for in-memory rendering.
    /// </summary>
    public MemoryStream? GetCurrentDocumentAsStream()
    {
        if (_currentDocument == null) return null;
        var ms = new MemoryStream(_currentDocument.SaveToBytes()) { Position = 0 };
        return ms;
    }

    /// <summary>
    /// Rotate a page by the given number of degrees (added to the
    /// existing rotation). Must be a multiple of 90.
    /// </summary>
    public void RotatePage(int pageIndex, int degrees)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        degrees = ((degrees % 360) + 360) % 360;
        if (degrees != 0 && degrees != 90 && degrees != 180 && degrees != 270)
            throw new ArgumentException("Rotation must be 0, 90, 180, or 270 degrees", nameof(degrees));

        var page = _currentDocument.GetPage(pageIndex + 1);
        page.Rotation = (page.Rotation + degrees) % 360;
    }

    public void RotatePageRight(int pageIndex) => RotatePage(pageIndex, 90);
    public void RotatePageLeft(int pageIndex) => RotatePage(pageIndex, 270);
    public void RotatePage180(int pageIndex) => RotatePage(pageIndex, 180);

    /// <summary>Dispose the current document and clear state.</summary>
    public void CloseDocument()
    {
        _currentDocument?.Dispose();
        _currentDocument = null;
        _currentFilePath = null;
    }
}
