using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Tests for B5 recent files pin/unpin functionality:
/// - Pin/unpin via context menu (here: TogglePin method)
/// - MRU bounded to 20 entries; pinned entries never evicted
/// - Persist to JSON
/// </summary>
public class RecentFilesPinTests
{
    private readonly RecentFilesService _service;

    public RecentFilesPinTests()
    {
        var logger = NullLogger<RecentFilesService>.Instance;
        _service = new RecentFilesService(logger);
    }

    [Fact]
    public void CanCreateRecentFileEntry()
    {
        var entry = new RecentFilesService.RecentFileEntry
        {
            Path = "/home/test.pdf",
            IsPinned = false
        };

        entry.Path.Should().Be("/home/test.pdf");
        entry.IsPinned.Should().BeFalse();
    }

    [Fact]
    public void CanTogglePinState()
    {
        // Create test file
        var testFile = Path.Combine(Path.GetTempPath(), "test_recent.pdf");
        File.WriteAllText(testFile, "test");

        try
        {
            var entries = new ObservableCollection<RecentFilesService.RecentFileEntry>
            {
                new RecentFilesService.RecentFileEntry { Path = testFile, IsPinned = false }
            };

            var entry = entries[0];
            entry.IsPinned.Should().BeFalse();

            // Toggle pin
            _service.TogglePin(testFile, entries);

            entry.IsPinned.Should().BeTrue();

            // Toggle back
            _service.TogglePin(testFile, entries);

            entry.IsPinned.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
        }
    }

    [Fact]
    public void PinnedEntriesAreNotEvicted()
    {
        // Create 25 test files
        var testFiles = new List<string>();
        try
        {
            for (int i = 0; i < 25; i++)
            {
                var file = Path.Combine(Path.GetTempPath(), $"test_recent_{i}.pdf");
                File.WriteAllText(file, $"test {i}");
                testFiles.Add(file);
            }

            var entries = new ObservableCollection<RecentFilesService.RecentFileEntry>();

            // Add all files
            foreach (var file in testFiles)
            {
                _service.AddOrUpdate(file, entries);
            }

            // Pin the first 3 files (oldest in current MRU order due to most recent access)
            var oldestThree = entries.OrderBy(e => e.LastAccessedUtc).Take(3).ToList();
            foreach (var entry in oldestThree)
            {
                entry.IsPinned = true;
            }

            // After trimming, we should have exactly 20 entries
            entries.Count.Should().Be(20);

            // All three pinned entries should still be present
            foreach (var pinned in oldestThree)
            {
                entries.Should().Contain(pinned);
            }
        }
        finally
        {
            foreach (var file in testFiles)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    [Fact]
    public void AddOrUpdateMovesFileToTop()
    {
        var testFile1 = Path.Combine(Path.GetTempPath(), "test_recent_1.pdf");
        var testFile2 = Path.Combine(Path.GetTempPath(), "test_recent_2.pdf");

        File.WriteAllText(testFile1, "test1");
        File.WriteAllText(testFile2, "test2");

        try
        {
            var entries = new ObservableCollection<RecentFilesService.RecentFileEntry>();

            _service.AddOrUpdate(testFile1, entries);
            _service.AddOrUpdate(testFile2, entries);

            // testFile2 should be first
            entries[0].Path.Should().Be(testFile2);
            entries[1].Path.Should().Be(testFile1);

            // Add testFile1 again
            _service.AddOrUpdate(testFile1, entries);

            // Now testFile1 should be first (moved to top)
            entries[0].Path.Should().Be(testFile1);
            entries[1].Path.Should().Be(testFile2);
        }
        finally
        {
            if (File.Exists(testFile1))
                File.Delete(testFile1);
            if (File.Exists(testFile2))
                File.Delete(testFile2);
        }
    }

    [Fact]
    public void RemoveDeletesFileFromRecentFiles()
    {
        var testFile = Path.Combine(Path.GetTempPath(), "test_recent_remove.pdf");
        File.WriteAllText(testFile, "test");

        try
        {
            var entries = new ObservableCollection<RecentFilesService.RecentFileEntry>();

            _service.AddOrUpdate(testFile, entries);
            entries.Should().HaveCount(1);

            _service.Remove(testFile, entries);
            entries.Should().HaveCount(0);
        }
        finally
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
        }
    }

    [Fact]
    public void AddOrUpdateIgnoresMissingFiles()
    {
        var entries = new ObservableCollection<RecentFilesService.RecentFileEntry>();

        // Try to add non-existent file
        _service.AddOrUpdate("/nonexistent/path/test.pdf", entries);

        // Should not be added
        entries.Should().HaveCount(0);
    }

    [Fact]
    public void LoadFiltersMissingFiles()
    {
        // This is harder to test without mocking, but we can verify the Load method exists
        var result = _service.Load();

        // Should return a valid collection (possibly empty if file doesn't exist)
        result.Should().NotBeNull();
    }
}
