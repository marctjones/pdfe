# Implementation Guide: True Content Redaction

This guide details how to implement complete content-level redaction in the `RedactionService`.

## Overview

Currently, the `RedactionService.cs` implements **visual redaction** (draws black rectangles). To implement **true content redaction** that permanently removes data from the PDF structure, you need to parse and manipulate PDF content streams.

## PDF Content Stream Basics

A PDF page's content stream contains operators that draw text, graphics, and images. Example:

```
BT                  % Begin text
/F1 12 Tf           % Set font F1, size 12
100 700 Td          % Move to position (100, 700)
(Hello World) Tj    % Show text
ET                  % End text
```

## Required Implementation

### 1. Content Stream Parser

```csharp
public class ContentStreamParser
{
    public List<PdfOperation> ParseContentStream(PdfPage page)
    {
        var operations = new List<PdfOperation>();
        
        // Read the content stream
        var content = ContentReader.ReadContent(page);
        var scanner = new CObjectScanner(content);
        
        // Current state tracking
        var graphicsStateStack = new Stack<GraphicsState>();
        var currentState = new GraphicsState();
        var currentTextState = new TextState();
        
        while (!scanner.EOF)
        {
            var obj = scanner.ReadObject();
            
            if (obj is COperator op)
            {
                var operation = ProcessOperator(op, currentState, currentTextState);
                operations.Add(operation);
                
                // Update state based on operator
                UpdateState(op, currentState, currentTextState, graphicsStateStack);
            }
        }
        
        return operations;
    }
    
    private void UpdateState(COperator op, GraphicsState gState, 
                            TextState tState, Stack<GraphicsState> stack)
    {
        switch (op.OpCode.Name)
        {
            case "q": // Save graphics state
                stack.Push(gState.Clone());
                break;
                
            case "Q": // Restore graphics state
                if (stack.Count > 0)
                    gState = stack.Pop();
                break;
                
            case "cm": // Modify transformation matrix
                var matrix = ReadMatrixFromOperands(op.Operands);
                gState.TransformationMatrix = 
                    gState.TransformationMatrix.Multiply(matrix);
                break;
                
            case "Tf": // Set text font and size
                tState.Font = op.Operands[0];
                tState.FontSize = GetNumber(op.Operands[1]);
                break;
                
            case "Tm": // Set text matrix
                tState.TextMatrix = ReadMatrixFromOperands(op.Operands);
                break;
                
            case "Td": // Move text position
            case "TD":
                var tx = GetNumber(op.Operands[0]);
                var ty = GetNumber(op.Operands[1]);
                tState.TextMatrix = tState.TextMatrix.Translate(tx, ty);
                break;
                
            // ... handle other state-changing operators
        }
    }
}
```

### 2. Graphics and Text State Tracking

```csharp
public class GraphicsState
{
    public Matrix TransformationMatrix { get; set; } = Matrix.Identity;
    public double LineWidth { get; set; } = 1.0;
    public XColor StrokeColor { get; set; } = XColors.Black;
    public XColor FillColor { get; set; } = XColors.Black;
    // ... other graphics state parameters
    
    public GraphicsState Clone()
    {
        return (GraphicsState)this.MemberwiseClone();
    }
}

public class TextState
{
    public PdfItem Font { get; set; }
    public double FontSize { get; set; }
    public Matrix TextMatrix { get; set; } = Matrix.Identity;
    public double CharSpacing { get; set; }
    public double WordSpacing { get; set; }
    public double HorizontalScaling { get; set; } = 100.0;
    public double Leading { get; set; }
    public double Rise { get; set; }
    // ... other text state parameters
}

public class Matrix
{
    public double A { get; set; } = 1;
    public double B { get; set; } = 0;
    public double C { get; set; } = 0;
    public double D { get; set; } = 1;
    public double E { get; set; } = 0;
    public double F { get; set; } = 0;
    
    public static Matrix Identity => new Matrix();
    
    public Matrix Multiply(Matrix other)
    {
        return new Matrix
        {
            A = A * other.A + B * other.C,
            B = A * other.B + B * other.D,
            C = C * other.A + D * other.C,
            D = C * other.B + D * other.D,
            E = E * other.A + F * other.C + other.E,
            F = E * other.B + F * other.D + other.F
        };
    }
    
    public Point Transform(Point point)
    {
        return new Point(
            A * point.X + C * point.Y + E,
            B * point.X + D * point.Y + F
        );
    }
}
```

