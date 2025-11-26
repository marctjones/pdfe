using Avalonia;
using PdfSharp.Pdf.Content.Objects;
using System.Collections.Generic;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Base class for PDF operations found in content streams
/// </summary>
public abstract class PdfOperation
{
    public CObject OriginalObject { get; set; }
    public Rect BoundingBox { get; set; }
    
    protected PdfOperation(CObject obj)
    {
        OriginalObject = obj;
        BoundingBox = new Rect();
    }
    
    /// <summary>
    /// Check if this operation intersects with a given area.
    /// Uses HYBRID intersection for precision:
    /// - Y-axis: Center-point OR significant overlap (>30% of height)
    ///   This prevents adjacent lines from being caught while still catching
    ///   text that significantly overlaps the selection area.
    /// - X-axis: Any-overlap based (catches all content on the same line)
    /// </summary>
    public virtual bool IntersectsWith(Rect area)
    {
        if (BoundingBox.Width <= 0 || BoundingBox.Height <= 0)
            return false;

        // For Y-axis: Use center-point OR significant overlap check
        // This prevents adjacent lines from being caught (which would have tiny overlap)
        // while still catching text that significantly overlaps the selection
        var centerY = BoundingBox.Y + BoundingBox.Height / 2.0;
        var centerInSelection = centerY >= area.Y && centerY <= area.Y + area.Height;

        // Check for significant overlap (more than 20% of text height)
        // This handles edge cases where selection is slightly below/above text center
        // 20% threshold chosen to:
        // - Allow text that meaningfully overlaps the selection (not just touching)
        // - Work with larger fonts where overlap percentage is smaller
        // - Prevent adjacent lines that barely touch from being caught
        var overlapTop = Math.Max(BoundingBox.Y, area.Y);
        var overlapBottom = Math.Min(BoundingBox.Y + BoundingBox.Height, area.Y + area.Height);
        var overlapAmount = Math.Max(0, overlapBottom - overlapTop);
        var hasSignificantOverlap = BoundingBox.Height > 0 &&
                                     overlapAmount >= BoundingBox.Height * 0.2;

        var yIntersects = centerInSelection || hasSignificantOverlap;

        // For X-axis: Use actual overlap (not just touching) to catch content on the same line
        // Using <= and >= ensures rectangles that just touch at an edge don't count as intersecting
        var xIntersects = !(BoundingBox.Right <= area.X || BoundingBox.X >= area.Right);

        return xIntersects && yIntersects;
    }
    
    /// <summary>
    /// Check if this operation is completely contained within an area
    /// </summary>
    public virtual bool IsContainedIn(Rect area)
    {
        return area.Contains(BoundingBox);
    }
}

/// <summary>
/// Represents a text-showing operation (Tj, TJ, ', ")
/// </summary>
public class TextOperation : PdfOperation
{
    public string Text { get; set; }
    public Point Position { get; set; }
    public double FontSize { get; set; }
    public string? FontName { get; set; }
    public PdfTextState TextState { get; set; }
    public PdfGraphicsState GraphicsState { get; set; }
    
    public TextOperation(CObject obj) : base(obj)
    {
        Text = string.Empty;
        Position = new Point();
        TextState = new PdfTextState();
        GraphicsState = new PdfGraphicsState();
    }
}

/// <summary>
/// Represents a path construction/painting operation
/// </summary>
public class PathOperation : PdfOperation
{
    public List<Point> Points { get; set; }
    public PathType Type { get; set; }
    public bool IsStroke { get; set; }
    public bool IsFill { get; set; }
    
    public PathOperation(CObject obj) : base(obj)
    {
        Points = new List<Point>();
        Type = PathType.Unknown;
    }
}

public enum PathType
{
    Unknown,
    MoveTo,      // m
    LineTo,      // l
    CurveTo,     // c, v, y
    Rectangle,   // re
    ClosePath,   // h
    Stroke,      // S, s
    Fill,        // f, F, f*
    FillStroke,  // B, B*, b, b*
    Clip         // W, W*
}

/// <summary>
/// Represents an image operation (Do)
/// </summary>
public class ImageOperation : PdfOperation
{
    public string ResourceName { get; set; }
    public Point Position { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    
    public ImageOperation(CObject obj) : base(obj)
    {
        ResourceName = string.Empty;
        Position = new Point();
    }
}

/// <summary>
/// Represents a graphics state operation (q, Q, cm, etc.)
/// </summary>
public class StateOperation : PdfOperation
{
    public StateOperationType Type { get; set; }
    public PdfMatrix? Matrix { get; set; }
    
    public StateOperation(CObject obj) : base(obj)
    {
        Type = StateOperationType.Other;
    }
    
    // State operations don't have bounding boxes
    public override bool IntersectsWith(Rect area) => false;
}

public enum StateOperationType
{
    SaveState,      // q
    RestoreState,   // Q
    Transform,      // cm
    Other
}

/// <summary>
/// Represents a text state operation (BT, ET, Tf, Td, etc.)
/// </summary>
public class TextStateOperation : PdfOperation
{
    public TextStateOperationType Type { get; set; }
    
    public TextStateOperation(CObject obj) : base(obj)
    {
        Type = TextStateOperationType.Other;
    }
    
    // Text state operations don't have bounding boxes
    public override bool IntersectsWith(Rect area) => false;
}

public enum TextStateOperationType
{
    BeginText,      // BT
    EndText,        // ET
    SetFont,        // Tf
    MoveText,       // Td, TD, T*
    SetMatrix,      // Tm
    SetLeading,     // TL
    SetSpacing,     // Tc, Tw
    SetScale,       // Tz
    SetRise,        // Ts
    SetRenderMode,  // Tr
    Other
}

/// <summary>
/// Represents any other operation we want to preserve
/// </summary>
public class GenericOperation : PdfOperation
{
    public string OperatorName { get; set; }

    public GenericOperation(CObject obj, string opName) : base(obj)
    {
        OperatorName = opName;
    }

    // Generic operations are preserved by default
    public override bool IntersectsWith(Rect area) => false;
}

/// <summary>
/// Represents an inline image operation (BI...ID...EI sequence)
/// </summary>
public class InlineImageOperation : PdfOperation
{
    /// <summary>
    /// Raw bytes of the complete BI...ID...EI sequence
    /// </summary>
    public byte[] RawData { get; set; }

    /// <summary>
    /// Position in the original content stream
    /// </summary>
    public int StreamPosition { get; set; }

    /// <summary>
    /// Length of the entire inline image sequence
    /// </summary>
    public int StreamLength { get; set; }

    /// <summary>
    /// Image width from inline image dictionary
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// Image height from inline image dictionary
    /// </summary>
    public int ImageHeight { get; set; }

    public InlineImageOperation(CObject obj) : base(obj)
    {
        RawData = Array.Empty<byte>();
    }

    /// <summary>
    /// Constructor for inline images that don't have a CObject representation
    /// </summary>
    public InlineImageOperation(byte[] rawData, Rect bounds, int position, int length)
        : base(new PdfSharp.Pdf.Content.Objects.CComment())  // Use a placeholder CObject
    {
        RawData = rawData;
        BoundingBox = bounds;
        StreamPosition = position;
        StreamLength = length;
    }
}
