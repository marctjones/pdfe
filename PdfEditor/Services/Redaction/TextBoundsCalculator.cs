using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using System;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Calculates bounding boxes for text operations
/// </summary>
public class TextBoundsCalculator
{
    private readonly ILogger<TextBoundsCalculator> _logger;

    public TextBoundsCalculator(ILogger<TextBoundsCalculator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate bounding box for text with given state
    /// </summary>
    public Rect CalculateBounds(string text, PdfTextState textState, PdfGraphicsState graphicsState, double pageHeight)
    {
        if (string.IsNullOrEmpty(text) || textState.FontSize <= 0)
        {
            _logger.LogTrace("Empty text or zero font size, returning empty rect");
            return new Rect();
        }

        _logger.LogTrace("Calculating bounds for text (length={Length}), fontSize={FontSize}",
            text.Length, textState.FontSize);

        // Get font metrics
        var fontMetrics = GetFontMetrics(textState.FontResource);

        // Calculate text width
        var textWidth = CalculateTextWidth(text, textState, fontMetrics);

        // Text height is approximately the font size
        var textHeight = textState.FontSize;

        // Get starting position from text matrix
        var (x, y) = textState.TextMatrix.Transform(0, 0);

        // Apply graphics transformation
        var (transformedX, transformedY) = graphicsState.TransformationMatrix.Transform(x, y);

        // Calculate end position
        var (endX, endY) = textState.TextMatrix.Transform(textWidth, textHeight);
        var (transformedEndX, transformedEndY) = graphicsState.TransformationMatrix.Transform(endX, endY);

        // Create bounding box
        var minX = Math.Min(transformedX, transformedEndX);
        var maxX = Math.Max(transformedX, transformedEndX);
        var minY = Math.Min(transformedY, transformedEndY);
        var maxY = Math.Max(transformedY, transformedEndY);

        // Convert from PDF coordinates (bottom-left) to Avalonia coordinates (top-left)
        var avaloniaY = pageHeight - maxY;

        var rect = new Rect(minX, avaloniaY, maxX - minX, maxY - minY);

        _logger.LogTrace(
            "Bounds calculated: ({X:F2},{Y:F2},{W:F2}x{H:F2}), " +
            "TextMatrix=({TmX:F2},{TmY:F2}), Width={Width:F2}, Height={Height:F2}",
            rect.X, rect.Y, rect.Width, rect.Height,
            x, y, textWidth, textHeight);

        return rect;
    }
    
    /// <summary>
    /// Calculate width of text considering character spacing, word spacing, and scaling
    /// </summary>
    private double CalculateTextWidth(string text, PdfTextState textState, FontMetrics fontMetrics)
    {
        var width = 0.0;
        var fontSize = textState.FontSize;
        var charSpacing = textState.CharacterSpacing;
        var wordSpacing = textState.WordSpacing;
        var horizontalScaling = textState.HorizontalScaling / 100.0;
        
        foreach (char c in text)
        {
            // Get character width from font
            var charWidth = GetCharacterWidth(c, fontMetrics, fontSize);
            
            // Add character spacing
            charWidth += charSpacing;
            
            // Add word spacing for spaces
            if (c == ' ')
                charWidth += wordSpacing;
            
            // Apply horizontal scaling
            charWidth *= horizontalScaling;
            
            width += charWidth;
        }
        
        return width;
    }
    
    /// <summary>
    /// Get width of a single character
    /// This is simplified - real implementation would read from font dictionary
    /// </summary>
    private double GetCharacterWidth(char c, FontMetrics fontMetrics, double fontSize)
    {
        // Use average character width as approximation
        // In a full implementation, you would:
        // 1. Map character to glyph index using font encoding
        // 2. Look up glyph width in font widths array
        // 3. Apply font scale factor
        
        return fontMetrics.AverageCharWidth * fontSize / 1000.0;
    }
    
    /// <summary>
    /// Extract font metrics from font dictionary
    /// This is simplified - real implementation would parse font dictionary
    /// </summary>
    private FontMetrics GetFontMetrics(PdfDictionary? fontDict)
    {
        if (fontDict == null)
            return FontMetrics.Default;
        
        try
        {
            // Try to read font metrics from font descriptor
            var fontDescriptor = fontDict.Elements.GetDictionary("/FontDescriptor");
            if (fontDescriptor != null)
            {
                var avgWidth = fontDescriptor.Elements.GetReal("/AvgWidth");
                var ascent = fontDescriptor.Elements.GetReal("/Ascent");
                var descent = fontDescriptor.Elements.GetReal("/Descent");
                
                if (avgWidth != 0)
                {
                    return new FontMetrics
                    {
                        AverageCharWidth = avgWidth,
                        Ascent = ascent,
                        Descent = descent
                    };
                }
            }
            
            // Fallback: Try to get from widths array
            var widths = fontDict.Elements.GetArray("/Widths");
            if (widths != null && widths.Elements.Count > 0)
            {
                var sum = 0.0;
                var count = 0;
                foreach (var item in widths.Elements)
                {
                    if (item is PdfInteger intItem)
                    {
                        sum += intItem.Value;
                        count++;
                    }
                    else if (item is PdfReal realItem)
                    {
                        sum += realItem.Value;
                        count++;
                    }
                }

                if (count > 0)
                {
                    return new FontMetrics
                    {
                        AverageCharWidth = sum / count
                    };
                }
            }
        }
        catch
        {
            // Fall through to default
        }
        
        return FontMetrics.Default;
    }
}

/// <summary>
/// Font metrics for text width calculation
/// </summary>
public class FontMetrics
{
    public double AverageCharWidth { get; set; } = 600; // Default for standard fonts
    public double Ascent { get; set; } = 750;
    public double Descent { get; set; } = -250;
    public double CapHeight { get; set; } = 700;
    
    public static FontMetrics Default => new FontMetrics();
}