### 3. PDF Operation Models

```csharp
public abstract class PdfOperation
{
    public COperator Operator { get; set; }
    public Rect BoundingBox { get; set; }
    
    public abstract bool IntersectsWith(Rect area);
}

public class TextOperation : PdfOperation
{
    public string Text { get; set; }
    public Point Position { get; set; }
    public double FontSize { get; set; }
    
    public override bool IntersectsWith(Rect area)
    {
        return area.Intersects(BoundingBox);
    }
}

public class PathOperation : PdfOperation
{
    public List<Point> Points { get; set; }
    public PathType Type { get; set; } // Stroke, Fill, etc.
    
    public override bool IntersectsWith(Rect area)
    {
        return area.Intersects(BoundingBox);
    }
}

public class ImageOperation : PdfOperation
{
    public string ResourceName { get; set; }
    
    public override bool IntersectsWith(Rect area)
    {
        return area.Intersects(BoundingBox);
    }
}
```

### 4. Text Bounding Box Calculation

```csharp
public class TextBoundingBoxCalculator
{
    public Rect CalculateTextBounds(string text, TextState textState, 
                                   GraphicsState graphicsState)
    {
        // Get font metrics (this is simplified - real implementation 
        // needs to read font metrics from the PDF font dictionary)
        var fontSize = textState.FontSize;
        var scaling = textState.HorizontalScaling / 100.0;
        
        // Calculate text width (simplified)
        var textWidth = text.Length * fontSize * 0.6 * scaling; // Approximate
        var textHeight = fontSize;
        
        // Get the starting position from text matrix
        var position = textState.TextMatrix.Transform(new Point(0, 0));
        
        // Apply graphics transformation
        var topLeft = graphicsState.TransformationMatrix.Transform(position);
        var bottomRight = graphicsState.TransformationMatrix.Transform(
            new Point(position.X + textWidth, position.Y + textHeight));
        
        return new Rect(topLeft, bottomRight);
    }
    
    // For more accurate calculation, you need to:
    // 1. Read font metrics from the font dictionary
    // 2. Calculate width of each character using font widths array
    // 3. Apply character spacing and word spacing
    // 4. Handle font encoding (to map character codes to glyphs)
}
```

### 5. Content Stream Rebuilding

```csharp
public class ContentStreamBuilder
{
    public byte[] BuildContentStream(List<PdfOperation> operations)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        
        foreach (var operation in operations)
        {
            WriteOperation(writer, operation.Operator);
        }
        
        writer.Flush();
        return stream.ToArray();
    }
    
    private void WriteOperation(StreamWriter writer, COperator op)
    {
        // Write operands
        foreach (var operand in op.Operands)
        {
            WriteOperand(writer, operand);
            writer.Write(" ");
        }
        
        // Write operator
        writer.Write(op.OpCode.Name);
        writer.WriteLine();
    }
    
    private void WriteOperand(StreamWriter writer, CObject operand)
    {
        switch (operand)
        {
            case CInteger i:
                writer.Write(i.Value);
                break;
            case CReal r:
                writer.Write(r.Value.ToString("0.####"));
                break;
            case CString s:
                writer.Write($"({s.Value})");
                break;
            case CName n:
                writer.Write($"/{n.Name}");
                break;
            // ... handle other types
        }
    }
}
```

### 6. Complete Redaction Service Implementation

