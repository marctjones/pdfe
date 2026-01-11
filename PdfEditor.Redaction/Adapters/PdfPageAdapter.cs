using Pdfe.Core.Document;
using Pdfe.Core.Text;
using CorePdfRectangle = Pdfe.Core.Document.PdfRectangle;

namespace PdfEditor.Redaction.Adapters;

/// <summary>
/// Adapter to provide a unified PDF page API during migration.
/// </summary>
public class PdfPageAdapter
{
    private readonly PdfPage _page;

    /// <summary>
    /// The underlying Pdfe.Core page.
    /// </summary>
    public PdfPage Page => _page;

    /// <summary>
    /// 1-based page number.
    /// </summary>
    public int PageNumber => _page.PageNumber;

    /// <summary>
    /// Page width in points.
    /// </summary>
    public double Width => _page.Width;

    /// <summary>
    /// Page height in points.
    /// </summary>
    public double Height => _page.Height;

    /// <summary>
    /// Page rotation in degrees.
    /// </summary>
    public int Rotation => _page.Rotation;

    /// <summary>
    /// The media box (as Pdfe.Core type).
    /// </summary>
    public CorePdfRectangle MediaBox => _page.MediaBox;

    /// <summary>
    /// The crop box (as Pdfe.Core type).
    /// </summary>
    public CorePdfRectangle CropBox => _page.CropBox;

    /// <summary>
    /// The media box converted to PdfEditor.Redaction type.
    /// </summary>
    public PdfRectangle MediaBoxAsRedaction => ToRedactionRect(_page.MediaBox);

    /// <summary>
    /// The crop box converted to PdfEditor.Redaction type.
    /// </summary>
    public PdfRectangle CropBoxAsRedaction => ToRedactionRect(_page.CropBox);

    private static PdfRectangle ToRedactionRect(CorePdfRectangle r) =>
        new(r.Left, r.Bottom, r.Right, r.Top);

    internal PdfPageAdapter(PdfPage page)
    {
        _page = page;
    }

    /// <summary>
    /// Get extracted text from the page.
    /// </summary>
    public string Text => _page.Text;

    /// <summary>
    /// Get all letters with position information.
    /// </summary>
    public IReadOnlyList<Letter> Letters => _page.Letters;

    /// <summary>
    /// Get letters as LetterAdapter for compatibility with existing code.
    /// </summary>
    public IReadOnlyList<LetterAdapter> GetLettersAdapted()
    {
        return _page.Letters.Select(l => new LetterAdapter(l)).ToList();
    }

    /// <summary>
    /// Get the raw content stream bytes (decoded).
    /// </summary>
    public byte[] GetContentStreamBytes()
    {
        return _page.GetContentStreamBytes();
    }

    /// <summary>
    /// Set the content stream bytes.
    /// </summary>
    public void SetContentStreamBytes(byte[] data)
    {
        _page.SetContentStreamBytes(data);
    }

    /// <summary>
    /// Get the content stream as a string.
    /// </summary>
    public string GetContentStreamText()
    {
        var bytes = GetContentStreamBytes();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
