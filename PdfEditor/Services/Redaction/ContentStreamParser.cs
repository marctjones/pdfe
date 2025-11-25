using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Parses PDF content streams and extracts operations
/// </summary>
public class ContentStreamParser
{
    private readonly TextBoundsCalculator _boundsCalculator;
    private readonly ILogger<ContentStreamParser> _logger;

    private readonly ILoggerFactory _loggerFactory;

    public ContentStreamParser(ILogger<ContentStreamParser> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _boundsCalculator = new TextBoundsCalculator(_loggerFactory.CreateLogger<TextBoundsCalculator>());
        _logger.LogDebug("ContentStreamParser instance created");
    }
    
    /// <summary>
    /// Parse a PDF page's content stream and return list of operations
    /// </summary>
    public List<PdfOperation> ParseContentStream(PdfPage page)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting content stream parsing");

        var operations = new List<PdfOperation>();

        try
        {
            _logger.LogDebug("Reading page content with ContentReader");
            var content = ContentReader.ReadContent(page);
            var pageHeight = page.Height.Point;

            _logger.LogDebug("Page dimensions: Width={Width}, Height={Height}",
                page.Width.Point, pageHeight);

            // State tracking
            var graphicsStateStack = new Stack<PdfGraphicsState>();
            var currentGraphicsState = new PdfGraphicsState();
            var currentTextState = new PdfTextState();
            var inTextObject = false;

            // Get page resources for font lookups
            var resources = page.Elements.GetDictionary("/Resources");
            _logger.LogDebug("Page resources dictionary found: {HasResources}", resources != null);

            // Parse all objects in the content stream
            _logger.LogDebug("Beginning recursive CObject parsing");
            ParseCObjects(content, operations, graphicsStateStack,
                         currentGraphicsState, currentTextState,
                         ref inTextObject, resources, pageHeight);

            sw.Stop();

            // Log statistics
            var textOps = operations.OfType<TextOperation>().Count();
            var pathOps = operations.OfType<PathOperation>().Count();
            var imageOps = operations.OfType<ImageOperation>().Count();
            var stateOps = operations.OfType<StateOperation>().Count();
            var textStateOps = operations.OfType<TextStateOperation>().Count();

            _logger.LogInformation(
                "Content stream parsing complete in {ElapsedMs}ms. " +
                "Total operations: {Total} (Text: {Text}, Path: {Path}, Image: {Image}, " +
                "State: {State}, TextState: {TextState})",
                sw.ElapsedMilliseconds, operations.Count, textOps, pathOps, imageOps,
                stateOps, textStateOps);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error parsing content stream after {ElapsedMs}ms",
                sw.ElapsedMilliseconds);
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
        _logger.LogTrace("Processing operator: {OpName}", opName);

