using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for ContentStreamBuilder – ensures operations are serialized back to bytes correctly.
/// </summary>
public class ContentStreamBuilderTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public ContentStreamBuilderTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfe_builder_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private static ContentStreamParser CreateParser() =>
        new ContentStreamParser(
            NullLogger<ContentStreamParser>.Instance,
            NullLoggerFactory.Instance);

    private static ContentStreamBuilder CreateBuilder() =>
        new ContentStreamBuilder(NullLogger<ContentStreamBuilder>.Instance);

    [Fact]
    public void BuildContentStream_FromParsedOperations_ShouldIncludeOriginalText()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "builder_text.pdf");
        const string sampleText = "Builder Round Trip";
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, sampleText);
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = CreateParser();
        var operations = parser.ParseContentStream(document.Pages[0]);
        var builder = CreateBuilder();

        // Act
        var bytes = builder.BuildContentStream(operations);
        var content = Encoding.ASCII.GetString(bytes);
        _output.WriteLine("Serialized content stream:");
        _output.WriteLine(content);

        // Assert
        content.Should().Contain(sampleText, "serialized stream should include original text literal");
        content.Should().Contain("Tj", "text showing operator should be preserved");
    }

    [Fact]
    public void BuildContentStream_WithNoOperations_ReturnsEmptyByteArray()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var bytes = builder.BuildContentStream(new List<PdfOperation>());

        // Assert
        bytes.Should().BeEmpty("serializing zero operations should produce no bytes");
    }

    [Fact]
    public void BuildContentStream_InlineImageOperation_WritesRawBytes()
    {
        // Arrange
        var rawInlineImage = "BI /W 1 /H 1 /BPC 8 ID \u0000 EI";
        var inlineOperation = new InlineImageOperation(
            Encoding.ASCII.GetBytes(rawInlineImage),
            bounds: default,
            position: 0,
            length: rawInlineImage.Length);

        var builder = CreateBuilder();

        // Act
        var bytes = builder.BuildContentStream(new List<PdfOperation> { inlineOperation });
        var content = Encoding.ASCII.GetString(bytes).Trim();

        // Assert
        content.Should().Be(rawInlineImage, "inline image bytes must be preserved exactly");
    }

    [Fact]
    public void BuildContentStream_GenericOperationWithoutOperator_IsIgnored()
    {
        // Arrange
        var builder = CreateBuilder();
        var operations = new List<PdfOperation>
        {
            new GenericOperation(new PdfSharp.Pdf.Content.Objects.CSequence(), "noop")
        };

        // Act
        var bytes = builder.BuildContentStream(operations);

        // Assert
        bytes.Should().BeEmpty();
    }

    [Fact]
    public void BuildContentStream_WithNullOperations_Throws()
    {
        var builder = CreateBuilder();

        Action act = () => builder.BuildContentStream(null!);

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
            // Ignore cleanup failures.
        }
    }
}
