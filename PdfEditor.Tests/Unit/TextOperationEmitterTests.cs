using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services.Redaction;
using PdfSharp.Pdf.Content.Objects;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Tests for TextOperationEmitter - verifies PDF byte generation for partial operations
/// </summary>
public class TextOperationEmitterTests
{
    private readonly TextOperationEmitter _emitter;
    private readonly Mock<ILogger<TextOperationEmitter>> _loggerMock;

    public TextOperationEmitterTests()
    {
        _loggerMock = new Mock<ILogger<TextOperationEmitter>>();
        _emitter = new TextOperationEmitter(_loggerMock.Object);
    }

    [Fact]
    public void EmitPartialOperation_SimpleText_GeneratesCorrectBytes()
    {
        // Arrange
        var run = new CharacterRun("HELLO")
        {
            StartIndex = 0,
            EndIndex = 5,
            Keep = true,
            StartPosition = new Point(100, 700),
            Width = 50
        };

        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(100, 92, 50, 12)
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var bytes = _emitter.EmitPartialOperation(run, textOp, letterMap, 792);

        // Assert
        bytes.Should().NotBeEmpty();

        var pdfContent = Encoding.ASCII.GetString(bytes);
        pdfContent.Should().Contain("100.00 700.00 Td", "should position text at (100, 700)");
        pdfContent.Should().Contain("(HELLO) Tj", "should emit text operation");
    }

    [Fact]
    public void EmitPartialOperation_EmptyRun_ReturnsEmpty()
    {
        // Arrange
        var run = new CharacterRun("HELLO")
        {
            StartIndex = 0,
            EndIndex = 0,  // Empty run
            Keep = true,
            StartPosition = new Point(100, 700),
            Width = 0
        };

        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect()
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var bytes = _emitter.EmitPartialOperation(run, textOp, letterMap, 792);

        // Assert
        bytes.Should().BeEmpty("empty run should produce no bytes");
    }

    [Fact]
    public void EmitPartialOperation_TextWithParentheses_EscapesCorrectly()
    {
        // Arrange
        var run = new CharacterRun("(TEST)")
        {
            StartIndex = 0,
            EndIndex = 6,
            Keep = true,
            StartPosition = new Point(100, 700),
            Width = 40
        };

        var textOp = new TextOperation(new CComment())
        {
            Text = "(TEST)",
            BoundingBox = new Rect()
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var bytes = _emitter.EmitPartialOperation(run, textOp, letterMap, 792);

        // Assert
        var pdfContent = Encoding.ASCII.GetString(bytes);
        pdfContent.Should().Contain("(\\(TEST\\)) Tj", "parentheses should be escaped");
        pdfContent.Should().NotContain("((TEST)) Tj", "unescaped parentheses would break PDF syntax");
    }

    [Fact]
    public void EmitPartialOperation_TextWithBackslash_EscapesCorrectly()
    {
        // Arrange
        var run = new CharacterRun("C:\\PATH")
        {
            StartIndex = 0,
            EndIndex = 7,
            Keep = true,
            StartPosition = new Point(100, 700),
            Width = 60
        };

        var textOp = new TextOperation(new CComment())
        {
            Text = "C:\\PATH",
            BoundingBox = new Rect()
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var bytes = _emitter.EmitPartialOperation(run, textOp, letterMap, 792);

        // Assert
        var pdfContent = Encoding.ASCII.GetString(bytes);
        pdfContent.Should().Contain("(C:\\\\PATH) Tj", "backslash should be escaped");
    }

    [Fact]
    public void EmitPartialOperation_TextWithNewline_EscapesCorrectly()
    {
        // Arrange
        var run = new CharacterRun("LINE1\nLINE2")
        {
            StartIndex = 0,
            EndIndex = 11,
            Keep = true,
            StartPosition = new Point(100, 700),
            Width = 80
        };

        var textOp = new TextOperation(new CComment())
        {
            Text = "LINE1\nLINE2",
            BoundingBox = new Rect()
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var bytes = _emitter.EmitPartialOperation(run, textOp, letterMap, 792);

        // Assert
        var pdfContent = Encoding.ASCII.GetString(bytes);
        pdfContent.Should().Contain("\\n", "newline should be escaped");
    }

    [Fact]
    public void EmitOperations_MultipleRuns_GeneratesMultiplePartials()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "FIRSTMIDDLELAST",
            BoundingBox = new Rect(100, 92, 150, 12)
        };

        var runs = new List<CharacterRun>
        {
            new CharacterRun(textOp.Text)
            {
                StartIndex = 0,
                EndIndex = 5,
                Keep = true,
                StartPosition = new Point(100, 700),
                Width = 50
            },
            new CharacterRun(textOp.Text)
            {
                StartIndex = 5,
                EndIndex = 11,
                Keep = false,  // This run should be skipped
                StartPosition = new Point(150, 700),
                Width = 60
            },
            new CharacterRun(textOp.Text)
            {
                StartIndex = 11,
                EndIndex = 15,
                Keep = true,
                StartPosition = new Point(210, 700),
                Width = 40
            }
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var operations = _emitter.EmitOperations(runs, textOp, letterMap, 792);

        // Assert
        operations.Should().HaveCount(2, "only Keep=true runs should be emitted");

        operations[0].DisplayText.Should().Be("FIRST");
        operations[0].RawBytes.Should().NotBeEmpty();

        operations[1].DisplayText.Should().Be("LAST");
        operations[1].RawBytes.Should().NotBeEmpty();

        // Verify both have proper PDF content
        var firstPdf = Encoding.ASCII.GetString(operations[0].RawBytes);
        firstPdf.Should().Contain("(FIRST) Tj");

        var lastPdf = Encoding.ASCII.GetString(operations[1].RawBytes);
        lastPdf.Should().Contain("(LAST) Tj");
    }

    [Fact]
    public void EmitOperations_NoKeepRuns_ReturnsEmpty()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect()
        };

        var runs = new List<CharacterRun>
        {
            new CharacterRun(textOp.Text)
            {
                StartIndex = 0,
                EndIndex = 5,
                Keep = false,  // All removed
                StartPosition = new Point(100, 700),
                Width = 50
            }
        };

        var letterMap = new Dictionary<int, Letter>();

        // Act
        var operations = _emitter.EmitOperations(runs, textOp, letterMap, 792);

        // Assert
        operations.Should().BeEmpty("no Keep=true runs means no operations");
    }

