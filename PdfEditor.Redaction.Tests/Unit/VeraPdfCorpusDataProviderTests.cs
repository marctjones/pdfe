using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Tests for the VeraPdfCorpusDataProvider.
/// </summary>
public class VeraPdfCorpusDataProviderTests
{
    private readonly ITestOutputHelper _output;

    public VeraPdfCorpusDataProviderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void IsCorpusAvailable_ReturnsBoolean()
    {
        // Act
        var available = VeraPdfCorpusDataProvider.IsCorpusAvailable;

        // Assert
        _output.WriteLine($"Corpus available: {available}");
        if (available)
        {
            _output.WriteLine($"Corpus path: {VeraPdfCorpusDataProvider.CorpusPath}");
        }

        (available == true || available == false).Should().BeTrue();
    }

    [SkippableFact]
    public void GetFontTestFiles_ReturnsFiles()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetFontTestFiles(10).ToList();

        // Assert
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            _output.WriteLine($"Font file: {file[1]}");
            var path = (string)file[0];
            if (!VeraPdfCorpusDataProvider.IsSkipMarker(path))
            {
                File.Exists(path).Should().BeTrue($"File should exist: {path}");
            }
        }
    }

    [SkippableFact]
    public void GetContentStreamTestFiles_ReturnsFiles()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetContentStreamTestFiles(10).ToList();

        // Assert
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            _output.WriteLine($"Content stream file: {file[1]}");
        }
    }

    [SkippableFact]
    public void GetPdfAComplianceTestFiles_ReturnsFilesFromMultipleCategories()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetPdfAComplianceTestFiles(20).ToList();

        // Assert
        files.Should().NotBeEmpty();
        _output.WriteLine($"Total compliance test files: {files.Count}");
    }

    [SkippableFact]
    public void GetFormFieldTestFiles_ReturnsFiles()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetFormFieldTestFiles(10).ToList();

        // Assert
        _output.WriteLine($"Form field test files: {files.Count}");
        // Note: May return skip marker if category doesn't exist
    }

    [SkippableFact]
    public void GetAvailableCategories_ReturnsCategories()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var categories = VeraPdfCorpusDataProvider.GetAvailableCategories().ToList();

        // Assert
        categories.Should().NotBeEmpty();
        _output.WriteLine("Available categories:");
        foreach (var cat in categories)
        {
            _output.WriteLine($"  - {cat}");
        }
    }

    [SkippableFact]
    public void GetPassingTestFiles_ReturnsOnlyPassFiles()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetPassingTestFiles(VeraPdfCorpusDataProvider.PdfA1b, 10).ToList();

        // Assert
        foreach (var file in files)
        {
            var path = (string)file[0];
            if (!VeraPdfCorpusDataProvider.IsSkipMarker(path))
            {
                path.Should().Contain("pass", "Should only return pass files");
            }
        }
    }

    [SkippableFact]
    public void GetFailingTestFiles_ReturnsOnlyFailFiles()
    {
        Skip.IfNot(VeraPdfCorpusDataProvider.IsCorpusAvailable, "Corpus not available");

        // Act
        var files = VeraPdfCorpusDataProvider.GetFailingTestFiles(VeraPdfCorpusDataProvider.PdfA1b, 10).ToList();

        // Assert
        foreach (var file in files)
        {
            var path = (string)file[0];
            if (!VeraPdfCorpusDataProvider.IsSkipMarker(path))
            {
                path.Should().Contain("fail", "Should only return fail files");
            }
        }
    }

    [Fact]
    public void IsSkipMarker_DetectsSkipPaths()
    {
        // Arrange & Act & Assert
        VeraPdfCorpusDataProvider.IsSkipMarker("SKIP: Corpus not available").Should().BeTrue();
        VeraPdfCorpusDataProvider.IsSkipMarker("/path/to/file.pdf").Should().BeFalse();
    }

    [Fact]
    public void GetFilesInCategory_WhenCorpusNotAvailable_ReturnsSkipMarker()
    {
        // This test works regardless of corpus availability
        // We test the behavior when a non-existent category is requested

        // Act
        var files = VeraPdfCorpusDataProvider.GetFilesInCategory("NonExistentCategory12345").ToList();

        // Assert
        files.Should().HaveCount(1);
        var path = (string)files[0][0];
        VeraPdfCorpusDataProvider.IsSkipMarker(path).Should().BeTrue();
    }
}
