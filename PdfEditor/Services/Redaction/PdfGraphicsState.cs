using System;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Represents the graphics state in a PDF content stream
/// Tracks transformations, colors, line width, etc.
/// </summary>
public class PdfGraphicsState
{
    public PdfMatrix TransformationMatrix { get; set; } = PdfMatrix.Identity;
    public double LineWidth { get; set; } = 1.0;
    public PdfColor StrokeColor { get; set; } = PdfColor.Black;
    public PdfColor FillColor { get; set; } = PdfColor.Black;
    public double[] LineDashPattern { get; set; } = Array.Empty<double>();
    public int LineDashPhase { get; set; } = 0;
    
    /// <summary>
    /// Clone the current state (for save/restore operations)
    /// </summary>
    public PdfGraphicsState Clone()
    {
        return new PdfGraphicsState
        {
            TransformationMatrix = TransformationMatrix.Clone(),
            LineWidth = LineWidth,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            LineDashPattern = (double[])LineDashPattern.Clone(),
            LineDashPhase = LineDashPhase
        };
    }
}

/// <summary>
/// Represents a transformation matrix in PDF
/// </summary>
public class PdfMatrix
{
    public double A { get; set; } = 1;
    public double B { get; set; } = 0;
    public double C { get; set; } = 0;
    public double D { get; set; } = 1;
    public double E { get; set; } = 0;
    public double F { get; set; } = 0;
    
    public static PdfMatrix Identity => new PdfMatrix();
    
    /// <summary>
    /// Create matrix from array [a b c d e f]
    /// </summary>
    public static PdfMatrix FromArray(double[] values)
    {
        if (values.Length != 6)
            throw new ArgumentException("Matrix array must have 6 elements");
            
        return new PdfMatrix
        {
            A = values[0],
            B = values[1],
            C = values[2],
            D = values[3],
            E = values[4],
            F = values[5]
        };
    }
    
    /// <summary>
    /// Multiply this matrix by another (this * other)
    /// </summary>
    public PdfMatrix Multiply(PdfMatrix other)
    {
        return new PdfMatrix
        {
            A = A * other.A + B * other.C,
            B = A * other.B + B * other.D,
            C = C * other.A + D * other.C,
            D = C * other.B + D * other.D,
            E = E * other.A + F * other.C + other.E,
            F = E * other.B + F * other.D + other.F
        };
    }
    
    /// <summary>
    /// Transform a point using this matrix
    /// </summary>
    public (double x, double y) Transform(double x, double y)
    {
        return (
            A * x + C * y + E,
            B * x + D * y + F
        );
    }
    
    /// <summary>
    /// Create a translation matrix
    /// </summary>
    public static PdfMatrix CreateTranslation(double tx, double ty)
    {
        return new PdfMatrix { E = tx, F = ty };
    }
    
    /// <summary>
    /// Create a scaling matrix
    /// </summary>
    public static PdfMatrix CreateScale(double sx, double sy)
    {
        return new PdfMatrix { A = sx, D = sy };
    }
    
    public PdfMatrix Clone()
    {
        return new PdfMatrix
        {
            A = A, B = B, C = C, D = D, E = E, F = F
        };
    }
}

/// <summary>
/// Represents a color in PDF
/// </summary>
public class PdfColor
{
    public double[] Components { get; set; }
    public ColorSpace Space { get; set; }
    
    public static PdfColor Black => new PdfColor 
    { 
        Components = new[] { 0.0 }, 
        Space = ColorSpace.Gray 
    };
    
    public PdfColor()
    {
        Components = new[] { 0.0 };
        Space = ColorSpace.Gray;
    }
}

public enum ColorSpace
{
    Gray,
    RGB,
    CMYK
}