    [Fact]
    public void CalculateCharacterPosition_WithLetterMatch_ReturnsLetterPosition()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            Position = new Point(100, 700)
        };

        // Use a real letter from a PDF to avoid constructor issues
        var tempDir = Path.GetTempPath() + "emitter_test_" + Guid.NewGuid();
        Directory.CreateDirectory(tempDir);

        try
        {
            var pdfPath = Path.Combine(tempDir, "test.pdf");
            Utilities.TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "H");

            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var page = doc.GetPage(1);
            var letter = page.Letters.First();

            var letterMap = new Dictionary<int, Letter>
            {
                { 0, letter }
            };

            // Act
            var position = _emitter.CalculateCharacterPosition(textOp, 0, letterMap);

            // Assert
            position.X.Should().Be(letter.GlyphRectangle.Left, "should use letter's left edge");
            position.Y.Should().Be(letter.GlyphRectangle.Bottom, "should use letter's bottom edge");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    [Fact]
    public void CalculateCharacterPosition_NoLetterMatch_ReturnsFallback()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            Position = new Point(100, 700)
        };

        var letterMap = new Dictionary<int, Letter>();  // Empty - no match

        // Act
        var position = _emitter.CalculateCharacterPosition(textOp, 0, letterMap);

        // Assert
        position.Should().Be(textOp.Position, "should fall back to operation position");
    }

    [Fact]
    public void EmitPartialOperation_BoundingBoxCalculation_IsCorrect()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(100, 92, 50, 12)
        };

        var runs = new List<CharacterRun>
        {
            new CharacterRun(textOp.Text)
            {
                StartIndex = 0,
                EndIndex = 5,
                Keep = true,
                StartPosition = new Point(100, 700),
                Width = 50
            }
        };

        var letterMap = new Dictionary<int, Letter>();
        var pageHeight = 792.0;

        // Act
        var operations = _emitter.EmitOperations(runs, textOp, letterMap, pageHeight);

        // Assert
        operations.Should().HaveCount(1);

        var op = operations[0];
        op.BoundingBox.X.Should().Be(100, "X should match run start position");
        op.BoundingBox.Width.Should().Be(50, "width should match run width");
        op.BoundingBox.Height.Should().Be(12, "height should match original operation");

        // Y coordinate conversion: Avalonia Y = pageHeight - PDF Y - height
        var expectedY = pageHeight - runs[0].StartPosition.Y - textOp.BoundingBox.Height;
        op.BoundingBox.Y.Should().BeApproximately(expectedY, 0.01, "Y should be converted from PDF to Avalonia coordinates");
    }
}