```csharp
public class RedactionService
{
    private readonly ContentStreamParser _parser;
    private readonly TextBoundingBoxCalculator _boundsCalculator;
    private readonly ContentStreamBuilder _builder;
    
    public RedactionService()
    {
        _parser = new ContentStreamParser();
        _boundsCalculator = new TextBoundingBoxCalculator();
        _builder = new ContentStreamBuilder();
    }
    
    public void RedactArea(PdfPage page, Rect area)
    {
        // Step 1: Parse content stream
        var operations = _parser.ParseContentStream(page);
        
        // Step 2: Filter operations that intersect with redaction area
        var filteredOperations = operations
            .Where(op => !op.IntersectsWith(area))
            .ToList();
        
        // Step 3: Rebuild content stream
        var newContent = _builder.BuildContentStream(filteredOperations);
        
        // Step 4: Replace page content stream
        page.Contents.Elements.Clear();
        var stream = new PdfDictionary.PdfStream(newContent, page.Owner);
        page.Contents.Elements.Add(stream);
        
        // Step 5: Draw black rectangle (visual redaction)
        DrawBlackRectangle(page, area);
        
        // Step 6: Handle images
        RedactImages(page, area);
    }
    
    private void RedactImages(PdfPage page, Rect area)
    {
        // Get page resources
        var resources = page.Elements.GetDictionary("/Resources");
        if (resources == null) return;
        
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects == null) return;
        
        // Iterate through XObjects (images)
        foreach (var key in xObjects.Elements.Keys)
        {
            var xObject = xObjects.Elements[key] as PdfDictionary;
            if (xObject == null) continue;
            
            var subtype = xObject.Elements.GetName("/Subtype");
            if (subtype == "/Image")
            {
                // Check if image intersects redaction area
                // If so, either remove it or modify it
                // (Implementation depends on requirements)
            }
        }
    }
}
```

## Testing the Implementation

### Test Cases

1. **Text Redaction**
   ```csharp
   [Test]
   public void TestTextRedaction()
   {
       var doc = PdfReader.Open("test.pdf", PdfDocumentOpenMode.Modify);
       var page = doc.Pages[0];
       
       // Redact area containing "Confidential"
       var area = new Rect(100, 100, 200, 50);
       _redactionService.RedactArea(page, area);
       
       doc.Save("redacted.pdf");
       
       // Verify: Open with text extraction and ensure text is gone
       var textExtractor = new PdfTextExtractor();
       var text = textExtractor.ExtractText("redacted.pdf", 0);
       Assert.IsFalse(text.Contains("Confidential"));
   }
   ```

2. **Image Redaction**
3. **Graphics Redaction**
4. **Multiple Redactions**

## Challenges and Solutions

### Challenge 1: Font Metrics
**Problem**: Calculating accurate text bounding boxes requires font metrics.  
**Solution**: Parse font dictionaries, read font widths array, handle different font encodings.

### Challenge 2: Coordinate Systems
**Problem**: PDF uses bottom-left origin, UI uses top-left.  
**Solution**: Convert coordinates: `pdfY = pageHeight - uiY - height`

### Challenge 3: Complex Graphics States
**Problem**: Graphics state can be saved/restored (q/Q operators).  
**Solution**: Use a stack to track state changes.

### Challenge 4: Inline Images
**Problem**: Images can be inline (BI...EI) or external (XObject).  
**Solution**: Handle both types separately.

### Challenge 5: Text in Different Fonts/Sizes
**Problem**: Text can use various fonts and sizes.  
**Solution**: Track text state changes (Tf, Ts operators).

## Performance Optimization

For large PDFs:
1. **Cache parsed operations** - Don't re-parse unless content changes
2. **Spatial indexing** - Use R-tree for fast intersection testing
3. **Incremental updates** - Only rewrite changed pages
4. **Parallel processing** - Process multiple pages concurrently

## References

- **PDF Specification**: ISO 32000-1:2008
- **Key Sections**:
  - Table 51: Text-showing operators
  - Table 59: Graphics state operators
  - Section 8.4: Graphics state
  - Section 9.2: Text objects

## Estimated Effort

- Content stream parser: 400 lines
- State tracking: 300 lines
- Text bounds calculation: 400 lines
- Graphics handling: 300 lines
- Image handling: 200 lines
- Content stream rebuilding: 300 lines
- Testing and debugging: 400 lines
- **Total**: ~2000 lines of code

## Alternative: Third-Party Libraries

If implementing from scratch is too complex, consider:
- **Aspose.PDF** (Commercial, expensive)
- **Syncfusion PDF** (Commercial, more affordable)
- **IronPDF** (Commercial)

However, these are commercial and may have licensing costs.

For a production application handling sensitive data, investing in the custom implementation or a commercial library is recommended to ensure true content removal.
