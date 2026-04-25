using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using PdfEditor.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// VM-level search integration. Exercises the exact path the GUI uses
/// (load doc → set SearchText → SearchMatches populated → highlights
/// computed for current page). Pre-fix the user reported that search
/// found nothing in the GUI even though the underlying service finds
/// hundreds of matches; these tests pin the VM bridge.
///
/// Uses [AvaloniaFact] because <see cref="MainWindowViewModel"/>'s
/// search code publishes results via Dispatcher.UIThread.Post — without
/// a running Avalonia dispatcher the post never fires and SearchMatches
/// stays empty.
/// </summary>
[Collection("AvaloniaTests")]
public class SearchViewModelTests
{
    private readonly ITestOutputHelper _out;
    public SearchViewModelTests(ITestOutputHelper o) { _out = o; }

    private const string PragmaticBook =
        "/home/marc/Downloads/business-success-with-open-source_P1.0.pdf";

    [AvaloniaFact]
    public async Task PragmaticBook_VmSearch_PopulatesSearchMatches()
    {
        if (!File.Exists(PragmaticBook)) return;

        var vm = new MainWindowViewModel();
        await vm.LoadDocumentAsync(PragmaticBook);

        // Setting SearchText schedules a debounced search (300 ms wait
        // + service walk + Dispatcher.UIThread.Post to publish results).
        vm.SearchText = "open source";

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && vm.SearchMatches.Count == 0)
            await Task.Delay(200);

        _out.WriteLine($"SearchMatches.Count = {vm.SearchMatches.Count}");
        if (vm.SearchMatches.Count > 0)
            _out.WriteLine(
                $"first match: page {vm.SearchMatches[0].PageIndex + 1}, " +
                $"text='{vm.SearchMatches[0].MatchedText}', " +
                $"box=({vm.SearchMatches[0].X:F1},{vm.SearchMatches[0].Y:F1}," +
                $"{vm.SearchMatches[0].Width:F1}×{vm.SearchMatches[0].Height:F1})");

        vm.SearchMatches.Should().NotBeEmpty(
            "the service finds 481 matches for 'open source' in this book — " +
            "if SearchMatches is empty the VM bridge is broken");
    }

    [AvaloniaFact]
    public async Task PragmaticBook_VmSearch_ComputesPageHighlights()
    {
        if (!File.Exists(PragmaticBook)) return;

        var vm = new MainWindowViewModel();
        await vm.LoadDocumentAsync(PragmaticBook);

        vm.SearchText = "Open Source";
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && vm.SearchMatches.Count == 0)
            await Task.Delay(200);

        // After NavigateToSearchMatch the VM jumps to the page with the
        // first match and UpdateSearchHighlights computes screenRects.
        await Task.Delay(800); // let the dispatcher post settle

        _out.WriteLine(
            $"After search: CurrentPageIndex={vm.CurrentPageIndex}, " +
            $"highlights={vm.CurrentPageSearchHighlights.Count}");

        vm.SearchMatches.Should().NotBeEmpty();
        vm.CurrentPageSearchHighlights.Should().NotBeEmpty(
            "the current page should contain at least one highlight rectangle " +
            "after the VM auto-navigates to the first match");

        // Each highlight rect should be a positive-area box; otherwise the
        // overlay control draws nothing visible.
        foreach (var r in vm.CurrentPageSearchHighlights)
        {
            r.Width.Should().BeGreaterThan(0);
            r.Height.Should().BeGreaterThan(0);
        }
    }

    [AvaloniaFact]
    public async Task PragmaticBook_VmSearch_HighlightCoordsAreInBitmapDips()
    {
        // Highlights must be in the same DIP space the bitmap renders into
        // (120 DPI = page-points × 1.667). Pre-fix this used 150/72 which
        // pushed highlights ~25 % off the page.
        if (!File.Exists(PragmaticBook)) return;

        var vm = new MainWindowViewModel();
        await vm.LoadDocumentAsync(PragmaticBook);

        vm.SearchText = "Brasseur";
        // Wait long enough to cover the 300 ms search debounce + the
        // service walk + dispatcher post; cross-test contention on the
        // shared headless dispatcher can stretch this on a busy run.
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline && vm.SearchMatches.Count == 0)
            await Task.Delay(200);
        await Task.Delay(800);

        var page = vm.PdfCoreDocument!.GetPage(vm.CurrentPageIndex + 1);
        // Bitmap dimensions at 120 DPI:
        const double renderDpi = 120.0;
        var maxX = page.Width * renderDpi / 72.0;
        var maxY = page.Height * renderDpi / 72.0;

        vm.CurrentPageSearchHighlights.Should().NotBeEmpty(
            "Brasseur appears on multiple pages of the book");
        foreach (var r in vm.CurrentPageSearchHighlights)
        {
            r.X.Should().BeInRange(0, maxX,
                $"highlight X must fit inside the {maxX:F0}-DIP bitmap; " +
                $"got X={r.X:F1}, W={r.Width:F1}");
            r.Y.Should().BeInRange(0, maxY,
                $"highlight Y must fit inside the {maxY:F0}-DIP bitmap; " +
                $"got Y={r.Y:F1}, H={r.Height:F1}");
        }
    }
}
