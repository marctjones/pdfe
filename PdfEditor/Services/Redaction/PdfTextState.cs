using PdfSharp.Pdf;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Represents the text state in a PDF content stream
/// Tracks font, size, positioning, spacing, etc.
/// </summary>
public class PdfTextState
{
    public string? FontName { get; set; }
    public double FontSize { get; set; } = 12;
    public double CharacterSpacing { get; set; } = 0;
    public double WordSpacing { get; set; } = 0;
    public double HorizontalScaling { get; set; } = 100.0;
    public double Leading { get; set; } = 0;
    public double Rise { get; set; } = 0;
    public int RenderingMode { get; set; } = 0; // 0 = fill text
    
    // Text matrix (position and transformation)
    public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;
    
    // Text line matrix (for line positioning)
    public PdfMatrix TextLineMatrix { get; set; } = PdfMatrix.Identity;
    
    // Font resource (if available)
    public PdfDictionary? FontResource { get; set; }
    
    /// <summary>
    /// Clone the current text state
    /// </summary>
    public PdfTextState Clone()
    {
        return new PdfTextState
        {
            FontName = FontName,
            FontSize = FontSize,
            CharacterSpacing = CharacterSpacing,
            WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling,
            Leading = Leading,
            Rise = Rise,
            RenderingMode = RenderingMode,
            TextMatrix = TextMatrix.Clone(),
            TextLineMatrix = TextLineMatrix.Clone(),
            FontResource = FontResource
        };
    }
    
    /// <summary>
    /// Reset text matrix and line matrix to identity
    /// </summary>
    public void ResetMatrices()
    {
        TextMatrix = PdfMatrix.Identity;
        TextLineMatrix = PdfMatrix.Identity;
    }
    
    /// <summary>
    /// Move text position by (tx, ty)
    /// Updates both text matrix and line matrix
    /// </summary>
    public void TranslateText(double tx, double ty)
    {
        var translation = PdfMatrix.CreateTranslation(tx, ty);
        TextLineMatrix = TextLineMatrix.Multiply(translation);
        TextMatrix = TextLineMatrix.Clone();
    }
    
    /// <summary>
    /// Set text matrix directly
    /// </summary>
    public void SetTextMatrix(PdfMatrix matrix)
    {
        TextMatrix = matrix.Clone();
        TextLineMatrix = matrix.Clone();
    }
    
    /// <summary>
    /// Move to start of next line (using Leading)
    /// </summary>
    public void MoveToNextLine()
    {
        TranslateText(0, -Leading);
    }
}
