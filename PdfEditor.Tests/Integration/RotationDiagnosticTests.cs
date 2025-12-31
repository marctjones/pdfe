using FluentAssertions;
using PdfEditor.Tests.Utilities;
using Xunit;
using Xunit.Abstractions;
using UglyToad.PdfPig;
using PdfEditor.Redaction;
using PdfEditor.Redaction.ContentStream.Parsing;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Diagnostic tests to dump actual coordinate data at each step of the redaction process.
/// This helps us understand exactly where the mismatch occurs for rotated pages.
/// </summary>
public class RotationDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();

    public RotationDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    private string GetTempPath(string suffix = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"diag_test_{Guid.NewGuid()}{suffix}");
        _tempFiles.Add(path);
        return path;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DumpAllCoordinates_ForRotatedPage(int rotation)
    {
        // Create test PDF
        var pdfPath = GetTempPath();
        TestPdfGenerator.CreateRotatedPdfVisualPosition(pdfPath, rotation, "REDACT", visualX: 300, visualY: 400, fontSize: 24);

        _output.WriteLine($"");
        _output.WriteLine($"===============================================");
        _output.WriteLine($"=== ROTATION {rotation}° DIAGNOSTIC DUMP ===");
        _output.WriteLine($"===============================================");
        _output.WriteLine($"");

        // STEP 1: What does PdfPig see?
        _output.WriteLine($"--- STEP 1: PdfPig Letter Extraction ---");
        using var pigDoc = PdfDocument.Open(pdfPath);
        var pigPage = pigDoc.GetPage(1);
        var letters = pigPage.Letters.ToList();

        _output.WriteLine($"Page dimensions: {pigPage.Width} x {pigPage.Height}");
        _output.WriteLine($"Page rotation property: {pigPage.Rotation}");
        _output.WriteLine($"Letter count: {letters.Count}");
        _output.WriteLine($"Full text (joined): '{string.Join("", letters.Select(l => l.Value))}'");
        _output.WriteLine($"");
        _output.WriteLine($"Individual letters:");
        foreach (var letter in letters)
        {
            _output.WriteLine($"  '{letter.Value}' at X={letter.GlyphRectangle.Left:F1}, Y={letter.GlyphRectangle.Bottom:F1} " +
                            $"(rect: {letter.GlyphRectangle.Left:F1},{letter.GlyphRectangle.Bottom:F1} to {letter.GlyphRectangle.Right:F1},{letter.GlyphRectangle.Top:F1})");
        }

        // Calculate bounding box of all letters
        if (letters.Count > 0)
        {
            var minX = letters.Min(l => l.GlyphRectangle.Left);
            var maxX = letters.Max(l => l.GlyphRectangle.Right);
            var minY = letters.Min(l => l.GlyphRectangle.Bottom);
            var maxY = letters.Max(l => l.GlyphRectangle.Top);
            _output.WriteLine($"");
            _output.WriteLine($"PdfPig bounding box of all letters: ({minX:F1},{minY:F1}) to ({maxX:F1},{maxY:F1})");
        }

        // STEP 2: What does FindTextLocations find?
        _output.WriteLine($"");
        _output.WriteLine($"--- STEP 2: TextRedactor.FindTextLocations equivalent ---");

        // Replicate FindTextMatches logic
        var fullText = string.Join("", letters.Select(l => l.Value));
        var searchText = "REDACT";
        var idx = fullText.IndexOf(searchText, StringComparison.Ordinal);
        _output.WriteLine($"Searching for '{searchText}' in '{fullText}'");
        _output.WriteLine($"Found at index: {idx}");

        if (idx >= 0)
        {
            var matchLetters = letters.Skip(idx).Take(searchText.Length).ToList();
            var matchMinX = matchLetters.Min(l => l.GlyphRectangle.Left);
            var matchMaxX = matchLetters.Max(l => l.GlyphRectangle.Right);
            var matchMinY = matchLetters.Min(l => l.GlyphRectangle.Bottom);
            var matchMaxY = matchLetters.Max(l => l.GlyphRectangle.Top);

            _output.WriteLine($"Match bounding box (redaction area): ({matchMinX:F1},{matchMinY:F1}) to ({matchMaxX:F1},{matchMaxY:F1})");
            _output.WriteLine($"This is the area that will be passed to ContentStreamRedactor");
        }

        // STEP 3: What does ContentStreamParser see?
        _output.WriteLine($"");
        _output.WriteLine($"--- STEP 3: ContentStreamParser operations ---");

        using var sharpDoc = PdfSharp.Pdf.IO.PdfReader.Open(pdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var sharpPage = sharpDoc.Pages[0];

        // Dump all page dimension properties
        _output.WriteLine($"PdfSharp page.Width: {sharpPage.Width.Point}");
        _output.WriteLine($"PdfSharp page.Height: {sharpPage.Height.Point}");
        _output.WriteLine($"PdfSharp page.Rotate: {sharpPage.Rotate}");
        var mediaBox = sharpPage.MediaBox;
        _output.WriteLine($"PdfSharp MediaBox: ({mediaBox.X1},{mediaBox.Y1}) to ({mediaBox.X2},{mediaBox.Y2})");
        _output.WriteLine($"MediaBox width: {mediaBox.Width}, height: {mediaBox.Height}");

        // Get content stream
        byte[]? contentBytes = null;
        var contents = sharpPage.Contents;
        if (contents != null && contents.Elements.Count > 0)
        {
            using var ms = new MemoryStream();
            for (int i = 0; i < contents.Elements.Count; i++)
            {
                var stream = contents.Elements.GetObject(i) as PdfSharp.Pdf.PdfDictionary;
                if (stream?.Stream?.Value != null)
                {
                    ms.Write(stream.Stream.Value, 0, stream.Stream.Value.Length);
                }
            }
            contentBytes = ms.ToArray();
        }

        if (contentBytes != null && contentBytes.Length > 0)
        {
            _output.WriteLine($"Content stream size: {contentBytes.Length} bytes");

            // Parse with ContentStreamParser
            var parser = new ContentStreamParser();
            var pageHeight = (double)sharpPage.Height.Point;
            _output.WriteLine($"PageHeight passed to parser: {pageHeight}");

            var operations = parser.Parse(contentBytes, pageHeight);
            _output.WriteLine($"Parsed {operations.Count} operations");

            // Show text operations
            var textOps = operations.OfType<TextOperation>().ToList();
            _output.WriteLine($"Text operations: {textOps.Count}");
            foreach (var textOp in textOps)
            {
                _output.WriteLine($"  TextOp '{textOp.Text}' at bbox ({textOp.BoundingBox.Left:F1},{textOp.BoundingBox.Bottom:F1}) to ({textOp.BoundingBox.Right:F1},{textOp.BoundingBox.Top:F1})");
                _output.WriteLine($"    Operator: {textOp.Operator}, FontName: {textOp.FontName}, FontSize: {textOp.FontSize}");
            }
        }
        else
        {
            _output.WriteLine($"No content stream found!");
        }

        // STEP 4: Compare coordinates
        _output.WriteLine($"");
        _output.WriteLine($"--- STEP 4: Coordinate Comparison ---");
        if (idx >= 0 && letters.Count > 0)
        {
            var matchLetters = letters.Skip(idx).Take(searchText.Length).ToList();
            var pdfPigBox = new {
                Left = matchLetters.Min(l => l.GlyphRectangle.Left),
                Right = matchLetters.Max(l => l.GlyphRectangle.Right),
                Bottom = matchLetters.Min(l => l.GlyphRectangle.Bottom),
                Top = matchLetters.Max(l => l.GlyphRectangle.Top)
            };

            _output.WriteLine($"PdfPig redaction area: ({pdfPigBox.Left:F1},{pdfPigBox.Bottom:F1}) to ({pdfPigBox.Right:F1},{pdfPigBox.Top:F1})");

            // Check if content stream parser found any text operations
            if (contentBytes != null)
            {
                var parser = new ContentStreamParser();
                var pageHeight = (double)sharpDoc.Pages[0].Height.Point;
                var operations = parser.Parse(contentBytes, pageHeight);
                var textOps = operations.OfType<TextOperation>().ToList();

                if (textOps.Count > 0)
                {
                    var parserBox = new {
                        Left = textOps.Min(t => t.BoundingBox.Left),
                        Right = textOps.Max(t => t.BoundingBox.Right),
                        Bottom = textOps.Min(t => t.BoundingBox.Bottom),
                        Top = textOps.Max(t => t.BoundingBox.Top)
                    };

                    _output.WriteLine($"Parser text bbox:      ({parserBox.Left:F1},{parserBox.Bottom:F1}) to ({parserBox.Right:F1},{parserBox.Top:F1})");

                    // Check for intersection
                    bool intersects = !(pdfPigBox.Right < parserBox.Left ||
                                       parserBox.Right < pdfPigBox.Left ||
                                       pdfPigBox.Top < parserBox.Bottom ||
                                       parserBox.Top < pdfPigBox.Bottom);

                    _output.WriteLine($"");
                    _output.WriteLine($"DO THEY INTERSECT? {intersects}");

                    if (!intersects)
                    {
                        _output.WriteLine($"");
                        _output.WriteLine($"*** PROBLEM FOUND: Coordinates don't intersect! ***");
                        _output.WriteLine($"PdfPig uses VISUAL coordinates (post-rotation)");
                        _output.WriteLine($"Parser uses CONTENT STREAM coordinates (pre-rotation)");
                        _output.WriteLine($"For rotation {rotation}°, these are in different coordinate spaces!");
                    }
                }
            }
        }

        _output.WriteLine($"");
        _output.WriteLine($"=== END DIAGNOSTIC FOR {rotation}° ===");
        _output.WriteLine($"");

        // This test always passes - it's just for diagnostics
        true.Should().BeTrue();
    }

    /// <summary>
    /// Empirically derive the transformation formulas by comparing Visual and Content coordinates.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DeriveTransformationFormulas(int rotation)
    {
        var pdfPath = GetTempPath();
        TestPdfGenerator.CreateRotatedPdfVisualPosition(pdfPath, rotation, "X", visualX: 300, visualY: 400, fontSize: 24);

        _output.WriteLine($"");
        _output.WriteLine($"=== DERIVING FORMULAS FOR {rotation}° ===");
        _output.WriteLine($"Input: visual position (300, 400) from TOP-LEFT of displayed page");

        // Get PdfPig's visual coordinates (Y from bottom)
        using var pigDoc = PdfDocument.Open(pdfPath);
        var pigPage = pigDoc.GetPage(1);
        var letter = pigPage.Letters.First();
        var visualX = letter.GlyphRectangle.Left;
        var visualY = letter.GlyphRectangle.Bottom;

        _output.WriteLine($"");
        _output.WriteLine($"PdfPig visual (Y from bottom): ({visualX:F1}, {visualY:F1})");
        _output.WriteLine($"PdfPig page dimensions: {pigPage.Width} x {pigPage.Height}");

        // Get ContentStreamParser's content stream coordinates
        using var sharpDoc = PdfSharp.Pdf.IO.PdfReader.Open(pdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var sharpPage = sharpDoc.Pages[0];

        byte[]? contentBytes = null;
        var contents = sharpPage.Contents;
        if (contents != null && contents.Elements.Count > 0)
        {
            using var ms = new MemoryStream();
            for (int i = 0; i < contents.Elements.Count; i++)
            {
                var stream = contents.Elements.GetObject(i) as PdfSharp.Pdf.PdfDictionary;
                if (stream?.Stream?.Value != null)
                {
                    ms.Write(stream.Stream.Value, 0, stream.Stream.Value.Length);
                }
            }
            contentBytes = ms.ToArray();
        }

        var parser = new PdfEditor.Redaction.ContentStream.Parsing.ContentStreamParser();
        var operations = parser.Parse(contentBytes!, (double)sharpPage.Height.Point);
        var textOp = operations.OfType<PdfEditor.Redaction.TextOperation>().First();
        var contentX = textOp.BoundingBox.Left;
        var contentY = textOp.BoundingBox.Bottom;

        _output.WriteLine($"Content stream: ({contentX:F1}, {contentY:F1})");
        _output.WriteLine($"MediaBox: {sharpPage.MediaBox.Width} x {sharpPage.MediaBox.Height}");

        // Now derive the formula
        var mediaBoxWidth = sharpPage.MediaBox.Width;
        var mediaBoxHeight = sharpPage.MediaBox.Height;
        var (visualWidth, visualHeight) = rotation switch {
            90 or 270 => (mediaBoxHeight, mediaBoxWidth),
            _ => (mediaBoxWidth, mediaBoxHeight)
        };

        _output.WriteLine($"Visual dimensions: {visualWidth} x {visualHeight}");
        _output.WriteLine($"");
        _output.WriteLine($"FORMULA DERIVATION:");
        _output.WriteLine($"  contentX = {contentX:F1}");
        _output.WriteLine($"  visualX = {visualX:F1}, visualY = {visualY:F1}");
        _output.WriteLine($"");

        // Try various formulas
        _output.WriteLine($"Candidate formulas for contentX = {contentX:F1}:");
        _output.WriteLine($"  visualX                    = {visualX:F1}");
        _output.WriteLine($"  visualY                    = {visualY:F1}");
        _output.WriteLine($"  mediaBoxWidth - visualX    = {mediaBoxWidth - visualX:F1}");
        _output.WriteLine($"  mediaBoxHeight - visualX   = {mediaBoxHeight - visualX:F1}");
        _output.WriteLine($"  mediaBoxWidth - visualY    = {mediaBoxWidth - visualY:F1}");
        _output.WriteLine($"  mediaBoxHeight - visualY   = {mediaBoxHeight - visualY:F1}");
        _output.WriteLine($"  visualWidth - visualX      = {visualWidth - visualX:F1}");
        _output.WriteLine($"  visualHeight - visualY     = {visualHeight - visualY:F1}");
        _output.WriteLine($"");
        _output.WriteLine($"Candidate formulas for contentY = {contentY:F1}:");
        _output.WriteLine($"  visualX                    = {visualX:F1}");
        _output.WriteLine($"  visualY                    = {visualY:F1}");
        _output.WriteLine($"  mediaBoxWidth - visualX    = {mediaBoxWidth - visualX:F1}");
        _output.WriteLine($"  mediaBoxHeight - visualX   = {mediaBoxHeight - visualX:F1}");
        _output.WriteLine($"  mediaBoxWidth - visualY    = {mediaBoxWidth - visualY:F1}");
        _output.WriteLine($"  mediaBoxHeight - visualY   = {mediaBoxHeight - visualY:F1}");
        _output.WriteLine($"  visualWidth - visualX      = {visualWidth - visualX:F1}");
        _output.WriteLine($"  visualHeight - visualY     = {visualHeight - visualY:F1}");

        true.Should().BeTrue();
    }
}
