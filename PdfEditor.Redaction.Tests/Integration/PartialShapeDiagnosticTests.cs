using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Diagnostic tests to verify partial shape redaction is actually working.
/// These tests dump content streams before/after to verify path operators are being modified.
/// </summary>
public class PartialShapeDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public PartialShapeDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"shape_diag_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Keep files for inspection
        _output.WriteLine($"Test files at: {_tempDir}");
    }

    [Fact]
    public void DiagnosePartialShapeRedaction_DumpContentStreams()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");

        // Create PDF with blue rectangle at (100,400) to (300,600) - 200x200 points
        _output.WriteLine("=== Creating Test PDF ===");
        TestPdfGenerator.CreateRectanglesPdf(inputPath, (100, 400, 200, 200, XColors.Blue));
        _output.WriteLine($"Rectangle: (100,400) to (300,600) - 200x200 points");

        // Dump original content stream
        _output.WriteLine("\n=== Original Content Stream ===");
        var originalContent = GetContentStreamText(inputPath);
        _output.WriteLine(originalContent);

        // Count path operators in original
        var originalReCount = CountOperator(originalContent, "re");
        var originalMCount = CountOperator(originalContent, " m\n");
        var originalLCount = CountOperator(originalContent, " l\n");
        var originalFCount = CountOperator(originalContent, " f\n") + CountOperator(originalContent, " f*\n");
        _output.WriteLine($"\nOriginal operators: re={originalReCount}, m={originalMCount}, l={originalLCount}, f={originalFCount}");

        // Perform redaction - right half of the rectangle
        _output.WriteLine("\n=== Performing Redaction ===");
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 400, 350, 600)  // Right half + extra
        };
        _output.WriteLine($"Redaction area: (200,400) to (350,600) - covers right half of rectangle");

        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        _output.WriteLine($"Success: {result.Success}");

        // Dump redacted content stream
        _output.WriteLine("\n=== Redacted Content Stream ===");
        var redactedContent = GetContentStreamText(outputPath);
        _output.WriteLine(redactedContent);

        // Count path operators in redacted
        var redactedReCount = CountOperator(redactedContent, "re");
        var redactedMCount = CountOperator(redactedContent, " m\n");
        var redactedLCount = CountOperator(redactedContent, " l\n");
        var redactedFCount = CountOperator(redactedContent, " f\n") + CountOperator(redactedContent, " f*\n");
        _output.WriteLine($"\nRedacted operators: re={redactedReCount}, m={redactedMCount}, l={redactedLCount}, f={redactedFCount}");

        // Analysis
        _output.WriteLine("\n=== Analysis ===");

        // If partial clipping worked, we should see:
        // - The original 're' operator replaced with 'm', 'l', 'h', 'f' operators
        // - The new polygon should represent the left half

        if (redactedMCount > originalMCount)
        {
            _output.WriteLine("GOOD: More 'm' operators in redacted - suggests path was reconstructed");
        }
        else if (redactedReCount == originalReCount)
        {
            _output.WriteLine("BAD: Same number of 're' operators - original rectangle may be unchanged");
        }

        // Check if content changed at all
        if (originalContent == redactedContent)
        {
            _output.WriteLine("FAIL: Content stream is IDENTICAL - no redaction occurred!");
        }
        else
        {
            _output.WriteLine("Content stream was modified");
        }

        // Look for the clipped polygon coordinates
        // Expected: left half should have x coordinates around 100-200, not 300
        if (redactedContent.Contains("300") && !redactedContent.Contains("black box"))
        {
            _output.WriteLine("WARNING: Content still contains '300' - right edge may not be clipped");
        }

        _output.WriteLine($"\n=== Files for manual inspection ===");
        _output.WriteLine($"Input:  {inputPath}");
        _output.WriteLine($"Output: {outputPath}");
        _output.WriteLine($"View: pdftotext {outputPath} - && cat {outputPath}");

        // Assertions
        result.Success.Should().BeTrue();
        originalContent.Should().NotBe(redactedContent, "Content stream should be modified by redaction");
    }

    [Fact]
    public void DiagnosePathCollector_FindsRectangle()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "collector_test.pdf");
        TestPdfGenerator.CreateRectanglesPdf(inputPath, (100, 400, 200, 200, XColors.Blue));

        // Parse the content stream
        var parser = new PdfEditor.Redaction.ContentStream.Parsing.ContentStreamParser();
        var contentBytes = GetContentStreamBytes(inputPath);
        var operations = parser.Parse(contentBytes, 792);  // US Letter height

        _output.WriteLine($"Parsed {operations.Count} operations:");
        foreach (var op in operations)
        {
            _output.WriteLine($"  {op.GetType().Name}: {op.Operator} at pos {op.StreamPosition}");
            if (op is PdfEditor.Redaction.PathOperation pathOp)
            {
                _output.WriteLine($"    Type: {pathOp.Type}, BBox: ({pathOp.BoundingBox.Left:F1},{pathOp.BoundingBox.Bottom:F1}) to ({pathOp.BoundingBox.Right:F1},{pathOp.BoundingBox.Top:F1})");
            }
        }

        // Use PathCollector
        var collector = new PdfEditor.Redaction.PathClipping.PathCollector();
        var paths = collector.CollectPaths(operations);

        _output.WriteLine($"\nCollected {paths.Count} complete paths:");
        foreach (var path in paths)
        {
            _output.WriteLine($"  IsRectangle: {path.IsRectangle}, PaintType: {path.PaintType}");
            _output.WriteLine($"  Subpaths: {path.Subpaths.Count}");
            foreach (var subpath in path.Subpaths)
            {
                _output.WriteLine($"    Points: {subpath.Count}");
                foreach (var pt in subpath.Take(5))
                {
                    _output.WriteLine($"      ({pt.X:F1}, {pt.Y:F1})");
                }
                if (subpath.Count > 5) _output.WriteLine($"      ... and {subpath.Count - 5} more");
            }
        }

        paths.Should().NotBeEmpty("Should find at least one path (the rectangle)");
    }

    private string GetContentStreamText(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];

        var sb = new StringBuilder();
        for (int i = 0; i < page.Contents.Elements.Count; i++)
        {
            var content = page.Contents.Elements.GetObject(i) as PdfSharp.Pdf.PdfDictionary;
            if (content?.Stream != null)
            {
                var bytes = content.Stream.Value;
                sb.AppendLine(Encoding.ASCII.GetString(bytes));
            }
        }
        return sb.ToString();
    }

    private byte[] GetContentStreamBytes(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];

        if (page.Contents.Elements.Count > 0)
        {
            var content = page.Contents.Elements.GetObject(0) as PdfSharp.Pdf.PdfDictionary;
            if (content?.Stream != null)
            {
                return content.Stream.Value;
            }
        }
        return Array.Empty<byte>();
    }

    private int CountOperator(string content, string op)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(op, index)) != -1)
        {
            count++;
            index += op.Length;
        }
        return count;
    }
}
