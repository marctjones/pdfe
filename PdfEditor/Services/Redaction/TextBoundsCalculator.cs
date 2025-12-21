using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Calculates bounding boxes for text operations in PDF content streams.
///
/// ================================================================================
/// COORDINATE CONVERSION DOCUMENTATION
/// ================================================================================
///
/// INPUT (from PDF content stream):
/// --------------------------------
/// - Text position from Text Matrix (Tm) and graphics CTM
/// - Coordinate system: PDF standard (origin at BOTTOM-LEFT, Y increases upward)
/// - Units: PDF points (72 per inch)
///
/// OUTPUT (for comparison with screen selection):
/// ----------------------------------------------
/// - Bounding box in Avalonia coordinate system
/// - Coordinate system: Top-left origin (Y increases downward)
/// - Units: PDF points (72 per inch)
///
/// CONVERSION FORMULA:
/// -------------------
/// avaloniaY = pageHeight - pdfY
///
/// EXAMPLE:
/// --------
/// Page height: 792 points (Letter size)
/// Text at PDF position (72, 720) = 72 points from left, 720 from bottom
/// Converted: (72, 792 - 720) = (72, 72) = 72 points from left, 72 from TOP
///
/// This matches the screen selection coordinates which are also in top-left origin.
///
/// WHY THIS MATTERS:
/// -----------------
/// If we don't convert, text at the TOP of the page (high PDF Y value) would have
/// a high Y in our bounding box, but screen selections at the TOP have LOW Y values.
/// The intersection test would fail even when the selection is over the text.
///
/// ================================================================================
/// </summary>
public class TextBoundsCalculator
{
    private readonly ILogger<TextBoundsCalculator> _logger;

    public TextBoundsCalculator(ILogger<TextBoundsCalculator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculate bounding box for text with given state.
    ///
    /// Returns bounds in Avalonia coordinates (top-left origin, PDF points).
    /// This allows direct comparison with screen selection areas.
    /// </summary>
    /// <param name="text">The text string</param>
    /// <param name="textState">Current PDF text state (font, matrix, etc.)</param>
    /// <param name="graphicsState">Current PDF graphics state (CTM, etc.)</param>
    /// <param name="pageHeight">Page height in PDF points (needed for Y-flip)</param>
    /// <returns>Bounding box in Avalonia coordinates (top-left origin)</returns>
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

        // In PDF text space, (0,0) is the baseline position
        // Text extends above baseline (ascent) and below baseline (descent)
        // Scale font metrics by fontSize / 1000 (PDF font units are per 1000)
        var ascent = fontMetrics.Ascent * textState.FontSize / 1000.0;
        var descent = fontMetrics.Descent * textState.FontSize / 1000.0;  // Descent is typically negative

        // Transform all four corners of the text rectangle in text space
        // In text space: Y=0 is baseline, positive Y is above, negative Y is below
        var corners = new[]
        {
            (0.0, descent),                // Bottom-left (below baseline)
            (textWidth, descent),          // Bottom-right (below baseline)
            (0.0, ascent),                 // Top-left (above baseline)
            (textWidth, ascent)            // Top-right (above baseline)
        };

        // Transform each corner through text matrix then graphics matrix
        var transformedCorners = new List<(double x, double y)>();
        foreach (var (cx, cy) in corners)
        {
            var (px, py) = textState.TextMatrix.Transform(cx, cy);
            var (tx, ty) = graphicsState.TransformationMatrix.Transform(px, py);
            transformedCorners.Add((tx, ty));
        }

        // Find bounding box of all transformed corners (still in PDF coordinates - bottom-left origin)
        var minX = transformedCorners.Min(c => c.x);
        var maxX = transformedCorners.Max(c => c.x);
        var minY = transformedCorners.Min(c => c.y);  // Bottom of text in PDF coords
        var maxY = transformedCorners.Max(c => c.y);  // Top of text in PDF coords

        // Convert from PDF coordinates (bottom-left origin) to Avalonia coordinates (top-left origin)
        // We have already transformed corners, so minY/maxY represent the actual bounding box
        // We can't use TextBoundsToPdfPointsTopLeft here because that assumes untransformed baseline
        // Instead, do direct Y-flip: PDF top (maxY) → Avalonia top
        var width = maxX - minX;
        var height = maxY - minY;
        var avaloniaY = pageHeight - maxY;  // maxY is the top in PDF coords

        var rect = new Rect(minX, avaloniaY, width, height);

        _logger.LogDebug(
            "Y coordinate conversion (direct Y-flip): " +
            "pageHeight={PageHeight:F2}, ascent={Ascent:F2}, descent={Descent:F2}, " +
            "pdfMinY={MinY:F2}, pdfMaxY={MaxY:F2} → " +
            "avaloniaY={AvaloniaY:F2}, avaloniaRect=({X:F2},{Y:F2},{W:F2}x{H:F2})",
            pageHeight, ascent, descent, minY, maxY, avaloniaY, rect.X, rect.Y, rect.Width, rect.Height);

        _logger.LogTrace(
            "Bounds calculated: ({X:F2},{Y:F2},{W:F2}x{H:F2}), " +
            "TextMatrix=(E={TmE:F2},F={TmF:F2}), Width={Width:F2}, Ascent={Ascent:F2}, Descent={Descent:F2}",
            rect.X, rect.Y, rect.Width, rect.Height,
            textState.TextMatrix.E, textState.TextMatrix.F, textWidth, ascent, descent);

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
