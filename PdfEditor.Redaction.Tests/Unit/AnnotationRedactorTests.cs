using FluentAssertions;
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Drawing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class AnnotationRedactorTests
{
    private readonly AnnotationRedactor _redactor = new();

    [Fact]
    public void RedactAnnotations_NoAnnotations_ReturnsZero()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        var area = new PdfRectangle(100, 100, 200, 200);

        // Act
        var result = _redactor.RedactAnnotations(page, new[] { area });

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void RedactAnnotations_EmptyAreas_ReturnsZero()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Act
        var result = _redactor.RedactAnnotations(page, Array.Empty<PdfRectangle>());

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetAnnotations_NoAnnotations_ReturnsEmptyList()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Act
        var result = _redactor.GetAnnotations(page);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllAnnotations_NoAnnotations_ReturnsZero()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Act
        var result = _redactor.RemoveAllAnnotations(page);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void RedactAnnotations_WithTextAnnotation_RemovesWhenIntersects()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Create a text annotation at a known location
        var annotRect = new XRect(100, 100, 50, 50);
        var annotation = new PdfTextAnnotation
        {
            Title = "Test Note",
            Contents = "This is a test annotation",
            Icon = PdfTextAnnotationIcon.Comment,
            Color = XColors.Yellow
        };
        annotation.Rectangle = new PdfSharp.Pdf.PdfRectangle(annotRect);
        page.Annotations.Add(annotation);

        // Verify annotation was added
        page.Annotations.Count.Should().Be(1);

        // Create a redaction area that intersects with the annotation
        var redactionArea = new PdfRectangle(90, 90, 160, 160);

        // Act
        var result = _redactor.RedactAnnotations(page, new[] { redactionArea });

        // Assert
        result.Should().Be(1);
        page.Annotations.Count.Should().Be(0);
    }

    [Fact]
    public void RedactAnnotations_WithTextAnnotation_DoesNotRemoveWhenNotIntersecting()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Create a text annotation at a known location
        var annotRect = new XRect(100, 100, 50, 50);
        var annotation = new PdfTextAnnotation
        {
            Title = "Test Note",
            Contents = "This is a test annotation"
        };
        annotation.Rectangle = new PdfSharp.Pdf.PdfRectangle(annotRect);
        page.Annotations.Add(annotation);

        // Verify annotation was added
        page.Annotations.Count.Should().Be(1);

        // Create a redaction area that does NOT intersect with the annotation
        var redactionArea = new PdfRectangle(500, 500, 600, 600);

        // Act
        var result = _redactor.RedactAnnotations(page, new[] { redactionArea });

        // Assert
        result.Should().Be(0);
        page.Annotations.Count.Should().Be(1);
    }

    [Fact]
    public void RemoveAllAnnotations_WithAnnotations_RemovesAll()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Add multiple annotations
        for (int i = 0; i < 3; i++)
        {
            var annotation = new PdfTextAnnotation
            {
                Title = $"Note {i}",
                Contents = $"Content {i}"
            };
            annotation.Rectangle = new PdfSharp.Pdf.PdfRectangle(new XRect(100 + i * 50, 100, 40, 40));
            page.Annotations.Add(annotation);
        }

        // Verify annotations were added
        page.Annotations.Count.Should().Be(3);

        // Act
        var result = _redactor.RemoveAllAnnotations(page);

        // Assert
        result.Should().Be(3);
        page.Annotations.Count.Should().Be(0);
    }

    [Fact]
    public void GetAnnotations_WithAnnotations_ReturnsInfo()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var annotation = new PdfTextAnnotation
        {
            Title = "Test Note"
        };
        annotation.Rectangle = new PdfSharp.Pdf.PdfRectangle(new XRect(100, 100, 50, 50));
        page.Annotations.Add(annotation);

        // Act
        var result = _redactor.GetAnnotations(page);

        // Assert
        result.Should().HaveCount(1);
        result[0].Index.Should().Be(0);
        result[0].Type.Should().Be("/Text");
        result[0].Rectangle.Should().NotBeNull();
    }
}
