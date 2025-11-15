using Avalonia;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Parses PDF content streams and extracts operations
/// </summary>
public class ContentStreamParser
{
    private readonly TextBoundsCalculator _boundsCalculator;
    
    public ContentStreamParser()
    {
        _boundsCalculator = new TextBoundsCalculator();
    }
    
    /// <summary>
    /// Parse a PDF page's content stream and return list of operations
    /// </summary>
    public List<PdfOperation> ParseContentStream(PdfPage page)
    {
        var operations = new List<PdfOperation>();
        
        try
        {
            var content = ContentReader.ReadContent(page);
            var pageHeight = page.Height.Point;
            
            // State tracking
            var graphicsStateStack = new Stack<PdfGraphicsState>();
            var currentGraphicsState = new PdfGraphicsState();
            var currentTextState = new PdfTextState();
            var inTextObject = false;
            
            // Get page resources for font lookups
            var resources = page.Elements.GetDictionary("/Resources");
            
            // Parse all objects in the content stream
            ParseCObjects(content, operations, graphicsStateStack, 
                         currentGraphicsState, currentTextState, 
                         ref inTextObject, resources, pageHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing content stream: {ex.Message}");
        }
        
        return operations;
    }
    
    /// <summary>
    /// Recursively parse CObjects from content stream
    /// </summary>
    private void ParseCObjects(CObject obj, List<PdfOperation> operations,
                              Stack<PdfGraphicsState> stateStack,
                              PdfGraphicsState gState, PdfTextState tState,
                              ref bool inTextObject, PdfDictionary? resources,
                              double pageHeight)
    {
        if (obj is COperator op)
        {
            var operation = ProcessOperator(op, gState, tState, inTextObject, 
                                          resources, pageHeight, ref inTextObject);
            if (operation != null)
                operations.Add(operation);
            
            // Update state based on operator
            UpdateState(op, gState, tState, stateStack, ref inTextObject, resources);
        }
        else if (obj is CSequence sequence)
        {
            foreach (var item in sequence)
            {
                ParseCObjects(item, operations, stateStack, gState, tState, 
                            ref inTextObject, resources, pageHeight);
            }
        }
        else if (obj is CArray array)
        {
            foreach (var item in array)
            {
                ParseCObjects(item, operations, stateStack, gState, tState, 
                            ref inTextObject, resources, pageHeight);
            }
        }
    }
    
    /// <summary>
    /// Process a single operator and create appropriate operation object
    /// </summary>
    private PdfOperation? ProcessOperator(COperator op, PdfGraphicsState gState, 
                                         PdfTextState tState, bool inTextObject,
                                         PdfDictionary? resources, double pageHeight,
                                         ref bool inText)
    {
        var opName = op.OpCode.Name;
        
        switch (opName)
        {
            // Text showing operators
            case "Tj":  // Show text
            case "TJ":  // Show text with individual glyph positioning
            case "'":   // Move to next line and show text
            case "\"":  // Set word/char spacing, move to next line, show text
                return CreateTextOperation(op, gState, tState, pageHeight);
            
            // Path construction
            case "m":   // Move to
            case "l":   // Line to
            case "c":   // Curve to
            case "v":   // Curve to (variant)
            case "y":   // Curve to (variant)
            case "h":   // Close path
            case "re":  // Rectangle
                return CreatePathOperation(op, gState, opName);
            
            // Path painting
            case "S":   // Stroke
            case "s":   // Close and stroke
            case "f":   // Fill
            case "F":   // Fill (alternate)
            case "f*":  // Fill (even-odd)
            case "B":   // Fill and stroke
            case "B*":  // Fill (even-odd) and stroke
            case "b":   // Close, fill, and stroke
            case "b*":  // Close, fill (even-odd), and stroke
                return CreatePathOperation(op, gState, opName);
            
            // Image
            case "Do":  // Draw XObject (often an image)
                return CreateImageOperation(op, gState, pageHeight);
            
            // Graphics state operations
            case "q":   // Save state
            case "Q":   // Restore state
            case "cm":  // Modify transformation matrix
                return CreateStateOperation(op);
            
            // Text state operations
            case "BT":  // Begin text
            case "ET":  // End text
            case "Tf":  // Set font
            case "Td":  // Move text position
            case "TD":  // Move text position and set leading
            case "Tm":  // Set text matrix
            case "T*":  // Move to start of next line
                return CreateTextStateOperation(op);
            
            default:
                // Preserve other operators
                return new GenericOperation(op, opName);
        }
    }
    
    /// <summary>
    /// Create a text operation from a text-showing operator
    /// </summary>
    private TextOperation CreateTextOperation(COperator op, PdfGraphicsState gState, 
                                             PdfTextState tState, double pageHeight)
    {
        var textOp = new TextOperation(op)
        {
            TextState = tState.Clone(),
            GraphicsState = gState.Clone(),
            FontSize = tState.FontSize,
            FontName = tState.FontName
        };
        
        // Extract text from operands
        textOp.Text = ExtractText(op);
        
        // Calculate bounding box
        textOp.BoundingBox = _boundsCalculator.CalculateBounds(
            textOp.Text, tState, gState, pageHeight);
        
        // Get position
        var (x, y) = tState.TextMatrix.Transform(0, 0);
        textOp.Position = new Point(x, pageHeight - y);
        
        return textOp;
    }
    
    /// <summary>
    /// Extract text string from operator operands
    /// </summary>
    private string ExtractText(COperator op)
    {
        if (op.Operands.Count == 0)
            return string.Empty;
        
        var operand = op.Operands[0];
        
        if (operand is CString str)
        {
            return str.Value;
        }
        else if (operand is CArray array)
        {
            // TJ operator uses array of strings and numbers
            var text = new System.Text.StringBuilder();
            foreach (var item in array)
            {
                if (item is CString arrayStr)
                    text.Append(arrayStr.Value);
            }
            return text.ToString();
        }
        
        return string.Empty;
    }
    
    /// <summary>
    /// Create a path operation
    /// </summary>
    private PathOperation CreatePathOperation(COperator op, PdfGraphicsState gState, string opName)
    {
        var pathOp = new PathOperation(op);
        
        // Determine path type
        pathOp.Type = opName switch
        {
            "m" => PathType.MoveTo,
            "l" => PathType.LineTo,
            "c" or "v" or "y" => PathType.CurveTo,
            "re" => PathType.Rectangle,
            "h" => PathType.ClosePath,
            "S" or "s" => PathType.Stroke,
            "f" or "F" or "f*" => PathType.Fill,
            "B" or "B*" or "b" or "b*" => PathType.FillStroke,
            _ => PathType.Unknown
        };
        
        pathOp.IsStroke = opName.Contains('S') || opName.Contains('s') || 
                         opName.Contains('B') || opName.Contains('b');
        pathOp.IsFill = opName.Contains('f') || opName.Contains('F') || 
                       opName.Contains('B') || opName.Contains('b');
        
        // Extract points from operands
        ExtractPathPoints(op, pathOp, gState);
        
        return pathOp;
    }
    
    /// <summary>
    /// Extract points from path operator
    /// </summary>
    private void ExtractPathPoints(COperator op, PathOperation pathOp, PdfGraphicsState gState)
    {
        var operands = op.Operands;
        
        // Extract coordinates based on operator
        if (op.OpCode.Name == "re") // Rectangle
        {
            if (operands.Count >= 4)
            {
                var x = GetNumber(operands[0]);
                var y = GetNumber(operands[1]);
                var width = GetNumber(operands[2]);
                var height = GetNumber(operands[3]);
                
                // Transform points
                var (x1, y1) = gState.TransformationMatrix.Transform(x, y);
                var (x2, y2) = gState.TransformationMatrix.Transform(x + width, y + height);
                
                pathOp.BoundingBox = new Rect(
                    Math.Min(x1, x2), Math.Min(y1, y2),
                    Math.Abs(x2 - x1), Math.Abs(y2 - y1));
            }
        }
        else
        {
            // For other path operations, extract coordinate pairs
            for (int i = 0; i + 1 < operands.Count; i += 2)
            {
                var x = GetNumber(operands[i]);
                var y = GetNumber(operands[i + 1]);
                var (tx, ty) = gState.TransformationMatrix.Transform(x, y);
                pathOp.Points.Add(new Point(tx, ty));
            }
            
            // Calculate bounding box from points
            if (pathOp.Points.Any())
            {
                var minX = pathOp.Points.Min(p => p.X);
                var minY = pathOp.Points.Min(p => p.Y);
                var maxX = pathOp.Points.Max(p => p.X);
                var maxY = pathOp.Points.Max(p => p.Y);
                pathOp.BoundingBox = new Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }
    
    /// <summary>
    /// Create an image operation
    /// </summary>
    private ImageOperation CreateImageOperation(COperator op, PdfGraphicsState gState, double pageHeight)
    {
        var imageOp = new ImageOperation(op);
        
        if (op.Operands.Count > 0 && op.Operands[0] is CName name)
        {
            imageOp.ResourceName = name.Name;
        }
        
        // Image is drawn in a 1x1 unit square, transformed by CTM
        var (x, y) = gState.TransformationMatrix.Transform(0, 0);
        var (x1, y1) = gState.TransformationMatrix.Transform(1, 1);
        
        imageOp.Position = new Point(x, pageHeight - y);
        imageOp.Width = Math.Abs(x1 - x);
        imageOp.Height = Math.Abs(y1 - y);
        imageOp.BoundingBox = new Rect(x, pageHeight - y1, imageOp.Width, imageOp.Height);
        
        return imageOp;
    }
    
    /// <summary>
    /// Create a state operation
    /// </summary>
    private StateOperation CreateStateOperation(COperator op)
    {
        var stateOp = new StateOperation(op);
        
        stateOp.Type = op.OpCode.Name switch
        {
            "q" => StateOperationType.SaveState,
            "Q" => StateOperationType.RestoreState,
            "cm" => StateOperationType.Transform,
            _ => StateOperationType.Other
        };
        
        if (stateOp.Type == StateOperationType.Transform && op.Operands.Count >= 6)
        {
            var values = new double[6];
            for (int i = 0; i < 6; i++)
                values[i] = GetNumber(op.Operands[i]);
            stateOp.Matrix = PdfMatrix.FromArray(values);
        }
        
        return stateOp;
    }
    
    /// <summary>
    /// Create a text state operation
    /// </summary>
    private TextStateOperation CreateTextStateOperation(COperator op)
    {
        var textStateOp = new TextStateOperation(op);
        
        textStateOp.Type = op.OpCode.Name switch
        {
            "BT" => TextStateOperationType.BeginText,
            "ET" => TextStateOperationType.EndText,
            "Tf" => TextStateOperationType.SetFont,
            "Td" or "TD" => TextStateOperationType.MoveText,
            "Tm" => TextStateOperationType.SetMatrix,
            "T*" => TextStateOperationType.MoveText,
            _ => TextStateOperationType.Other
        };
        
        return textStateOp;
    }
    
    /// <summary>
    /// Update graphics and text state based on operator
    /// </summary>
    private void UpdateState(COperator op, PdfGraphicsState gState, PdfTextState tState,
                            Stack<PdfGraphicsState> stateStack, ref bool inTextObject,
                            PdfDictionary? resources)
    {
        var opName = op.OpCode.Name;
        
        switch (opName)
        {
            case "q": // Save graphics state
                stateStack.Push(gState.Clone());
                break;
            
            case "Q": // Restore graphics state
                if (stateStack.Count > 0)
                {
                    var restored = stateStack.Pop();
                    gState.TransformationMatrix = restored.TransformationMatrix;
                    gState.LineWidth = restored.LineWidth;
                    gState.StrokeColor = restored.StrokeColor;
                    gState.FillColor = restored.FillColor;
                }
                break;
            
            case "cm": // Modify transformation matrix
                if (op.Operands.Count >= 6)
                {
                    var values = new double[6];
                    for (int i = 0; i < 6; i++)
                        values[i] = GetNumber(op.Operands[i]);
                    var matrix = PdfMatrix.FromArray(values);
                    gState.TransformationMatrix = gState.TransformationMatrix.Multiply(matrix);
                }
                break;
            
            case "BT": // Begin text
                inTextObject = true;
                tState.ResetMatrices();
                break;
            
            case "ET": // End text
                inTextObject = false;
                break;
            
            case "Tf": // Set font
                if (op.Operands.Count >= 2)
                {
                    if (op.Operands[0] is CName fontName)
                    {
                        tState.FontName = fontName.Name;
                        tState.FontResource = GetFontResource(resources, fontName.Name);
                    }
                    tState.FontSize = GetNumber(op.Operands[1]);
                }
                break;
            
            case "Td": // Move text position
                if (op.Operands.Count >= 2)
                {
                    var tx = GetNumber(op.Operands[0]);
                    var ty = GetNumber(op.Operands[1]);
                    tState.TranslateText(tx, ty);
                }
                break;
            
            case "TD": // Move text position and set leading
                if (op.Operands.Count >= 2)
                {
                    var tx = GetNumber(op.Operands[0]);
                    var ty = GetNumber(op.Operands[1]);
                    tState.Leading = -ty;
                    tState.TranslateText(tx, ty);
                }
                break;
            
            case "Tm": // Set text matrix
                if (op.Operands.Count >= 6)
                {
                    var values = new double[6];
                    for (int i = 0; i < 6; i++)
                        values[i] = GetNumber(op.Operands[i]);
                    tState.SetTextMatrix(PdfMatrix.FromArray(values));
                }
                break;
            
            case "T*": // Move to start of next line
                tState.MoveToNextLine();
                break;
            
            case "TL": // Set text leading
                if (op.Operands.Count >= 1)
                    tState.Leading = GetNumber(op.Operands[0]);
                break;
            
            case "Tc": // Set character spacing
                if (op.Operands.Count >= 1)
                    tState.CharacterSpacing = GetNumber(op.Operands[0]);
                break;
            
            case "Tw": // Set word spacing
                if (op.Operands.Count >= 1)
                    tState.WordSpacing = GetNumber(op.Operands[0]);
                break;
            
            case "Tz": // Set horizontal scaling
                if (op.Operands.Count >= 1)
                    tState.HorizontalScaling = GetNumber(op.Operands[0]);
                break;
        }
    }
    
    /// <summary>
    /// Get font resource from resources dictionary
    /// </summary>
    private PdfDictionary? GetFontResource(PdfDictionary? resources, string fontName)
    {
        if (resources == null)
            return null;
        
        var fonts = resources.Elements.GetDictionary("/Font");
        if (fonts == null)
            return null;
        
        return fonts.Elements.GetDictionary("/" + fontName);
    }
    
    /// <summary>
    /// Extract numeric value from CObject
    /// </summary>
    private double GetNumber(CObject obj)
    {
        if (obj is CInteger i)
            return i.Value;
        if (obj is CReal r)
            return r.Value;
        return 0;
    }
}
