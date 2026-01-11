using System.Globalization;
using System.Text;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Graphics;

/// <summary>
/// Provides graphics drawing operations for a PDF page.
/// Generates PDF content stream operators for drawing shapes, text, and images.
/// </summary>
public class PdfGraphics : IDisposable
{
    private readonly PdfPage _page;
    private readonly StringBuilder _operators;
    private bool _disposed;

    /// <summary>
    /// Creates a graphics context for the specified page.
    /// </summary>
    internal PdfGraphics(PdfPage page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _operators = new StringBuilder();
    }

    #region State Management

    /// <summary>
    /// Saves the current graphics state (q operator).
    /// </summary>
    public void SaveState()
    {
        ThrowIfDisposed();
        _operators.AppendLine("q");
    }

    /// <summary>
    /// Restores the previous graphics state (Q operator).
    /// </summary>
    public void RestoreState()
    {
        ThrowIfDisposed();
        _operators.AppendLine("Q");
    }

    #endregion

    #region Transformations

    /// <summary>
    /// Translates the coordinate system.
    /// </summary>
    public void Translate(double tx, double ty)
    {
        ThrowIfDisposed();
        // Translation matrix: [1 0 0 1 tx ty]
        _operators.AppendLine($"1 0 0 1 {Fmt(tx)} {Fmt(ty)} cm");
    }

    /// <summary>
    /// Scales the coordinate system.
    /// </summary>
    public void Scale(double sx, double sy)
    {
        ThrowIfDisposed();
        // Scale matrix: [sx 0 0 sy 0 0]
        _operators.AppendLine($"{Fmt(sx)} 0 0 {Fmt(sy)} 0 0 cm");
    }

    /// <summary>
    /// Rotates the coordinate system by the specified angle in degrees.
    /// </summary>
    public void Rotate(double degrees)
    {
        ThrowIfDisposed();
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        // Rotation matrix: [cos sin -sin cos 0 0]
        _operators.AppendLine($"{Fmt(cos)} {Fmt(sin)} {Fmt(-sin)} {Fmt(cos)} 0 0 cm");
    }

    /// <summary>
    /// Applies a transformation matrix.
    /// </summary>
    public void Transform(double a, double b, double c, double d, double e, double f)
    {
        ThrowIfDisposed();
        _operators.AppendLine($"{Fmt(a)} {Fmt(b)} {Fmt(c)} {Fmt(d)} {Fmt(e)} {Fmt(f)} cm");
    }

    #endregion

    #region Rectangle Drawing

    /// <summary>
    /// Draws a filled rectangle.
    /// </summary>
    public void DrawRectangle(double x, double y, double width, double height, PdfBrush fill)
    {
        DrawRectangle(x, y, width, height, fill, null);
    }

    /// <summary>
    /// Draws a rectangle with optional fill and stroke.
    /// </summary>
    public void DrawRectangle(double x, double y, double width, double height, PdfBrush? fill, PdfPen? stroke)
    {
        ThrowIfDisposed();

        // Set colors
        if (fill != null)
        {
            _operators.AppendLine(fill.GetFillColorOperator());
        }

        if (stroke != null)
        {
            _operators.AppendLine(stroke.GetStrokeColorOperator());
            _operators.AppendLine(stroke.GetLineWidthOperator());
        }

        // Draw rectangle path
        _operators.AppendLine($"{Fmt(x)} {Fmt(y)} {Fmt(width)} {Fmt(height)} re");

        // Fill and/or stroke
        if (fill != null && stroke != null)
        {
            _operators.AppendLine("B"); // Fill and stroke
        }
        else if (fill != null)
        {
            _operators.AppendLine("f"); // Fill only
        }
        else if (stroke != null)
        {
            _operators.AppendLine("S"); // Stroke only
        }
    }

    #endregion

    #region Line Drawing

