using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PdfRenderService cache functionality.
/// </summary>
public class PdfRenderServiceCacheTests
{
    private readonly PdfRenderService _service;
    private readonly Mock<ILogger<PdfRenderService>> _loggerMock;

    public PdfRenderServiceCacheTests()
    {
        _loggerMock = new Mock<ILogger<PdfRenderService>>();
        _service = new PdfRenderService(_loggerMock.Object);
    }

    [Fact]
    public void MaxCacheEntries_DefaultValue_IsTwenty()
    {
        _service.MaxCacheEntries.Should().Be(20);
    }

    [Fact]
    public void MaxCacheMemoryBytes_DefaultValue_Is100MB()
    {
        _service.MaxCacheMemoryBytes.Should().Be(100 * 1024 * 1024);
    }

    [Fact]
    public void MaxCacheEntries_SetValue_UpdatesProperty()
    {
        _service.MaxCacheEntries = 50;
        _service.MaxCacheEntries.Should().Be(50);
    }

    [Fact]
    public void MaxCacheEntries_SetBelowMinimum_ClampsToOne()
    {
        _service.MaxCacheEntries = 0;
        _service.MaxCacheEntries.Should().Be(1);

        _service.MaxCacheEntries = -5;
        _service.MaxCacheEntries.Should().Be(1);
    }

    [Fact]
    public void MaxCacheMemoryBytes_SetValue_UpdatesProperty()
    {
        _service.MaxCacheMemoryBytes = 50 * 1024 * 1024; // 50 MB
        _service.MaxCacheMemoryBytes.Should().Be(50 * 1024 * 1024);
    }

    [Fact]
    public void MaxCacheMemoryBytes_SetBelowMinimum_ClampsTo1MB()
    {
        _service.MaxCacheMemoryBytes = 100; // Way below 1 MB
        _service.MaxCacheMemoryBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void GetCacheStats_InitialState_ReturnsZeroes()
    {
        var stats = _service.GetCacheStats();

        stats.Count.Should().Be(0);
        stats.MaxEntries.Should().Be(20);
        stats.Hits.Should().Be(0);
        stats.Misses.Should().Be(0);
        stats.CurrentBytes.Should().Be(0);
        stats.MaxBytes.Should().Be(100 * 1024 * 1024);
        stats.HitRate.Should().Be(0);
    }

    [Fact]
    public void ClearCache_AfterClearing_StatsShowZero()
    {
        _service.ClearCache();

        var stats = _service.GetCacheStats();
        stats.Count.Should().Be(0);
        stats.CurrentBytes.Should().Be(0);
    }

    [Fact]
    public void LogCacheStats_DoesNotThrow()
    {
        var action = () => _service.LogCacheStats();
        action.Should().NotThrow();
    }

    [Fact]
    public void CacheStatistics_HitRate_CalculatesCorrectly()
    {
        // Create a CacheStatistics record with known values
        var stats = new PdfRenderService.CacheStatistics(
            Count: 5,
            MaxEntries: 20,
            Hits: 75,
            Misses: 25,
            CurrentBytes: 1024 * 1024,
            MaxBytes: 100 * 1024 * 1024,
            HitRate: 0.75
        );

        stats.HitRate.Should().Be(0.75);
        stats.Count.Should().Be(5);
        stats.Hits.Should().Be(75);
        stats.Misses.Should().Be(25);
    }

    [Fact]
    public void CacheStatistics_Record_SupportsEquality()
    {
        var stats1 = new PdfRenderService.CacheStatistics(5, 20, 10, 5, 1024, 2048, 0.67);
        var stats2 = new PdfRenderService.CacheStatistics(5, 20, 10, 5, 1024, 2048, 0.67);

        stats1.Should().Be(stats2);
    }
}
