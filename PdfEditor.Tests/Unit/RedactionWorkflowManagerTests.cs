using System;
using System.Linq;
using Avalonia;
using FluentAssertions;
using PdfEditor.ViewModels;
using Xunit;

namespace PdfEditor.Tests.Unit;

public class RedactionWorkflowManagerTests
{
    [Fact]
    public void Constructor_InitializesEmptyCollections()
    {
        // Act
        var manager = new RedactionWorkflowManager();

        // Assert
        manager.PendingRedactions.Should().BeEmpty();
        manager.AppliedRedactions.Should().BeEmpty();
        manager.PendingCount.Should().Be(0);
        manager.AppliedCount.Should().Be(0);
    }

    [Fact]
    public void MarkArea_AddsToCollection()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();

        // Act
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Test text");

        // Assert
        manager.PendingCount.Should().Be(1);
        var pending = manager.PendingRedactions.First();
        pending.PageNumber.Should().Be(1);
        pending.Area.Should().Be(new Rect(10, 10, 100, 50));
        pending.PreviewText.Should().Be("Test text");
    }

    [Fact]
    public void MarkArea_GeneratesUniqueIds()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();

        // Act
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Text 1");
        manager.MarkArea(1, new Rect(20, 20, 100, 50), "Text 2");

        // Assert
        var ids = manager.PendingRedactions.Select(p => p.Id).ToList();
        ids.Should().HaveCount(2);
        ids[0].Should().NotBe(ids[1]);
    }

    [Fact]
    public void RemovePending_RemovesById()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Test");
        var id = manager.PendingRedactions.First().Id;

        // Act
        var removed = manager.RemovePending(id);

        // Assert
        removed.Should().BeTrue();
        manager.PendingCount.Should().Be(0);
    }

    [Fact]
    public void RemovePending_WithInvalidId_ReturnsFalse()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Test");

        // Act
        var removed = manager.RemovePending(Guid.NewGuid());

        // Assert
        removed.Should().BeFalse();
        manager.PendingCount.Should().Be(1);
    }

    [Fact]
    public void ClearPending_RemovesAll()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Text 1");
        manager.MarkArea(2, new Rect(20, 20, 100, 50), "Text 2");

        // Act
        manager.ClearPending();

        // Assert
        manager.PendingCount.Should().Be(0);
    }

    [Fact]
    public void MoveToApplied_TransfersItems()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Text 1");
        manager.MarkArea(2, new Rect(20, 20, 100, 50), "Text 2");

        // Act
        manager.MoveToApplied();

        // Assert
        manager.PendingCount.Should().Be(0);
        manager.AppliedCount.Should().Be(2);
        manager.AppliedRedactions.Select(a => a.PreviewText).Should().Contain(new[] { "Text 1", "Text 2" });
    }

    [Fact]
    public void GetPendingForPage_FiltersByPage()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Page 1 Text");
        manager.MarkArea(2, new Rect(20, 20, 100, 50), "Page 2 Text");
        manager.MarkArea(1, new Rect(30, 30, 100, 50), "Page 1 Text 2");

        // Act
        var page1Pending = manager.GetPendingForPage(1).ToList();

        // Assert
        page1Pending.Should().HaveCount(2);
        page1Pending.All(p => p.PageNumber == 1).Should().BeTrue();
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        // Arrange
        var manager = new RedactionWorkflowManager();
        manager.MarkArea(1, new Rect(10, 10, 100, 50), "Pending");
        manager.MoveToApplied();
        manager.MarkArea(2, new Rect(20, 20, 100, 50), "New Pending");

        // Act
        manager.Reset();

        // Assert
        manager.PendingCount.Should().Be(0);
        manager.AppliedCount.Should().Be(0);
    }
}