    /// <summary>
    /// Draws a line from (x1,y1) to (x2,y2).
    /// </summary>
    public void DrawLine(double x1, double y1, double x2, double y2, PdfPen pen)
    {
        ThrowIfDisposed();

        _operators.AppendLine(pen.GetStrokeColorOperator());
        _operators.AppendLine(pen.GetLineWidthOperator());
        _operators.AppendLine($"{Fmt(x1)} {Fmt(y1)} m");
        _operators.AppendLine($"{Fmt(x2)} {Fmt(y2)} l");
        _operators.AppendLine("S");
    }

    #endregion

    #region Path Operations

    /// <summary>
    /// Begins a new path.
    /// </summary>
    public void BeginPath()
    {
        ThrowIfDisposed();
        // Path operations follow
    }

    /// <summary>
    /// Moves the current point to (x, y) without drawing.
    /// </summary>
    public void MoveTo(double x, double y)
    {
        ThrowIfDisposed();
        _operators.AppendLine($"{Fmt(x)} {Fmt(y)} m");
    }

    /// <summary>
    /// Draws a line from the current point to (x, y).
    /// </summary>
    public void LineTo(double x, double y)
    {
        ThrowIfDisposed();
        _operators.AppendLine($"{Fmt(x)} {Fmt(y)} l");
    }

    /// <summary>
    /// Draws a cubic Bezier curve.
    /// </summary>
    /// <param name="x1">First control point X</param>
    /// <param name="y1">First control point Y</param>
    /// <param name="x2">Second control point X</param>
    /// <param name="y2">Second control point Y</param>
    /// <param name="x3">End point X</param>
    /// <param name="y3">End point Y</param>
    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        ThrowIfDisposed();
        _operators.AppendLine($"{Fmt(x1)} {Fmt(y1)} {Fmt(x2)} {Fmt(y2)} {Fmt(x3)} {Fmt(y3)} c");
    }

    /// <summary>
    /// Closes the current subpath by drawing a line to the starting point.
    /// </summary>
    public void ClosePath()
    {
        ThrowIfDisposed();
        _operators.AppendLine("h");
    }

    /// <summary>
    /// Strokes the current path.
    /// </summary>
    public void Stroke(PdfPen pen)
    {
        ThrowIfDisposed();
        _operators.AppendLine(pen.GetStrokeColorOperator());
        _operators.AppendLine(pen.GetLineWidthOperator());
        _operators.AppendLine("S");
    }

    /// <summary>
    /// Fills the current path.
    /// </summary>
    public void Fill(PdfBrush brush)
    {
        ThrowIfDisposed();
        _operators.AppendLine(brush.GetFillColorOperator());
        _operators.AppendLine("f");
    }

    /// <summary>
    /// Fills and strokes the current path.
    /// </summary>
    public void FillAndStroke(PdfBrush brush, PdfPen pen)
    {
        ThrowIfDisposed();
        _operators.AppendLine(brush.GetFillColorOperator());
        _operators.AppendLine(pen.GetStrokeColorOperator());
        _operators.AppendLine(pen.GetLineWidthOperator());
        _operators.AppendLine("B");
    }

    #endregion

    #region Output

    /// <summary>
    /// Gets the generated PDF operators as a string.
    /// </summary>
    public string GetOperators()
    {
        return _operators.ToString();
    }

    /// <summary>
    /// Flushes the graphics operations to the page's content stream.
    /// </summary>
    public void Flush()
    {
        ThrowIfDisposed();

        if (_operators.Length == 0)
            return;

        // Get existing content
        var existingContent = _page.GetContentStreamBytes();
        var existingText = Encoding.Latin1.GetString(existingContent);

        // Append new operators
        var newContent = existingText + "\n" + _operators.ToString();
        var newBytes = Encoding.Latin1.GetBytes(newContent);

        // Update page content
        _page.SetContentStreamBytes(newBytes);

        // Clear the buffer
        _operators.Clear();
    }

    #endregion

    #region Private Helpers

    private static string Fmt(double value)
    {
        // Format number without unnecessary trailing zeros
        if (Math.Abs(value - Math.Round(value)) < 0.0001)
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PdfGraphics));
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            // Flush any remaining operations
            if (_operators.Length > 0)
                Flush();
            _disposed = true;
        }
    }
}
