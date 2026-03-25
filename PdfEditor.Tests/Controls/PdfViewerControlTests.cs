using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using PdfEditor.Controls;
using PdfEditor.Tests.Utilities;
using System.Threading.Tasks;
using Xunit;
using PdfCoreDocument = Pdfe.Core.Document.PdfDocument;

namespace PdfEditor.Tests.Controls;

/// <summary>
/// Basic GUI verification tests for PdfViewerControl.
/// Ensures the control can be instantiated, loaded with documents,
/// and responds correctly to zoom/navigation commands.
/// </summary>
[Collection("AvaloniaTests")]
public class PdfViewerControlTests
{
    private const string TestPdfPath = "TestData/simple.pdf";

    #region Instantiation Tests

    [AvaloniaFact]
    public void PdfViewerControl_CanBeInstantiated()
    {
        // Act
        var control = new PdfViewerControl();

        // Assert
        control.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void PdfViewerControl_HasDefaultProperties()
    {
        // Arrange & Act
        var control = new PdfViewerControl();

        // Assert
        control.ZoomLevel.Should().Be(1.0);
        control.CurrentPage.Should().Be(1);
        control.InteractionMode.Should().Be(InteractionMode.None);
        control.Document.Should().BeNull();
    }

    #endregion

    #region Document Loading Tests

    [AvaloniaFact]
    public async Task PdfViewerControl_CanLoadDocument()
    {
        // Arrange
        var pdfPath = TestPdfGenerator.CreateSimplePdf("PdfViewerControlTests_LoadDoc");
        var control = new PdfViewerControl();

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = PdfCoreDocument.Open(pdfPath);
            control.Document = doc;
        });

        // Give time for rendering
        await Task.Delay(500);

        // Assert
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.Document.Should().NotBeNull();
            control.Document!.PageCount.Should().BeGreaterThan(0);
        });

        // Cleanup
        control.Document?.Dispose();
    }

    [AvaloniaFact]
    public async Task PdfViewerControl_LoadMultiPageDocument_ShowsPageCount()
    {
        // Arrange
        var pdfPath = TestPdfGenerator.CreateMultiPagePdf("PdfViewerControlTests_MultiPage", pageCount: 3);
        var control = new PdfViewerControl();

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = PdfCoreDocument.Open(pdfPath);
            control.Document = doc;
        });

        await Task.Delay(500);

        // Assert
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.Document.Should().NotBeNull();
            control.Document!.PageCount.Should().Be(3);
        });

        // Cleanup
        control.Document?.Dispose();
    }

    #endregion

    #region Zoom Tests

    [AvaloniaFact]
    public void PdfViewerControl_ZoomIn_IncreasesZoomLevel()
    {
        // Arrange
        var control = new PdfViewerControl();
        var initialZoom = control.ZoomLevel;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            control.ZoomLevel = initialZoom * 1.25; // Simulate zoom in
        });

        // Assert
        control.ZoomLevel.Should().BeGreaterThan(initialZoom);
    }

    [AvaloniaFact]
    public void PdfViewerControl_ZoomOut_DecreasesZoomLevel()
    {
        // Arrange
        var control = new PdfViewerControl();
        control.ZoomLevel = 2.0;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            control.ZoomLevel = 2.0 / 1.25; // Simulate zoom out
        });

        // Assert
        control.ZoomLevel.Should().BeLessThan(2.0);
    }

    [AvaloniaFact]
    public void PdfViewerControl_ZoomLevel_CanBeSetDirectly()
    {
        // Arrange
        var control = new PdfViewerControl();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            control.ZoomLevel = 1.5;
        });

        // Assert
        control.ZoomLevel.Should().Be(1.5);
    }

    #endregion

    #region Page Navigation Tests

    [AvaloniaFact]
    public async Task PdfViewerControl_CurrentPage_CanBeChanged()
    {
        // Arrange
        var pdfPath = TestPdfGenerator.CreateMultiPagePdf("PdfViewerControlTests_Navigation", pageCount: 3);
        var control = new PdfViewerControl();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = PdfCoreDocument.Open(pdfPath);
            control.Document = doc;
        });

        await Task.Delay(500);

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.CurrentPage = 2;
        });

        // Assert
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.CurrentPage.Should().Be(2);
        });

        // Cleanup
        control.Document?.Dispose();
    }

    [AvaloniaFact]
    public async Task PdfViewerControl_CanNavigateToLastPage()
    {
        // Arrange
        var pdfPath = TestPdfGenerator.CreateMultiPagePdf("PdfViewerControlTests_LastPage", pageCount: 5);
        var control = new PdfViewerControl();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = PdfCoreDocument.Open(pdfPath);
            control.Document = doc;
        });

        await Task.Delay(500);

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.CurrentPage = control.Document!.PageCount;
        });

        // Assert
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.CurrentPage.Should().Be(5);
        });

        // Cleanup
        control.Document?.Dispose();
    }

    #endregion

    #region Interaction Mode Tests

    [AvaloniaFact]
    public void PdfViewerControl_InteractionMode_CanBeChanged()
    {
        // Arrange
        var control = new PdfViewerControl();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            control.InteractionMode = InteractionMode.Redaction;
        });

        // Assert
        control.InteractionMode.Should().Be(InteractionMode.Redaction);
    }

    [AvaloniaFact]
    public void PdfViewerControl_InteractionMode_CanBeSwitched()
    {
        // Arrange
        var control = new PdfViewerControl();
        control.InteractionMode = InteractionMode.Redaction;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            control.InteractionMode = InteractionMode.TextSelection;
        });

        // Assert
        control.InteractionMode.Should().Be(InteractionMode.TextSelection);
    }

    #endregion

    #region Event Tests

    [AvaloniaFact]
    public async Task PdfViewerControl_PageChanged_FiresEvent()
    {
        // Arrange
        var pdfPath = TestPdfGenerator.CreateMultiPagePdf("PdfViewerControlTests_PageEvent", pageCount: 3);
        var control = new PdfViewerControl();
        var eventFired = false;
        int? newPageNumber = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = PdfCoreDocument.Open(pdfPath);
            control.Document = doc;
        });

        await Task.Delay(500);

        control.PageChanged += (s, e) =>
        {
            eventFired = true;
            newPageNumber = e.PageNumber;
        };

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            control.CurrentPage = 2;
        });

        await Task.Delay(300); // Wait for event to fire

        // Assert
        eventFired.Should().BeTrue();
        newPageNumber.Should().Be(2);

        // Cleanup
        control.Document?.Dispose();
    }

    #endregion
}
