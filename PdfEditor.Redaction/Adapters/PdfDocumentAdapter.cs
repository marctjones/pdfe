using Pdfe.Core.Document;

namespace PdfEditor.Redaction.Adapters;

/// <summary>
/// Adapter to provide a unified PDF document API during migration.
/// This allows gradual migration from PdfPig/PDFsharp to Pdfe.Core.
/// </summary>
public class PdfDocumentAdapter : IDisposable
{
    private readonly PdfDocument _document;
    private readonly string? _filePath;
    private bool _disposed;

    /// <summary>
    /// The underlying Pdfe.Core document.
    /// </summary>
    public PdfDocument Document => _document;

    /// <summary>
    /// Number of pages in the document.
    /// </summary>
    public int PageCount => _document.PageCount;

    /// <summary>
    /// PDF version string (e.g., "1.4", "2.0").
    /// </summary>
    public string Version => _document.Version;

    /// <summary>
    /// Whether the document is encrypted.
    /// </summary>
    public bool IsEncrypted => _document.IsEncrypted;

    /// <summary>
    /// The file path if opened from a file.
    /// </summary>
    public string? FilePath => _filePath;

    private PdfDocumentAdapter(PdfDocument document, string? filePath = null)
    {
        _document = document;
        _filePath = filePath;
    }

    /// <summary>
    /// Open a PDF from a file path.
    /// </summary>
    public static PdfDocumentAdapter Open(string path)
    {
        var doc = PdfDocument.Open(path);
        return new PdfDocumentAdapter(doc, path);
    }

    /// <summary>
    /// Open a PDF from a byte array.
    /// </summary>
    public static PdfDocumentAdapter Open(byte[] data)
    {
        var doc = PdfDocument.Open(data);
        return new PdfDocumentAdapter(doc);
    }

    /// <summary>
    /// Open a PDF from a stream.
    /// </summary>
    public static PdfDocumentAdapter Open(Stream stream)
    {
        var doc = PdfDocument.Open(stream);
        return new PdfDocumentAdapter(doc);
    }

    /// <summary>
    /// Get a page by 1-based page number.
    /// </summary>
    public PdfPageAdapter GetPage(int pageNumber)
    {
        var page = _document.GetPage(pageNumber);
        return new PdfPageAdapter(page);
    }

    /// <summary>
    /// Save the document to the original file path.
    /// </summary>
    public void Save()
    {
        if (_filePath == null)
            throw new InvalidOperationException("Document was not opened from a file. Use Save(path) instead.");

        _document.Save(_filePath);
    }

    /// <summary>
    /// Save the document to a new path.
    /// </summary>
    public void Save(string path)
    {
        _document.Save(path);
    }

    /// <summary>
    /// Save the document to a stream.
    /// </summary>
    public void Save(Stream stream)
    {
        _document.Save(stream);
    }

    /// <summary>
    /// Save the document to a byte array.
    /// </summary>
    public byte[] SaveToBytes()
    {
        using var ms = new MemoryStream();
        _document.Save(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _document.Dispose();
            _disposed = true;
        }
    }
}