        switch (opName)
        {
            // Text showing operators
            case "Tj":  // Show text
            case "TJ":  // Show text with individual glyph positioning
            case "'":   // Move to next line and show text
            case "\"":  // Set word/char spacing, move to next line, show text
                _logger.LogDebug("Creating text operation for operator: {OpName}", opName);
                return CreateTextOperation(op, gState, tState, pageHeight);

            // Path construction
            case "m":   // Move to
            case "l":   // Line to
            case "c":   // Curve to
            case "v":   // Curve to (variant)
            case "y":   // Curve to (variant)
            case "h":   // Close path
            case "re":  // Rectangle
                _logger.LogDebug("Creating path operation for operator: {OpName}", opName);
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
                _logger.LogDebug("Creating path painting operation for operator: {OpName}", opName);
                return CreatePathOperation(op, gState, opName);

            // Image
            case "Do":  // Draw XObject (often an image)
                _logger.LogDebug("Creating image operation for operator: {OpName}", opName);
                return CreateImageOperation(op, gState, pageHeight);

            // Graphics state operations
            case "q":   // Save state
            case "Q":   // Restore state
            case "cm":  // Modify transformation matrix
                _logger.LogTrace("Creating state operation for operator: {OpName}", opName);
                return CreateStateOperation(op);

            // Text state operations
            case "BT":  // Begin text
            case "ET":  // End text
            case "Tf":  // Set font
            case "Td":  // Move text position
            case "TD":  // Move text position and set leading
            case "Tm":  // Set text matrix
            case "T*":  // Move to start of next line
                _logger.LogTrace("Creating text state operation for operator: {OpName}", opName);
                return CreateTextStateOperation(op);

            default:
                // Preserve other operators
                _logger.LogTrace("Creating generic operation for unknown operator: {OpName}", opName);
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

        _logger.LogDebug(
            "Text operation created: Text=\"{Text}\" (length={Length}), " +
            "BoundingBox=({X:F2},{Y:F2},{W:F2}x{H:F2}), Font={Font}/{Size}pt",
            textOp.Text.Length > 50 ? textOp.Text.Substring(0, 50) + "..." : textOp.Text,
            textOp.Text.Length,
            textOp.BoundingBox.X, textOp.BoundingBox.Y,
            textOp.BoundingBox.Width, textOp.BoundingBox.Height,
            textOp.FontName, textOp.FontSize);

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

    /// <summary>
    /// Parse inline images (BI...ID...EI sequences) from raw content stream bytes
    /// </summary>
    /// <param name="page">The PDF page</param>
    /// <param name="pageHeight">Page height for coordinate conversion</param>
    /// <param name="graphicsState">Current graphics state for transformations</param>
    /// <returns>List of inline image operations found</returns>
    public List<InlineImageOperation> ParseInlineImages(PdfPage page, double pageHeight, PdfGraphicsState graphicsState)
    {
        var inlineImages = new List<InlineImageOperation>();

        try
        {
            // Get raw content stream bytes
            var rawBytes = GetRawContentStream(page);
            if (rawBytes == null || rawBytes.Length == 0)
            {
                return inlineImages;
            }

            _logger.LogDebug("Scanning {Length} bytes for inline images", rawBytes.Length);

            // Scan for BI (Begin Inline Image) operators
            int position = 0;
            while (position < rawBytes.Length - 2)
            {
                // Look for "BI" followed by whitespace
                if (rawBytes[position] == 'B' && rawBytes[position + 1] == 'I' &&
                    (position + 2 >= rawBytes.Length || IsWhitespace(rawBytes[position + 2])))
                {
                    // Check that BI is preceded by whitespace or start of stream
                    if (position == 0 || IsWhitespace(rawBytes[position - 1]))
                    {
                        var imageStart = position;

                        // Find ID (Image Data) marker
                        var idPosition = FindSequence(rawBytes, position + 2, "ID");
                        if (idPosition < 0)
                        {
                            position++;
                            continue;
                        }

                        // Find EI (End Image) marker
                        // EI must be preceded by whitespace and followed by whitespace or end
                        var eiPosition = FindEIMarker(rawBytes, idPosition + 2);
                        if (eiPosition < 0)
                        {
                            position++;
                            continue;
                        }

                        // Extract the complete inline image sequence
                        var imageLength = eiPosition + 2 - imageStart;
                        var imageData = new byte[imageLength];
                        Array.Copy(rawBytes, imageStart, imageData, 0, imageLength);

                        // Parse image properties from BI...ID section
                        var properties = ParseInlineImageProperties(rawBytes, position + 2, idPosition);

                        // Calculate bounding box using CTM
                        // Inline images are rendered in a 1x1 unit square transformed by CTM
                        var bounds = CalculateInlineImageBounds(graphicsState, pageHeight,
                            properties.width, properties.height);

                        var inlineImage = new InlineImageOperation(imageData, bounds, imageStart, imageLength)
                        {
                            ImageWidth = properties.width,
                            ImageHeight = properties.height
                        };

                        inlineImages.Add(inlineImage);

                        _logger.LogDebug(
                            "Found inline image at position {Position}: {Width}x{Height}, bounds=({X:F2},{Y:F2},{W:F2}x{H:F2})",
                            imageStart, properties.width, properties.height,
                            bounds.X, bounds.Y, bounds.Width, bounds.Height);

                        // Skip past this image
                        position = eiPosition + 2;
                        continue;
                    }
                }

                position++;
            }

            _logger.LogInformation("Found {Count} inline images in content stream", inlineImages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing inline images");
        }

        return inlineImages;
    }

    /// <summary>
    /// Get raw content stream bytes from a page
    /// </summary>
    private byte[]? GetRawContentStream(PdfPage page)
    {
        try
        {
            if (page.Contents.Elements.Count == 0)
                return null;

            using var ms = new MemoryStream();

            foreach (var item in page.Contents.Elements)
            {
                PdfDictionary? contentDict = null;

                if (item is PdfReference pdfRef)
                {
                    contentDict = pdfRef.Value as PdfDictionary;
                }
                else if (item is PdfDictionary dict)
                {
                    contentDict = dict;
                }

                if (contentDict?.Stream?.Value != null)
                {
                    ms.Write(contentDict.Stream.Value, 0, contentDict.Stream.Value.Length);
                    ms.WriteByte((byte)'\n');
                }
            }

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get raw content stream");
            return null;
        }
    }

    /// <summary>
    /// Check if a byte is PDF whitespace
    /// </summary>
    private bool IsWhitespace(byte b)
    {
        return b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    /// <summary>
    /// Find a sequence of ASCII characters in byte array
    /// </summary>
    private int FindSequence(byte[] data, int startIndex, string sequence)
    {
        var seqBytes = Encoding.ASCII.GetBytes(sequence);

        for (int i = startIndex; i <= data.Length - seqBytes.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < seqBytes.Length; j++)
            {
                if (data[i + j] != seqBytes[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                // For ID, must be followed by single whitespace then data
                if (sequence == "ID" && i + 2 < data.Length && !IsWhitespace(data[i + 2]))
                {
                    continue;
                }
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Find EI marker that ends an inline image
    /// EI must be preceded by whitespace and followed by whitespace/end
    /// </summary>
    private int FindEIMarker(byte[] data, int startIndex)
    {
        for (int i = startIndex; i < data.Length - 1; i++)
        {
            if (data[i] == 'E' && data[i + 1] == 'I')
            {
                // Must be preceded by whitespace
                if (i > 0 && !IsWhitespace(data[i - 1]))
                    continue;

                // Must be followed by whitespace or end of data
                if (i + 2 < data.Length && !IsWhitespace(data[i + 2]))
                    continue;

                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Parse inline image properties from the BI...ID section
    /// </summary>
    private (int width, int height) ParseInlineImageProperties(byte[] data, int start, int end)
    {
        int width = 1;
        int height = 1;

        try
        {
            var propString = Encoding.ASCII.GetString(data, start, end - start);

            // Parse key/value pairs
            // Common keys: W (Width), H (Height), BPC (BitsPerComponent), CS (ColorSpace)
            var tokens = propString.Split(new[] { ' ', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length - 1; i++)
            {
                var key = tokens[i].TrimStart('/');
                var value = tokens[i + 1];

                if ((key == "W" || key == "Width") && int.TryParse(value, out int w))
                {
                    width = w;
                }
                else if ((key == "H" || key == "Height") && int.TryParse(value, out int h))
                {
                    height = h;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse inline image properties");
        }

        return (width, height);
    }

    /// <summary>
    /// Calculate bounding box for inline image based on CTM
    /// </summary>
    private Rect CalculateInlineImageBounds(PdfGraphicsState gState, double pageHeight, int imgWidth, int imgHeight)
    {
        // Inline images are drawn in a unit square, scaled by CTM
        // Transform the four corners of the unit square
        var ctm = gState.TransformationMatrix;

        var (x1, y1) = ctm.Transform(0, 0);
        var (x2, y2) = ctm.Transform(1, 0);
        var (x3, y3) = ctm.Transform(1, 1);
        var (x4, y4) = ctm.Transform(0, 1);

        // Find bounding box
        var minX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var maxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        var minY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        var maxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));

        // Convert to Avalonia coordinates (top-left origin)
        var avaloniaY = pageHeight - maxY;

        return new Rect(minX, avaloniaY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Parse a PDF page's content stream recursively, including Form XObjects
    /// </summary>
    public List<PdfOperation> ParseContentStreamRecursive(PdfPage page)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting recursive content stream parsing (including Form XObjects)");

        var operations = new List<PdfOperation>();

        try
        {
            // Parse main content stream
            operations.AddRange(ParseContentStream(page));

            // Get page resources
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
            {
                _logger.LogDebug("No resources dictionary found");
                return operations;
            }

            // Parse Form XObjects
            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects != null)
            {
                var pageHeight = page.Height.Point;
                var formXObjectOps = ParseFormXObjects(xObjects, pageHeight, new PdfGraphicsState());
                operations.AddRange(formXObjectOps);

                _logger.LogInformation("Found {Count} operations in Form XObjects", formXObjectOps.Count);
            }

            sw.Stop();
            _logger.LogInformation(
                "Recursive parsing complete in {ElapsedMs}ms. Total operations: {Count}",
                sw.ElapsedMilliseconds, operations.Count);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error during recursive content stream parsing");
        }

        return operations;
    }

    /// <summary>
    /// Parse all Form XObjects in a resources dictionary
    /// </summary>
    public List<PdfOperation> ParseFormXObjects(PdfDictionary xObjects, double pageHeight, PdfGraphicsState parentState)
    {
        var operations = new List<PdfOperation>();

        foreach (var key in xObjects.Elements.Keys)
        {
            try
            {
                PdfDictionary? xObject = null;

                var element = xObjects.Elements[key];
                if (element is PdfSharp.Pdf.Advanced.PdfReference pdfRef)
                {
                    xObject = pdfRef.Value as PdfDictionary;
                }
                else if (element is PdfDictionary dict)
                {
                    xObject = dict;
                }

                if (xObject == null)
                    continue;

                // Check if it's a Form XObject
                var subtype = xObject.Elements.GetName("/Subtype");
                if (subtype != "/Form")
                    continue;

                _logger.LogDebug("Parsing Form XObject: {Key}", key);

                var formOps = ParseSingleFormXObject(xObject, key.ToString(), pageHeight, parentState);
                operations.AddRange(formOps);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing XObject {Key}", key);
            }
        }

        return operations;
    }

    /// <summary>
    /// Parse a single Form XObject's content stream
    /// </summary>
    private List<PdfOperation> ParseSingleFormXObject(
        PdfDictionary formXObject,
        string name,
        double pageHeight,
        PdfGraphicsState parentState)
    {
        var operations = new List<PdfOperation>();

        try
        {
            // Get the form's stream
            if (formXObject.Stream?.Value == null)
            {
                _logger.LogDebug("Form XObject {Name} has no stream", name);
                return operations;
            }

            // Get form's BBox for coordinate transformation
            var bbox = formXObject.Elements.GetArray("/BBox");
            var matrix = formXObject.Elements.GetArray("/Matrix");

            // Create a modified graphics state for the form
            var formState = parentState.Clone();

            // Apply form's transformation matrix if present
            if (matrix != null && matrix.Elements.Count >= 6)
            {
                var matrixValues = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    matrixValues[i] = GetArrayDouble(matrix, i);
                }
                var formMatrix = PdfMatrix.FromArray(matrixValues);
                formState.TransformationMatrix = formState.TransformationMatrix.Multiply(formMatrix);
            }

            // Parse the form's content stream
            var content = ContentReader.ReadContent(formXObject);
            if (content == null)
            {
                _logger.LogDebug("Could not read content from Form XObject {Name}", name);
                return operations;
            }

            // State tracking for form parsing
            var stateStack = new Stack<PdfGraphicsState>();
            var textState = new PdfTextState();
            var inTextObject = false;

            // Get form's resources
            var formResources = formXObject.Elements.GetDictionary("/Resources");

            // Parse the form's content
            ParseCObjects(content, operations, stateStack, formState, textState,
                         ref inTextObject, formResources, pageHeight);

            // Tag operations as coming from Form XObject
            foreach (var op in operations)
            {
                if (op is FormXObjectOperation formOp)
                {
                    formOp.SourceXObjectName = name;
                }
            }

            _logger.LogDebug("Parsed {Count} operations from Form XObject {Name}", operations.Count, name);

            // Recursively parse nested XObjects
            if (formResources != null)
            {
                var nestedXObjects = formResources.Elements.GetDictionary("/XObject");
                if (nestedXObjects != null)
                {
                    var nestedOps = ParseFormXObjects(nestedXObjects, pageHeight, formState);
                    operations.AddRange(nestedOps);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing Form XObject {Name}", name);
        }

        return operations;
    }

    /// <summary>
    /// Get a double value from a PdfArray at the specified index
    /// </summary>
    private double GetArrayDouble(PdfSharp.Pdf.PdfArray array, int index)
    {
        if (index >= array.Elements.Count)
            return 0;

        var element = array.Elements[index];

        if (element is PdfSharp.Pdf.PdfInteger intVal)
            return intVal.Value;
        if (element is PdfSharp.Pdf.PdfReal realVal)
            return realVal.Value;

        return 0;
    }
}

/// <summary>
/// Represents an operation from a Form XObject
/// </summary>
public class FormXObjectOperation : PdfOperation
{
    /// <summary>
    /// Name of the XObject this operation came from
    /// </summary>
    public string SourceXObjectName { get; set; } = string.Empty;

    public FormXObjectOperation(CObject obj) : base(obj) { }
}
