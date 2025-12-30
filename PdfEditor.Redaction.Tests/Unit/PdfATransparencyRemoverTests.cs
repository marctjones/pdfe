using FluentAssertions;
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class PdfATransparencyRemoverTests
{
    private readonly PdfATransparencyRemover _remover;

    public PdfATransparencyRemoverTests()
    {
        _remover = new PdfATransparencyRemover();
    }

    [Fact]
    public void RemoveTransparency_EmptyDocument_ReturnsZero()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        var removed = _remover.RemoveTransparency(doc);

        // Assert
        removed.Should().Be(0);
    }

    [Fact]
    public void RemoveTransparency_PageWithTransparencyGroup_RemovesGroup()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Add transparency group to page
        var group = new PdfDictionary(doc);
        group.Elements.SetName("/Type", "/Group");
        group.Elements.SetName("/S", "/Transparency");
        page.Elements.SetObject("/Group", group);

        // Verify setup
        page.Elements.ContainsKey("/Group").Should().BeTrue();

        // Act
        var removed = _remover.RemoveTransparency(doc);

        // Assert
        removed.Should().Be(1);
        page.Elements.ContainsKey("/Group").Should().BeFalse();
    }

    [Fact]
    public void RemoveTransparency_PageWithNonTransparencyGroup_KeepsGroup()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Add non-transparency group (e.g., /OCProperties)
        var group = new PdfDictionary(doc);
        group.Elements.SetName("/Type", "/Group");
        group.Elements.SetName("/S", "/SomeOtherType");
        page.Elements.SetObject("/Group", group);

        // Act
        var removed = _remover.RemoveTransparency(doc);

        // Assert
        removed.Should().Be(0);
        page.Elements.ContainsKey("/Group").Should().BeTrue();
    }

    [Fact]
    public void HasTransparency_PageWithTransparencyGroup_ReturnsTrue()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var group = new PdfDictionary(doc);
        group.Elements.SetName("/Type", "/Group");
        group.Elements.SetName("/S", "/Transparency");
        page.Elements.SetObject("/Group", group);

        // Act
        var hasTransparency = _remover.HasTransparency(doc);

        // Assert
        hasTransparency.Should().BeTrue();
    }

    [Fact]
    public void HasTransparency_EmptyPage_ReturnsFalse()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        var hasTransparency = _remover.HasTransparency(doc);

        // Assert
        hasTransparency.Should().BeFalse();
    }

    [Fact]
    public void PageHasTransparency_WithTransparencyGroup_ReturnsTrue()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var group = new PdfDictionary(doc);
        group.Elements.SetName("/S", "/Transparency");
        page.Elements.SetObject("/Group", group);

        // Act
        var hasTransparency = _remover.PageHasTransparency(page);

        // Assert
        hasTransparency.Should().BeTrue();
    }

    [Fact]
    public void RemoveTransparencyFromPage_RemovesTransparencyGroup()
    {
        // Arrange
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var group = new PdfDictionary(doc);
        group.Elements.SetName("/S", "/Transparency");
        page.Elements.SetObject("/Group", group);

        // Act
        var removed = _remover.RemoveTransparencyFromPage(page);

        // Assert
        removed.Should().Be(1);
        page.Elements.ContainsKey("/Group").Should().BeFalse();
    }

    [Fact]
    public void RemoveTransparency_MultiplePages_RemovesAllTransparency()
    {
        // Arrange
        using var doc = new PdfDocument();

        for (int i = 0; i < 3; i++)
        {
            var page = doc.AddPage();
            var group = new PdfDictionary(doc);
            group.Elements.SetName("/S", "/Transparency");
            page.Elements.SetObject("/Group", group);
        }

        // Act
        var removed = _remover.RemoveTransparency(doc);

        // Assert
        removed.Should().Be(3);
        foreach (var page in doc.Pages)
        {
            page.Elements.ContainsKey("/Group").Should().BeFalse();
        }
    }

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act & Assert
        var remover = new PdfATransparencyRemover();
        remover.Should().NotBeNull();
    }

    [Fact]
    public void RemoveTransparency_DocumentWithMixedPages_OnlyRemovesTransparency()
    {
        // Arrange
        using var doc = new PdfDocument();

        // Page with transparency
        var page1 = doc.AddPage();
        var group1 = new PdfDictionary(doc);
        group1.Elements.SetName("/S", "/Transparency");
        page1.Elements.SetObject("/Group", group1);

        // Page without transparency
        doc.AddPage();

        // Page with non-transparency group
        var page3 = doc.AddPage();
        var group3 = new PdfDictionary(doc);
        group3.Elements.SetName("/S", "/Other");
        page3.Elements.SetObject("/Group", group3);

        // Act
        var removed = _remover.RemoveTransparency(doc);

        // Assert
        removed.Should().Be(1);
        page1.Elements.ContainsKey("/Group").Should().BeFalse();
        page3.Elements.ContainsKey("/Group").Should().BeTrue();
    }
}
