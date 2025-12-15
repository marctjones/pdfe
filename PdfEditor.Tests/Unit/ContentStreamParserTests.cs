using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for ContentStreamParser – verifies it can read simple PDFs and
/// returns the expected operation models without throwing.
/// </summary>
public class ContentStreamParserTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public ContentStreamParserTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfe_parser_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private static ContentStreamParser CreateParser() =>
        new ContentStreamParser(
            NullLogger<ContentStreamParser>.Instance,
            NullLoggerFactory.Instance);

    [Fact]
    public void ParseContentStream_SimpleTextPdf_ProducesTextOperation()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "parser_text.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLO WORLD");
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var parser = CreateParser();

        // Act
        var operations = parser.ParseContentStream(page);

        // Assert
        operations.Should().NotBeEmpty("a single text draw command should be parsed");
        var textOp = operations.OfType<TextOperation>().Should().ContainSingle().Subject;
        textOp.Text.Should().Contain("HELLO WORLD");
        textOp.BoundingBox.Width.Should().BeGreaterThan(0);
        textOp.BoundingBox.Height.Should().BeGreaterThan(0);
        _output.WriteLine($"Parsed text op bounding box: {textOp.BoundingBox}");
    }

    [Fact]
    public void ParseContentStream_TextWithGraphicsPdf_ContainsPathOperation()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "parser_graphics.pdf");
        TestPdfGenerator.CreateTextWithGraphicsPdf(pdfPath);
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = CreateParser();

        // Act
        var operations = parser.ParseContentStream(document.Pages[0]);

        // Assert
        operations.Should().Contain(o => o is PathOperation, "drawing rectangles should yield path operations");
        var pathOp = operations.OfType<PathOperation>().First();
        pathOp.Type.Should().NotBe(PathType.Unknown);
        pathOp.BoundingBox.Width.Should().BeGreaterThan(0);
        pathOp.BoundingBox.Height.Should().BeGreaterThan(0);
        _output.WriteLine($"Path op type: {pathOp.Type}, bbox={pathOp.BoundingBox}");
    }

    [Fact]
    public void ParseContentStream_BlankPage_ReturnsEmptyList()
    {
        // Arrange
        using var document = new PdfDocument();
        var page = document.AddPage(); // No content written
        var parser = CreateParser();

        // Act
        var operations = parser.ParseContentStream(page);

        // Assert
        operations.Should().BeEmpty("blank pages should not produce operations");
    }

    [Fact]
    public void ParseContentStream_WithNullPage_ThrowsArgumentNull()
    {
        var parser = CreateParser();

        Action act = () => parser.ParseContentStream(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Swallow cleanup exceptions – tests already completed.
        }
    }
}
