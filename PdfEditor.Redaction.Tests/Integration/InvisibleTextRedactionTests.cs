using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests for invisible text (Tr mode 3) redaction.
/// Invisible text is a security concern - it must be removed during redaction
/// even though it's not visible to users.
/// </summary>
/// <remarks>
/// Addresses issue #168: Invisible text integration test
/// Follows up on issue #83: Tr operator implementation
/// </remarks>
public class InvisibleTextRedactionTests
{
    private readonly ITestOutputHelper _output;

    public InvisibleTextRedactionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TrOperator_IsParsedCorrectly_InContentStream()
    {
        // Arrange: Content stream with invisible text (Tr mode 3)
        var contentStream = @"
BT
/F1 12 Tf
3 Tr
100 700 Td
(HIDDEN SENSITIVE DATA) Tj
ET
";
        var bytes = System.Text.Encoding.ASCII.GetBytes(contentStream);

        // Act: Parse the content stream
        var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
        var operations = parser.Parse(bytes, 792.0);

        // Assert: Tr operator should be parsed and state should be tracked
        var trOps = operations.Where(op => op is TextStateOperation tso && tso.Operator == "Tr").ToList();
        trOps.Should().HaveCount(1, "There should be one Tr operator");

        var textOps = operations.OfType<TextOperation>().ToList();
        textOps.Should().HaveCount(1, "There should be one text operation");
        textOps[0].Text.Should().Be("HIDDEN SENSITIVE DATA");

        _output.WriteLine($"Parsed {operations.Count} operations");
        _output.WriteLine($"Tr operations: {trOps.Count}");
        _output.WriteLine($"Text: '{textOps[0].Text}'");
    }

    [Fact]
    public void ContentStreamParser_TracksTextRenderingMode()
    {
        // Arrange: Content stream that changes Tr mode
        var contentStream = @"
BT
/F1 12 Tf
0 Tr
100 700 Td
(VISIBLE) Tj
3 Tr
100 680 Td
(INVISIBLE) Tj
0 Tr
100 660 Td
(VISIBLE AGAIN) Tj
ET
";
        var bytes = System.Text.Encoding.ASCII.GetBytes(contentStream);

        // Act: Parse the content stream
        var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
        var operations = parser.Parse(bytes, 792.0);

        // Assert: All three text operations should be parsed
        var textOps = operations.OfType<TextOperation>().ToList();
        textOps.Should().HaveCount(3);

        textOps[0].Text.Should().Be("VISIBLE");
        textOps[1].Text.Should().Be("INVISIBLE");
        textOps[2].Text.Should().Be("VISIBLE AGAIN");

        _output.WriteLine("Parsed text operations:");
        foreach (var op in textOps)
        {
            _output.WriteLine($"  - '{op.Text}'");
        }
    }

    [Fact]
    public void TrOperator_IsPreservedInContentStreamBuilder()
    {
        // Arrange: Parse and rebuild a content stream with Tr operator
        var originalContent = @"
BT
/F1 12 Tf
3 Tr
100 700 Td
(HIDDEN TEXT) Tj
ET
";
        var bytes = System.Text.Encoding.ASCII.GetBytes(originalContent);

        var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
        var operations = parser.Parse(bytes, 792.0);

        // Act: Rebuild the content stream
        var builder = new ContentStreamBuilder();
        var rebuiltBytes = builder.Build(operations);
        var rebuiltContent = System.Text.Encoding.ASCII.GetString(rebuiltBytes);

        _output.WriteLine($"Rebuilt content stream:\n{rebuiltContent}");

        // Assert: Tr operator should be in the rebuilt content
        rebuiltContent.Should().Contain("3 Tr", "Tr operator should be preserved in rebuilt content");
        rebuiltContent.Should().Contain("HIDDEN TEXT", "Text should be preserved");
    }

