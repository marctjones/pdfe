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
    private PdfDocument? _currentDocument;
    private string? _currentFilePath;

    public int PageCount => _currentDocument?.PageCount ?? 0;
    public bool IsDocumentLoaded => _currentDocument != null;

    /// <summary>
    /// Load a PDF document from file
    /// </summary>
    public void LoadDocument(string filePath)
    {
        try
        {
            _currentDocument?.Dispose();
            _currentDocument = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify);
            _currentFilePath = filePath;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load PDF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Save the current document to a file
    /// </summary>
    public void SaveDocument(string? filePath = null)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        var savePath = filePath ?? _currentFilePath;
        if (string.IsNullOrEmpty(savePath))
            throw new ArgumentException("File path is required");

        _currentDocument.Save(savePath);
    }

    /// <summary>
    /// Remove a page from the document
    /// </summary>
    public void RemovePage(int pageIndex)
    {
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        if (pageIndex < 0 || pageIndex >= _currentDocument.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        _currentDocument.Pages.RemoveAt(pageIndex);
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
        if (_currentDocument == null)
            throw new InvalidOperationException("No document loaded");

        using var sourceDocument = PdfReader.Open(sourcePdfPath, PdfDocumentOpenMode.Import);
        
        var indices = pageIndices?.ToList() ?? Enumerable.Range(0, sourceDocument.PageCount).ToList();

        foreach (var index in indices)
        {
            if (index >= 0 && index < sourceDocument.PageCount)
            {
                _currentDocument.AddPage(sourceDocument.Pages[index]);
            }
        }
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
    /// Close the current document
    /// </summary>
    public void CloseDocument()
    {
        _currentDocument?.Dispose();
        _currentDocument = null;
        _currentFilePath = null;
    }
}
