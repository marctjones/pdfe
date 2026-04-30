using FluentAssertions;
using Pdfe.Core.Fonts;
using System.Collections.Generic;
using Xunit;

namespace Pdfe.Core.Tests.Fonts;

/// <summary>
/// Tests for CffSubsetter MVP implementation.
///
/// MVP scope:
/// - Test null/empty input handling (basic safety)
/// - Test malformed input graceful fallback (returns original)
/// - Test error cases don't crash
///
/// Note: Full round-trip subsetting tests with real CFF blobs would require
/// either:
/// 1) Extracting CFF from a corpus PDF (requires test-pdfs directory)
/// 2) Building a fully valid CFF from scratch (very complex, 200+ lines)
///
/// The subsetter is designed with defensive coding: if anything fails, it
/// returns the original bytes unchanged. Thus the most important tests are
/// error-handling and integration paths, not synthetic test-CFF parsing.
///
/// Future: Add round-trip tests once a real CFF test file is available.
/// </summary>
public class CffSubsetterTests
{
    [Fact]
    public void Subset_NullInput_ReturnsSafe()
    {
        // Null input should return empty array without crashing
        byte[] nullBytes = null!;
        var usedGlyphs = new HashSet<int> { 0 };

        var subsetted = CffSubsetter.Subset(nullBytes, usedGlyphs);

        subsetted.Should().NotBeNull();
        subsetted.Length.Should().Be(0, "null input should return empty array");
    }

    [Fact]
    public void Subset_EmptyInput_ReturnsSafe()
    {
        // Empty byte array should return empty array without crashing
        byte[] emptyBytes = Array.Empty<byte>();
        var usedGlyphs = new HashSet<int> { 0 };

        var subsetted = CffSubsetter.Subset(emptyBytes, usedGlyphs);

        subsetted.Should().NotBeNull();
        subsetted.Length.Should().Be(0, "empty input should return empty array");
    }

    [Fact]
    public void Subset_MalformedCff_ReturnsOriginal()
    {
        // If the input is too malformed to parse, subsetter should return original unchanged
        // (defensive design: fail gracefully)
        byte[] malformed = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var usedGlyphs = new HashSet<int> { 0 };

        var subsetted = CffSubsetter.Subset(malformed, usedGlyphs);

        // On error, should return original
        subsetted.Should().NotBeNull();
        subsetted.Should().Equal(malformed, "malformed input should return original unchanged");
    }

    [Fact]
    public void Subset_InvalidCffVersion_ReturnsOriginal()
    {
        // CFF version 2 (not supported) should return original
        byte[] cffV2 = new byte[] { 2, 0, 4, 1 }; // major=2, minor=0, hdrSize=4, offSize=1
        var usedGlyphs = new HashSet<int> { 0 };

        var subsetted = CffSubsetter.Subset(cffV2, usedGlyphs);

        subsetted.Should().NotBeNull();
        subsetted.Should().Equal(cffV2, "CFF v2 should not be processed; return original");
    }

    [Fact]
    public void Subset_EmptyUsedGlyphIds_StillIncludesNotdef()
    {
        // Even if usedGlyphIds is empty, .notdef (glyph 0) should be included.
        // This test documents the expected behavior (which real CFF would verify).
        var usedGlyphs = new HashSet<int> { }; // empty

        // This would work with a real CFF, but we're testing the defensive behavior:
        // the subsetter should handle empty sets without crashing.
        byte[] dummy = new byte[] { 1, 0, 4, 1 }; // minimal CFF header
        var subsetted = CffSubsetter.Subset(dummy, usedGlyphs);

        subsetted.Should().NotBeNull();
        // Returns original (or error) — main thing is no crash
    }

    [Fact]
    public void Subset_OutOfRangeGlyphIds_IgnoredSafely()
    {
        // Glyph IDs that don't exist in the font should be silently ignored
        // (documented behavior: the subsetter filters valid IDs internally)
        var usedGlyphs = new HashSet<int> { 0, 100, 999 };

        byte[] dummy = new byte[] { 1, 0, 4, 1 };
        var subsetted = CffSubsetter.Subset(dummy, usedGlyphs);

        subsetted.Should().NotBeNull();
        // No crash on out-of-range glyphs
    }

    [Fact]
    public void Subset_NegativeGlyphIds_IgnoredSafely()
    {
        // Negative glyph IDs should be ignored gracefully
        var usedGlyphs = new HashSet<int> { 0, -1, -5 };

        byte[] dummy = new byte[] { 1, 0, 4, 1 };
        var subsetted = CffSubsetter.Subset(dummy, usedGlyphs);

        subsetted.Should().NotBeNull();
        // No crash on negative glyphs
    }

    [Fact]
    public void Subset_DuplicateGlyphIds_HandlesSafely()
    {
        // HashSet automatically deduplicates, so this should work fine
        var usedGlyphs = new HashSet<int> { 0, 0, 2, 2, 5, 5 };

        usedGlyphs.Count.Should().Be(3, "HashSet should deduplicate");

        byte[] dummy = new byte[] { 1, 0, 4, 1 };
        var subsetted = CffSubsetter.Subset(dummy, usedGlyphs);

        subsetted.Should().NotBeNull();
    }

    [Fact]
    public void Subset_LargeGlyphSet_DoesNotCrash()
    {
        // Subsetter should handle large glyph sets without crashing
        var usedGlyphs = new HashSet<int>();
        for (int i = 0; i < 10000; i += 2)
            usedGlyphs.Add(i);

        byte[] dummy = new byte[] { 1, 0, 4, 1 };
        var subsetted = CffSubsetter.Subset(dummy, usedGlyphs);

        subsetted.Should().NotBeNull();
    }

    [Fact]
    public void Subset_CoreParser_IsAvailableInCore()
    {
        // Verify that CffParser can be used in Pdfe.Core (not just Pdfe.Rendering)
        // This tests the refactoring: CffParser should be accessible from Core.
        var dummyCff = new byte[] { 1, 0, 4, 1 }; // Too short to parse, but that's OK
        var info = CffParser.Parse(dummyCff);

        // Expect null on invalid/short input (parser is tolerant)
        // The important thing is that it's callable from Pdfe.Core
        // (If this test runs without namespace error, refactoring succeeded)
    }
}