    [Fact]
    public void InvisibleText_IsIncludedInRedactionFiltering()
    {
        // Arrange: Content stream with invisible text that should be removed
        var contentStream = @"
BT
/F1 12 Tf
3 Tr
100 700 Td
(HIDDEN SENSITIVE) Tj
ET
";
        var bytes = System.Text.Encoding.ASCII.GetBytes(contentStream);

        var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
        var operations = parser.Parse(bytes, 792.0);

        // Get the text operation
        var textOps = operations.OfType<TextOperation>().ToList();
        textOps.Should().HaveCount(1);

        var textOp = textOps[0];

        // Act: Check if the text operation has a bounding box (needed for filtering)
        _output.WriteLine($"Text: '{textOp.Text}'");
        _output.WriteLine($"BoundingBox: ({textOp.BoundingBox.Left:F1},{textOp.BoundingBox.Bottom:F1})-({textOp.BoundingBox.Right:F1},{textOp.BoundingBox.Top:F1})");

        // Assert: Text operation should have valid bounding box for intersection testing
        textOp.BoundingBox.Width.Should().BeGreaterThan(0, "Text should have width");
        textOp.BoundingBox.Height.Should().BeGreaterThan(0, "Text should have height");

        // Check intersection with a redaction area
        var redactionArea = new PdfRectangle(90, 690, 300, 720);
        var intersects = textOp.BoundingBox.IntersectsWith(redactionArea);
        intersects.Should().BeTrue("Invisible text should intersect with redaction area for removal");
    }

    [Fact]
    public void TrOperator_AllModes_AreParsed()
    {
        // Test all 8 text rendering modes (0-7)
        for (int mode = 0; mode <= 7; mode++)
        {
            var contentStream = $@"
BT
/F1 12 Tf
{mode} Tr
100 700 Td
(Mode {mode}) Tj
ET
";
            var bytes = System.Text.Encoding.ASCII.GetBytes(contentStream);

            var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
            var operations = parser.Parse(bytes, 792.0);

            var trOps = operations.Where(op => op is TextStateOperation tso && tso.Operator == "Tr").ToList();
            trOps.Should().HaveCount(1, $"Mode {mode} should be parsed");

            var textOps = operations.OfType<TextOperation>().ToList();
            textOps.Should().HaveCount(1);
            textOps[0].Text.Should().Be($"Mode {mode}");
        }

        _output.WriteLine("All 8 text rendering modes (0-7) parsed successfully");
    }

    [Fact]
    public void RealWorldScenario_OcrLayerText_CanBeRedacted()
    {
        // This test simulates a common real-world scenario:
        // OCR layers often use invisible text (Tr mode 3) positioned over scanned images
        // to make the text searchable/selectable without being visible.

        // Arrange: Simulate an OCR layer with invisible text
        var ocrContent = @"
q
% Image would be here
100 600 400 200 re W n
Q
BT
/F1 10 Tf
3 Tr
110 780 Td
(CONFIDENTIAL DOCUMENT) Tj
0 -12 Td
(Patient Name: John Doe) Tj
0 -12 Td
(SSN: 123-45-6789) Tj
ET
";
        var bytes = System.Text.Encoding.ASCII.GetBytes(ocrContent);

        // Act: Parse the content
        var parser = new ContentStreamParser(OperatorRegistry.CreateDefault());
        var operations = parser.Parse(bytes, 792.0);

        // Assert: All text operations should be parsed even though they're invisible
        var textOps = operations.OfType<TextOperation>().ToList();
        textOps.Should().HaveCount(3, "All OCR text should be parsed");

        _output.WriteLine("OCR layer text found:");
        foreach (var op in textOps)
        {
            _output.WriteLine($"  '{op.Text}' at ({op.BoundingBox.Left:F1}, {op.BoundingBox.Bottom:F1})");
        }

        // Check that sensitive data can be identified for redaction
        textOps.Should().Contain(op => op.Text.Contains("SSN"),
            "Sensitive OCR text should be parseable for redaction");
    }
}
