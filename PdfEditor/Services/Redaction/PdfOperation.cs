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
    /// Check if this operation intersects with a given area
    /// </summary>
    public virtual bool IntersectsWith(Rect area)
    {
        if (BoundingBox.Width <= 0 || BoundingBox.Height <= 0)
            return false;
            
        return BoundingBox.Intersects(area);
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
