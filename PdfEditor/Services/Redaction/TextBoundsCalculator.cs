using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // Transform all four corners of the text rectangle in text space
        // This properly handles rotation and scaling in the text matrix
        var corners = new[]
        {
            (0.0, 0.0),                    // Bottom-left
            (textWidth, 0.0),              // Bottom-right
            (0.0, textHeight),             // Top-left
            (textWidth, textHeight)        // Top-right
        };

        // Transform each corner through text matrix then graphics matrix
        var transformedCorners = new List<(double x, double y)>();
        foreach (var (cx, cy) in corners)
        {
            var (px, py) = textState.TextMatrix.Transform(cx, cy);
            var (tx, ty) = graphicsState.TransformationMatrix.Transform(px, py);
            transformedCorners.Add((tx, ty));
        }

        // Find bounding box of all transformed corners
        var minX = transformedCorners.Min(c => c.x);
        var maxX = transformedCorners.Max(c => c.x);
        var minY = transformedCorners.Min(c => c.y);
        var maxY = transformedCorners.Max(c => c.y);

        // Convert from PDF coordinates (bottom-left) to Avalonia coordinates (top-left)
        var avaloniaY = pageHeight - maxY;

        var rect = new Rect(minX, avaloniaY, maxX - minX, maxY - minY);

        _logger.LogTrace(
            "Bounds calculated: ({X:F2},{Y:F2},{W:F2}x{H:F2}), " +
            "TextMatrix=(E={TmE:F2},F={TmF:F2}), Width={Width:F2}, Height={Height:F2}",
            rect.X, rect.Y, rect.Width, rect.Height,
            textState.TextMatrix.E, textState.TextMatrix.F, textWidth, textHeight);

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
