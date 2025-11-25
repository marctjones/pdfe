# PDF Redaction Implementation Plan v2.0

## Overview

This document provides a detailed, test-driven implementation plan for completing the PDF redaction feature. Each phase includes specific tasks, required tests, and commit checkpoints that are triggered by passing test suites.

**Key Principle**: No code is committed until associated tests pass.

---

## Table of Contents

1. [Phase 0: Glyph Position Normalization (Security Critical)](#phase-0-glyph-position-normalization-security-critical)
2. [Phase 1: Search & Redact](#phase-1-search--redact)
3. [Phase 2: Batch Processing](#phase-2-batch-processing)
4. [Phase 3: Bates Numbering](#phase-3-bates-numbering)
5. [Phase 4: Form XObjects Support](#phase-4-form-xobjects-support)
6. [Phase 5: Enterprise Features](#phase-5-enterprise-features)
7. [Test Infrastructure](#test-infrastructure)
8. [Commit Strategy](#commit-strategy)

---

## Phase 0: Glyph Position Normalization (Security Critical)

### Background

Research paper "Story Beyond the Eye: Glyph Positions Break PDF Text Redaction" (PETS 2023) demonstrates that even when text is properly removed, **character positioning metadata can leak information** about redacted content. This affects Adobe Acrobat and all major PDF tools.

### Objective

Implement position normalization to prevent glyph positioning attacks, making this implementation **more secure than commercial alternatives**.

### Tasks

#### Task 0.1: Position Leakage Detection
**File**: `PdfEditor/Services/Redaction/PositionLeakageAnalyzer.cs`

```csharp
public class PositionLeakageAnalyzer
{
    /// <summary>
    /// Analyze a redacted PDF for potential position information leakage
    /// </summary>
    public PositionLeakageReport Analyze(PdfDocument document, List<RedactionArea> redactedAreas);

    /// <summary>
    /// Calculate entropy of character spacing near redaction boundaries
    /// </summary>
    public double CalculateSpacingEntropy(PdfPage page, Rect redactionArea);

    /// <summary>
    /// Detect if relative positioning (Td) reveals redaction width
    /// </summary>
    public bool DetectsRelativePositionLeak(PdfPage page, Rect redactionArea);
}

public class PositionLeakageReport
{
    public double OverallRiskScore { get; set; }  // 0.0 (safe) to 1.0 (high risk)
    public List<LeakageVulnerability> Vulnerabilities { get; set; }
    public List<string> Recommendations { get; set; }
}
```

#### Task 0.2: Position Normalizer
**File**: `PdfEditor/Services/Redaction/PositionNormalizer.cs`

```csharp
public class PositionNormalizer
{
    /// <summary>
    /// Normalize text positioning after redaction to prevent information leakage
    /// </summary>
    public List<PdfOperation> NormalizePositions(
        List<PdfOperation> operations,
        List<PdfOperation> removedOperations,
        Rect redactionArea);

    /// <summary>
    /// Convert relative positioning (Td) to absolute (Tm) near redaction boundaries
    /// </summary>
    public PdfOperation ConvertToAbsolutePosition(TextStateOperation tdOperation, PdfTextState state);

    /// <summary>
    /// Reset character/word spacing to defaults after redaction
    /// </summary>
    public List<PdfOperation> ResetSpacingAfterRedaction(
        List<PdfOperation> operations,
        int redactionEndIndex);
}
```

#### Task 0.3: Enhanced RedactionService Integration
**File**: `PdfEditor/Services/RedactionService.cs`

Add position normalization to the redaction pipeline:
```csharp
public RedactionResult RedactAreaSecure(PdfPage page, Rect area, RedactionOptions options)
{
    // 1. Parse content stream
    var operations = _parser.ParseContentStream(page);

    // 2. Identify operations to remove
    var toRemove = operations.Where(op => op.IntersectsWith(area)).ToList();

    // 3. Filter operations
    var filtered = operations.Where(op => !op.IntersectsWith(area)).ToList();

    // 4. NEW: Normalize positions to prevent leakage
    if (options.NormalizePositions)
    {
        filtered = _positionNormalizer.NormalizePositions(filtered, toRemove, area);
    }

    // 5. Rebuild and replace
    var newContent = _builder.BuildContentStream(filtered);
    ReplacePageContent(page, newContent);

    // 6. Draw visual redaction
    DrawBlackRectangle(page, area);

    // 7. Verify (optional)
    if (options.VerifyRedaction)
    {
        return VerifyRedaction(page, area, toRemove);
    }

    return RedactionResult.Success();
}
```

### Tests for Phase 0

#### Unit Tests
**File**: `PdfEditor.Tests/Unit/PositionNormalizerTests.cs`

```csharp
public class PositionNormalizerTests
{
    [Fact]
    public void ConvertToAbsolutePosition_ShouldReplaceTdWithTm()
    {
        // Arrange
        var normalizer = new PositionNormalizer();
        var tdOperation = CreateTdOperation(50, 0);  // Relative move
        var state = new PdfTextState { /* position context */ };

        // Act
        var result = normalizer.ConvertToAbsolutePosition(tdOperation, state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        ((TextStateOperation)result).Type.Should().Be(TextStateOperationType.SetMatrix);
    }

    [Fact]
    public void NormalizePositions_ShouldResetSpacingNearRedaction()
    {
        // Arrange
        var operations = CreateOperationsWithCustomSpacing();
        var removed = new List<PdfOperation> { operations[5] };  // Middle operation
        var area = removed[0].BoundingBox;

        // Act
        var normalized = _normalizer.NormalizePositions(operations, removed, area);

        // Assert
        // Operations after redaction should have default spacing
        var opsAfterRedaction = normalized.Skip(5).OfType<TextStateOperation>();
        opsAfterRedaction.Should().Contain(op =>
            op.Type == TextStateOperationType.SetCharacterSpacing &&
            op.Value == 0);
    }

    [Fact]
    public void NormalizePositions_ShouldNotAffectDistantOperations()
    {
        // Operations far from redaction should be unchanged
    }
}
```

#### Integration Tests
**File**: `PdfEditor.Tests/Integration/PositionLeakageTests.cs`

```csharp
public class PositionLeakageTests
{
    [Fact]
    public void RedactSSN_ShouldNotLeakSSNWidth()
    {
        // Arrange
        var pdf = TestPdfGenerator.CreatePdfWithText("My SSN is 123-45-6789 and more text");
        var ssnBounds = FindTextBounds(pdf, "123-45-6789");

        // Act
        var service = CreateRedactionService();
        service.RedactAreaSecure(pdf.Pages[0], ssnBounds, new RedactionOptions
        {
            NormalizePositions = true
        });

        // Assert
        var analyzer = new PositionLeakageAnalyzer();
        var report = analyzer.Analyze(pdf, new[] { ssnBounds });

        report.OverallRiskScore.Should().BeLessThan(0.3,
            "Position leakage risk should be low after normalization");
    }

    [Fact]
    public void RedactName_CharacterSpacingShouldNotRevealNameLength()
    {
        // Arrange
        var pdf = TestPdfGenerator.CreatePdfWithText("Contact: John Smith for details");
        var nameBounds = FindTextBounds(pdf, "John Smith");

        // Act
        var service = CreateRedactionService();
        service.RedactAreaSecure(pdf.Pages[0], nameBounds, new RedactionOptions
        {
            NormalizePositions = true
        });

        // Assert
        // Extract character positions after redaction
        var positions = ExtractCharacterPositions(pdf.Pages[0]);

        // Gap between "Contact:" and "for" should be standardized
        var gap = positions["f_in_for"] - positions["colon_after_contact"];
        gap.Should().BeApproximately(StandardGapWidth, tolerance: 5,
            "Gap should not reveal length of redacted name");
    }

    [Fact]
    public void RedactWithoutNormalization_ShouldHaveHigherLeakageRisk()
    {
        // Arrange
        var pdf1 = TestPdfGenerator.CreatePdfWithText("Secret: CONFIDENTIAL-DATA here");
        var pdf2 = TestPdfGenerator.CreatePdfWithText("Secret: CONFIDENTIAL-DATA here");
        var textBounds = FindTextBounds(pdf1, "CONFIDENTIAL-DATA");

        // Act - Without normalization
        var service = CreateRedactionService();
        service.RedactArea(pdf1.Pages[0], textBounds);  // Old method

        // Act - With normalization
        service.RedactAreaSecure(pdf2.Pages[0], textBounds, new RedactionOptions
        {
            NormalizePositions = true
        });

        // Assert
        var analyzer = new PositionLeakageAnalyzer();
        var report1 = analyzer.Analyze(pdf1, new[] { textBounds });
        var report2 = analyzer.Analyze(pdf2, new[] { textBounds });

        report2.OverallRiskScore.Should().BeLessThan(report1.OverallRiskScore,
            "Normalized redaction should have lower leakage risk");
    }

    [Fact]
    public void MultipleRedactions_ShouldNormalizeAllBoundaries()
    {
        // Test that multiple redactions on same line are all normalized
    }

    [Theory]
    [InlineData("Short")]
    [InlineData("Medium Length Text")]
    [InlineData("Very Long Confidential Information String")]
    public void RedactVariableLengthText_GapShouldBeConsistent(string textToRedact)
    {
        // All redactions should result in similar gap widths
        // regardless of original text length
    }
}
```

#### External Tool Validation Tests
**File**: `PdfEditor.Tests/Integration/PositionLeakageExternalValidationTests.cs`

```csharp
public class PositionLeakageExternalValidationTests
{
    [Fact]
    [Trait("Category", "ExternalTools")]
    public void RedactedPdf_ShouldPassMutoolPositionAnalysis()
    {
        // Use mutool to extract glyph positions
        // Verify no anomalous spacing patterns
    }

    [Fact]
    [Trait("Category", "ExternalTools")]
    public void RedactedPdf_ShouldNotRevealWidthToPdftotext()
    {
        // pdftotext with -layout option
        // Verify spacing is normalized
    }
}
```

### Commit Checkpoint 0

**Trigger**: All Phase 0 tests pass
```bash
# Run Phase 0 tests
dotnet test --filter "FullyQualifiedName~PositionLeak" --logger "console;verbosity=detailed"
dotnet test --filter "FullyQualifiedName~PositionNormalizer"

# If all pass, commit
git add PdfEditor/Services/Redaction/PositionNormalizer.cs
git add PdfEditor/Services/Redaction/PositionLeakageAnalyzer.cs
git add PdfEditor/Services/RedactionService.cs
git add PdfEditor.Tests/Unit/PositionNormalizerTests.cs
git add PdfEditor.Tests/Integration/PositionLeakageTests.cs
git commit -m "feat(redaction): Add glyph position normalization to prevent information leakage

- Add PositionNormalizer to convert relative to absolute positioning
- Add PositionLeakageAnalyzer for security verification
- Integrate into RedactionService with NormalizePositions option
- Add comprehensive tests for position leakage prevention
- Addresses PETS 2023 'Story Beyond the Eye' vulnerability

This makes the redaction more secure than Adobe Acrobat."
```

---

## Phase 1: Search & Redact

### Objective

Enable users to find all instances of a pattern (text, SSN, email, phone) and redact them in one operation.

### Tasks

#### Task 1.1: Text Search Service
**File**: `PdfEditor/Services/Redaction/TextSearchService.cs`

```csharp
public class TextSearchService
{
    /// <summary>
    /// Find all occurrences of text in document with their bounding boxes
    /// </summary>
    public List<TextMatch> FindText(PdfDocument document, string searchText, SearchOptions options);

    /// <summary>
    /// Find all matches for a regex pattern
    /// </summary>
    public List<TextMatch> FindPattern(PdfDocument document, string regexPattern, SearchOptions options);

    /// <summary>
    /// Find all instances of a specific PII type
    /// </summary>
    public List<TextMatch> FindPII(PdfDocument document, PIIType piiType);
}

public class TextMatch
{
    public int PageNumber { get; set; }
    public Rect BoundingBox { get; set; }
    public string MatchedText { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double Confidence { get; set; }  // For fuzzy matches
}

public enum PIIType
{
    SSN,              // \d{3}-\d{2}-\d{4}
    Email,            // Standard email pattern
    Phone,            // Various phone formats
    CreditCard,       // Credit card numbers
    DateOfBirth,      // Date patterns
    Address,          // Street addresses
    Custom            // User-defined pattern
}

public class SearchOptions
{
    public bool CaseSensitive { get; set; } = false;
    public bool WholeWord { get; set; } = false;
    public bool UseRegex { get; set; } = false;
    public int MaxResults { get; set; } = 1000;
    public List<int> PageRange { get; set; }  // null = all pages
}
```

#### Task 1.2: PII Pattern Matcher
**File**: `PdfEditor/Services/Redaction/PIIPatternMatcher.cs`

```csharp
public class PIIPatternMatcher
{
    private static readonly Dictionary<PIIType, string[]> Patterns = new()
    {
        [PIIType.SSN] = new[]
        {
            @"\b\d{3}-\d{2}-\d{4}\b",           // 123-45-6789
            @"\b\d{3}\s\d{2}\s\d{4}\b",         // 123 45 6789
            @"\b\d{9}\b"                         // 123456789
        },
        [PIIType.Email] = new[]
        {
            @"\b[\w._%+-]+@[\w.-]+\.[A-Za-z]{2,}\b"
        },
        [PIIType.Phone] = new[]
        {
            @"\b\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",  // (123) 456-7890
            @"\b\d{3}[-.\s]\d{4}\b",                       // 456-7890
            @"\b\+1[-.\s]?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b"  // +1 (123) 456-7890
        },
        [PIIType.CreditCard] = new[]
        {
            @"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b",  // 1234-5678-9012-3456
            @"\b\d{16}\b"                                     // 1234567890123456
        },
        [PIIType.DateOfBirth] = new[]
        {
            @"\b\d{1,2}/\d{1,2}/\d{2,4}\b",    // MM/DD/YYYY
            @"\b\d{4}-\d{2}-\d{2}\b"            // YYYY-MM-DD
        }
    };

    public List<TextMatch> FindPII(string text, PIIType piiType);
    public List<TextMatch> FindAllPII(string text);
    public bool ValidateMatch(TextMatch match, PIIType piiType);  // Luhn check for CC, etc.
}
```

#### Task 1.3: Batch Redact Service
**File**: `PdfEditor/Services/Redaction/BatchRedactService.cs`

```csharp
public class BatchRedactService
{
    /// <summary>
    /// Redact all matches in a document
    /// </summary>
    public BatchRedactionResult RedactMatches(
        PdfDocument document,
        List<TextMatch> matches,
        RedactionOptions options);

    /// <summary>
    /// Search and redact in one operation
    /// </summary>
    public BatchRedactionResult SearchAndRedact(
        PdfDocument document,
        string pattern,
        SearchOptions searchOptions,
        RedactionOptions redactionOptions);

    /// <summary>
    /// Redact all PII of specified types
    /// </summary>
    public BatchRedactionResult RedactAllPII(
        PdfDocument document,
        PIIType[] piiTypes,
        RedactionOptions options);
}

public class BatchRedactionResult
{
    public bool Success { get; set; }
    public int TotalMatches { get; set; }
    public int RedactedCount { get; set; }
    public int FailedCount { get; set; }
    public List<RedactionError> Errors { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<int, int> MatchesPerPage { get; set; }
}
```

#### Task 1.4: ViewModel Integration
**File**: `PdfEditor/ViewModels/MainWindowViewModel.cs` (additions)

```csharp
// New commands
public ReactiveCommand<Unit, Unit> SearchAndRedactCommand { get; }
public ReactiveCommand<PIIType, Unit> RedactPIICommand { get; }

// New properties
[Reactive] public string SearchPattern { get; set; }
[Reactive] public bool UseRegex { get; set; }
[Reactive] public bool CaseSensitive { get; set; }
[Reactive] public ObservableCollection<TextMatch> SearchResults { get; set; }
[Reactive] public bool IsSearching { get; set; }

// Search dialog/panel support
public async Task<List<TextMatch>> PreviewSearchResults(string pattern)
{
    // Show matches without redacting
}

public async Task RedactSelectedMatches(IEnumerable<TextMatch> selectedMatches)
{
    // Redact only user-selected matches
}
```

### Tests for Phase 1

#### Unit Tests
**File**: `PdfEditor.Tests/Unit/TextSearchServiceTests.cs`

```csharp
public class TextSearchServiceTests
{
    [Fact]
    public void FindText_SimpleTerm_ShouldReturnAllOccurrences()
    {
        // Arrange
        var pdf = TestPdfGenerator.CreatePdfWithText(
            "The quick brown fox jumps over the lazy fox");
        var service = new TextSearchService();

        // Act
        var matches = service.FindText(pdf, "fox", new SearchOptions());

        // Assert
        matches.Should().HaveCount(2);
        matches[0].MatchedText.Should().Be("fox");
        matches[1].MatchedText.Should().Be("fox");
    }

    [Fact]
    public void FindText_CaseInsensitive_ShouldMatchAllCases()
    {
        var pdf = TestPdfGenerator.CreatePdfWithText("FOX Fox fox");
        var matches = service.FindText(pdf, "fox", new SearchOptions { CaseSensitive = false });
        matches.Should().HaveCount(3);
    }

    [Fact]
    public void FindText_WholeWord_ShouldNotMatchPartial()
    {
        var pdf = TestPdfGenerator.CreatePdfWithText("foxes outfox the fox");
        var matches = service.FindText(pdf, "fox", new SearchOptions { WholeWord = true });
        matches.Should().HaveCount(1);
    }

    [Fact]
    public void FindText_AcrossPages_ShouldSearchAllPages()
    {
        var pdf = TestPdfGenerator.CreateMultiPagePdf(new[] { "Page 1 fox", "Page 2", "Page 3 fox" });
        var matches = service.FindText(pdf, "fox", new SearchOptions());
        matches.Should().HaveCount(2);
        matches.Select(m => m.PageNumber).Should().Contain(new[] { 1, 3 });
    }

    [Fact]
    public void FindText_ShouldReturnAccurateBoundingBoxes()
    {
        // Verify that returned bounding boxes correctly locate the text
    }
}

public class PIIPatternMatcherTests
{
    [Theory]
    [InlineData("123-45-6789", true)]
    [InlineData("123 45 6789", true)]
    [InlineData("123456789", true)]
    [InlineData("12-345-6789", false)]
    [InlineData("1234-56-789", false)]
    public void FindSSN_ShouldMatchValidFormats(string input, bool shouldMatch)
    {
        var matcher = new PIIPatternMatcher();
        var text = $"SSN: {input}";
        var matches = matcher.FindPII(text, PIIType.SSN);

        if (shouldMatch)
            matches.Should().HaveCount(1);
        else
            matches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("invalid@", false)]
    [InlineData("@example.com", false)]
    public void FindEmail_ShouldMatchValidFormats(string input, bool shouldMatch)
    {
        // Test email pattern matching
    }

    [Theory]
    [InlineData("(123) 456-7890", true)]
    [InlineData("123-456-7890", true)]
    [InlineData("123.456.7890", true)]
    [InlineData("+1 (123) 456-7890", true)]
    [InlineData("12345", false)]
    public void FindPhone_ShouldMatchValidFormats(string input, bool shouldMatch)
    {
        // Test phone pattern matching
    }

    [Fact]
    public void FindCreditCard_ShouldValidateWithLuhn()
    {
        var matcher = new PIIPatternMatcher();

        // Valid Visa test number
        var validCC = "4111-1111-1111-1111";
        var invalidCC = "4111-1111-1111-1112";  // Fails Luhn

        matcher.ValidateMatch(new TextMatch { MatchedText = validCC }, PIIType.CreditCard)
            .Should().BeTrue();
        matcher.ValidateMatch(new TextMatch { MatchedText = invalidCC }, PIIType.CreditCard)
            .Should().BeFalse();
    }

    [Fact]
    public void FindAllPII_ShouldDetectMultipleTypes()
    {
        var text = "Contact John at john@email.com or (555) 123-4567. SSN: 123-45-6789";
        var matcher = new PIIPatternMatcher();

        var matches = matcher.FindAllPII(text);

        matches.Should().HaveCount(3);
        matches.Should().Contain(m => m.MatchedText.Contains("@"));  // Email
        matches.Should().Contain(m => m.MatchedText.Contains("555"));  // Phone
        matches.Should().Contain(m => m.MatchedText.Contains("123-45"));  // SSN
    }
}
```

#### Integration Tests
**File**: `PdfEditor.Tests/Integration/SearchAndRedactTests.cs`

```csharp
public class SearchAndRedactTests
{
    [Fact]
    public void SearchAndRedact_SSN_ShouldRemoveAllInstances()
    {
        // Arrange
        var pdf = TestPdfGenerator.CreatePdfWithText(
            "Employee: John Doe\n" +
            "SSN: 123-45-6789\n" +
            "Emergency Contact SSN: 987-65-4321\n" +
            "Department: Engineering");

        // Act
        var service = new BatchRedactService();
        var result = service.RedactAllPII(pdf, new[] { PIIType.SSN }, new RedactionOptions());

        // Assert
        result.TotalMatches.Should().Be(2);
        result.RedactedCount.Should().Be(2);

        // Verify text extraction
        var extractedText = PdfTestHelpers.ExtractAllText(pdf);
        extractedText.Should().NotContain("123-45-6789");
        extractedText.Should().NotContain("987-65-4321");
        extractedText.Should().Contain("John Doe");  // Non-PII preserved
        extractedText.Should().Contain("Engineering");
    }

    [Fact]
    public void SearchAndRedact_MultiplePages_ShouldRedactAll()
    {
        // Arrange - 10 page document with SSN on pages 1, 5, 10
        var pages = Enumerable.Range(1, 10)
            .Select(i => i switch
            {
                1 => "Page 1: SSN 111-11-1111",
                5 => "Page 5: SSN 555-55-5555",
                10 => "Page 10: SSN 000-00-0000",
                _ => $"Page {i}: No sensitive data"
            }).ToArray();
        var pdf = TestPdfGenerator.CreateMultiPagePdf(pages);

        // Act
        var service = new BatchRedactService();
        var result = service.RedactAllPII(pdf, new[] { PIIType.SSN }, new RedactionOptions());

        // Assert
        result.TotalMatches.Should().Be(3);
        result.MatchesPerPage[1].Should().Be(1);
        result.MatchesPerPage[5].Should().Be(1);
        result.MatchesPerPage[10].Should().Be(1);

        // Verify all removed
        var text = PdfTestHelpers.ExtractAllText(pdf);
        text.Should().NotMatchRegex(@"\d{3}-\d{2}-\d{4}");
    }

    [Fact]
    public void SearchAndRedact_RegexPattern_ShouldMatchCustomPattern()
    {
        // Arrange
        var pdf = TestPdfGenerator.CreatePdfWithText(
            "Case ID: ABC-12345\n" +
            "Related Case: XYZ-99999\n" +
            "Reference: NOT-A-CASE");

        // Act - Custom pattern for case IDs
        var service = new BatchRedactService();
        var result = service.SearchAndRedact(
            pdf,
            @"[A-Z]{3}-\d{5}",
            new SearchOptions { UseRegex = true },
            new RedactionOptions());

        // Assert
        result.TotalMatches.Should().Be(2);
        var text = PdfTestHelpers.ExtractAllText(pdf);
        text.Should().NotContain("ABC-12345");
        text.Should().NotContain("XYZ-99999");
        text.Should().Contain("NOT-A-CASE");  // Doesn't match pattern
    }

    [Fact]
    public void SearchAndRedact_ShouldApplyPositionNormalization()
    {
        // Verify that batch redaction also normalizes positions
        var pdf = TestPdfGenerator.CreatePdfWithText("Email: user@example.com is valid");

        var service = new BatchRedactService();
        service.RedactAllPII(pdf, new[] { PIIType.Email },
            new RedactionOptions { NormalizePositions = true });

        var analyzer = new PositionLeakageAnalyzer();
        var report = analyzer.Analyze(pdf, /* redacted areas */);
        report.OverallRiskScore.Should().BeLessThan(0.3);
    }

    [Fact]
    public void SearchAndRedact_SelectiveRedaction_ShouldOnlyRedactSelected()
    {
        // Test UI workflow where user previews and selects specific matches
        var pdf = TestPdfGenerator.CreatePdfWithText(
            "John's SSN: 111-11-1111\n" +
            "Jane's SSN: 222-22-2222\n" +
            "Bob's SSN: 333-33-3333");

        // User searches and sees 3 results
        var searchService = new TextSearchService();
        var matches = searchService.FindPII(pdf, PIIType.SSN);
        matches.Should().HaveCount(3);

        // User selects only first 2
        var selected = matches.Take(2).ToList();

        // Redact only selected
        var service = new BatchRedactService();
        service.RedactMatches(pdf, selected, new RedactionOptions());

        // Verify
        var text = PdfTestHelpers.ExtractAllText(pdf);
        text.Should().NotContain("111-11-1111");
        text.Should().NotContain("222-22-2222");
        text.Should().Contain("333-33-3333");  // Not selected, not redacted
    }

    [Fact]
    public async Task SearchAndRedact_LargeDocument_ShouldCompleteInReasonableTime()
    {
        // Performance test - 100 page document
        var pages = Enumerable.Range(1, 100)
            .Select(i => $"Page {i}: SSN {i:D3}-{i:D2}-{i:D4} and email user{i}@test.com")
            .ToArray();
        var pdf = TestPdfGenerator.CreateMultiPagePdf(pages);

        var service = new BatchRedactService();
        var sw = Stopwatch.StartNew();

        var result = service.RedactAllPII(pdf,
            new[] { PIIType.SSN, PIIType.Email },
            new RedactionOptions());

        sw.Stop();

        result.TotalMatches.Should().Be(200);  // 100 SSNs + 100 emails
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "100-page batch redaction should complete in under 30 seconds");
    }
}
```

### Commit Checkpoint 1

**Trigger**: All Phase 1 tests pass
```bash
# Run Phase 1 tests
dotnet test --filter "FullyQualifiedName~TextSearch" --logger "console;verbosity=detailed"
dotnet test --filter "FullyQualifiedName~PIIPattern"
dotnet test --filter "FullyQualifiedName~SearchAndRedact"

# If all pass, commit
git add PdfEditor/Services/Redaction/TextSearchService.cs
git add PdfEditor/Services/Redaction/PIIPatternMatcher.cs
git add PdfEditor/Services/Redaction/BatchRedactService.cs
git add PdfEditor/ViewModels/MainWindowViewModel.cs
git add PdfEditor.Tests/Unit/TextSearchServiceTests.cs
git add PdfEditor.Tests/Unit/PIIPatternMatcherTests.cs
git add PdfEditor.Tests/Integration/SearchAndRedactTests.cs
git commit -m "feat(redaction): Add Search & Redact functionality

- Add TextSearchService for text and pattern searching with bounding boxes
- Add PIIPatternMatcher with built-in patterns for SSN, email, phone, credit card
- Add BatchRedactService for redacting multiple matches
- Integrate with ViewModel for UI support
- Include Luhn validation for credit card numbers
- Support selective redaction from search results

Closes #XX - Search and Redact feature"
```

---

## Phase 2: Batch Processing

### Objective

Process multiple PDF files with the same redaction rules.

### Tasks

#### Task 2.1: Batch Processor Service
**File**: `PdfEditor/Services/Redaction/BatchDocumentProcessor.cs`

```csharp
public class BatchDocumentProcessor
{
    /// <summary>
    /// Process multiple files with same redaction rules
    /// </summary>
    public async Task<BatchProcessingResult> ProcessFilesAsync(
        IEnumerable<string> filePaths,
        RedactionRuleSet rules,
        BatchOptions options,
        IProgress<BatchProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process all PDFs in a directory
    /// </summary>
    public async Task<BatchProcessingResult> ProcessDirectoryAsync(
        string directoryPath,
        RedactionRuleSet rules,
        BatchOptions options,
        bool recursive = false,
        IProgress<BatchProgress> progress = null,
        CancellationToken cancellationToken = default);
}

public class RedactionRuleSet
{
    public List<PIIType> PIITypesToRedact { get; set; } = new();
    public List<string> TextPatternsToRedact { get; set; } = new();
    public List<string> RegexPatternsToRedact { get; set; } = new();
    public RedactionOptions Options { get; set; } = new();
}

public class BatchOptions
{
    public int MaxParallelism { get; set; } = 4;
    public string OutputDirectory { get; set; }
    public string OutputFilePattern { get; set; } = "{filename}_redacted{extension}";
    public bool OverwriteExisting { get; set; } = false;
    public bool ContinueOnError { get; set; } = true;
    public bool CreateAuditLog { get; set; } = true;
}

public class BatchProcessingResult
{
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public int TotalRedactions { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<FileProcessingResult> FileResults { get; set; }
    public string AuditLogPath { get; set; }
}

public class BatchProgress
{
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; }
    public string Status { get; set; }
    public double PercentComplete => (double)CurrentFile / TotalFiles * 100;
}
```

#### Task 2.2: Audit Log Generator
**File**: `PdfEditor/Services/Redaction/AuditLogGenerator.cs`

```csharp
public class AuditLogGenerator
{
    public void CreateAuditLog(BatchProcessingResult result, string outputPath);
    public void AppendEntry(string logPath, FileProcessingResult fileResult);

    // Generates CSV/JSON audit log with:
    // - Timestamp
    // - Source file
    // - Output file
    // - Redaction count
    // - PII types found
    // - Processing time
    // - Errors (if any)
}
```

### Tests for Phase 2

#### Integration Tests
**File**: `PdfEditor.Tests/Integration/BatchProcessingTests.cs`

```csharp
public class BatchProcessingTests
{
    [Fact]
    public async Task BatchProcess_MultipleFiles_ShouldRedactAll()
    {
        // Arrange - Create 5 test PDFs
        var tempDir = CreateTempDirectory();
        var files = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            var path = Path.Combine(tempDir, $"doc{i}.pdf");
            TestPdfGenerator.CreatePdfWithText($"Document {i}: SSN 111-{i:D2}-1111", path);
            files.Add(path);
        }

        var outputDir = CreateTempDirectory();

        // Act
        var processor = new BatchDocumentProcessor();
        var result = await processor.ProcessFilesAsync(
            files,
            new RedactionRuleSet { PIITypesToRedact = { PIIType.SSN } },
            new BatchOptions { OutputDirectory = outputDir });

        // Assert
        result.TotalFiles.Should().Be(5);
        result.SuccessfulFiles.Should().Be(5);
        result.FailedFiles.Should().Be(0);

        // Verify each output file
        foreach (var file in files)
        {
            var outputPath = Path.Combine(outputDir,
                Path.GetFileNameWithoutExtension(file) + "_redacted.pdf");
            File.Exists(outputPath).Should().BeTrue();

            var text = PdfTestHelpers.ExtractAllText(outputPath);
            text.Should().NotMatchRegex(@"\d{3}-\d{2}-\d{4}");
        }
    }

    [Fact]
    public async Task BatchProcess_WithProgress_ShouldReportProgress()
    {
        // Test progress reporting
        var progressReports = new List<BatchProgress>();
        var progress = new Progress<BatchProgress>(p => progressReports.Add(p));

        // Process files...

        progressReports.Should().HaveCountGreaterThan(0);
        progressReports.Last().PercentComplete.Should().Be(100);
    }

    [Fact]
    public async Task BatchProcess_WithCancellation_ShouldStopGracefully()
    {
        // Test cancellation
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Process many files...

        // Should throw OperationCanceledException
    }

    [Fact]
    public async Task BatchProcess_ContinueOnError_ShouldProcessRemainingFiles()
    {
        // One corrupted file shouldn't stop batch processing
    }

    [Fact]
    public async Task BatchProcess_ShouldCreateAuditLog()
    {
        // Verify audit log is created with correct entries
    }

    [Fact]
    public async Task BatchProcess_Directory_ShouldProcessAllPdfs()
    {
        // Test directory processing
    }

    [Fact]
    public async Task BatchProcess_DirectoryRecursive_ShouldIncludeSubdirectories()
    {
        // Test recursive directory processing
    }
}
```

### Commit Checkpoint 2

**Trigger**: All Phase 2 tests pass
```bash
dotnet test --filter "FullyQualifiedName~BatchProcessing"

git add PdfEditor/Services/Redaction/BatchDocumentProcessor.cs
git add PdfEditor/Services/Redaction/AuditLogGenerator.cs
git add PdfEditor.Tests/Integration/BatchProcessingTests.cs
git commit -m "feat(redaction): Add batch processing for multiple documents

- Add BatchDocumentProcessor for processing multiple PDFs
- Support parallel processing with configurable concurrency
- Add progress reporting and cancellation support
- Generate audit logs for compliance tracking
- Support directory processing with recursive option"
```

---

## Phase 3: Bates Numbering

### Objective

Add sequential page numbering across documents (legal requirement).

### Tasks

#### Task 3.1: Bates Numbering Service
**File**: `PdfEditor/Services/BatesNumberingService.cs`

```csharp
public class BatesNumberingService
{
    /// <summary>
    /// Apply Bates numbers to a document
    /// </summary>
    public void ApplyBatesNumbers(PdfDocument document, BatesOptions options);

    /// <summary>
    /// Apply Bates numbers across multiple documents
    /// </summary>
    public BatesResult ApplyBatesNumbersToSet(
        IEnumerable<string> filePaths,
        BatesOptions options);
}

public class BatesOptions
{
    public string Prefix { get; set; } = "";           // e.g., "DOE"
    public string Suffix { get; set; } = "";           // e.g., "-CONF"
    public int StartNumber { get; set; } = 1;
    public int NumberOfDigits { get; set; } = 6;       // DOE000001
    public BatesPosition Position { get; set; } = BatesPosition.BottomRight;
    public string FontName { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 10;
    public double MarginX { get; set; } = 36;          // 0.5 inch
    public double MarginY { get; set; } = 36;
}

public enum BatesPosition
{
    TopLeft, TopCenter, TopRight,
    BottomLeft, BottomCenter, BottomRight
}
```

### Tests for Phase 3

**File**: `PdfEditor.Tests/Integration/BatesNumberingTests.cs`

```csharp
public class BatesNumberingTests
{
    [Fact]
    public void ApplyBatesNumbers_SingleDocument_ShouldNumberAllPages()
    {
        var pdf = TestPdfGenerator.CreateMultiPagePdf(5);
        var service = new BatesNumberingService();

        service.ApplyBatesNumbers(pdf, new BatesOptions
        {
            Prefix = "DOE",
            StartNumber = 1,
            NumberOfDigits = 4
        });

        // Verify each page has correct number
        var text1 = PdfTestHelpers.ExtractAllText(pdf.Pages[0]);
        var text5 = PdfTestHelpers.ExtractAllText(pdf.Pages[4]);

        text1.Should().Contain("DOE0001");
        text5.Should().Contain("DOE0005");
    }

    [Fact]
    public void ApplyBatesNumbers_MultipleDocuments_ShouldBeSequential()
    {
        // Doc1 (3 pages): DOE0001-DOE0003
        // Doc2 (2 pages): DOE0004-DOE0005
        // Doc3 (4 pages): DOE0006-DOE0009
    }

    [Fact]
    public void ApplyBatesNumbers_CustomPosition_ShouldPlaceCorrectly()
    {
        // Test different positions
    }

    [Fact]
    public void ApplyBatesNumbers_WithPrefixAndSuffix_ShouldFormatCorrectly()
    {
        // Test: "SMITH-000001-CONF"
    }
}
```

### Commit Checkpoint 3

```bash
dotnet test --filter "FullyQualifiedName~BatesNumbering"

git add PdfEditor/Services/BatesNumberingService.cs
git add PdfEditor.Tests/Integration/BatesNumberingTests.cs
git commit -m "feat: Add Bates numbering for legal document production

- Add BatesNumberingService with configurable prefix/suffix
- Support multiple position options (corners, center)
- Enable sequential numbering across document sets
- Essential feature for legal discovery workflow"
```

---

## Phase 4: Form XObjects Support

### Objective

Handle nested content streams in Form XObjects.

### Tasks

#### Task 4.1: Recursive XObject Parser
**File**: Update `PdfEditor/Services/Redaction/ContentStreamParser.cs`

```csharp
public List<PdfOperation> ParseContentStreamRecursive(PdfPage page)
{
    var operations = new List<PdfOperation>();
    var resources = page.Elements.GetDictionary("/Resources");

    // Parse main content stream
    operations.AddRange(ParseContentStream(page));

    // Parse Form XObjects
    var xobjects = resources?.Elements.GetDictionary("/XObject");
    if (xobjects != null)
    {
        foreach (var xobj in xobjects.Elements)
        {
            if (IsFormXObject(xobj))
            {
                var formOps = ParseFormXObject(xobj, page.Height.Point);
                operations.AddRange(formOps);
            }
        }
    }

    return operations;
}
```

### Tests for Phase 4

**File**: `PdfEditor.Tests/Integration/FormXObjectRedactionTests.cs`

```csharp
public class FormXObjectRedactionTests
{
    [Fact]
    public void RedactFormXObject_ShouldRemoveTextFromNestedStream()
    {
        // Create PDF with Form XObject containing text
        var pdf = TestPdfGenerator.CreatePdfWithFormXObject("CONFIDENTIAL");

        // Redact the area
        var service = CreateRedactionService();
        service.RedactArea(pdf.Pages[0], textArea);

        // Verify text removed from XObject
        var text = PdfTestHelpers.ExtractAllText(pdf);
        text.Should().NotContain("CONFIDENTIAL");
    }

    [Fact]
    public void RedactNestedFormXObject_ShouldHandleMultipleLevels()
    {
        // XObject containing another XObject
    }
}
```

### Commit Checkpoint 4

```bash
dotnet test --filter "FullyQualifiedName~FormXObject"

git add PdfEditor/Services/Redaction/ContentStreamParser.cs
git add PdfEditor.Tests/Integration/FormXObjectRedactionTests.cs
git commit -m "feat(redaction): Add Form XObject (nested content) support

- Implement recursive parsing for Form XObjects
- Handle nested XObjects within XObjects
- Maintain coordinate transformations through nesting levels
- Fixes redaction of text in complex PDF structures"
```

---

## Phase 5: Enterprise Features

### Objective

Add features for enterprise deployment.

### Tasks

- Redaction certificates (digital signatures)
- REST API wrapper
- Compliance validation
- Advanced audit logging

*(Detailed specifications to be added after Phase 0-4 completion)*

---

## Test Infrastructure

### Test Utilities to Add

**File**: `PdfEditor.Tests/Utilities/TestPdfGenerator.cs` (additions)

```csharp
public static class TestPdfGenerator
{
    // Existing methods...

    // New methods for enhanced testing
    public static PdfDocument CreatePdfWithFormXObject(string text);
    public static PdfDocument CreatePdfWithCIDFont(string text);
    public static PdfDocument CreatePdfWithRotation(int degrees, string text);
    public static PdfDocument CreatePdfWithClippingPath(string text);
    public static PdfDocument CreateMultiPagePdf(string[] pageContents);
    public static PdfDocument CreateMultiPagePdf(int pageCount);
    public static void CreatePdfWithText(string text, string outputPath);

    // PII test documents
    public static PdfDocument CreatePdfWithPII(PIITestData data);
}

public class PIITestData
{
    public List<string> SSNs { get; set; } = new();
    public List<string> Emails { get; set; } = new();
    public List<string> Phones { get; set; } = new();
    public List<string> CreditCards { get; set; } = new();
}
```

**File**: `PdfEditor.Tests/Utilities/PdfTestHelpers.cs` (additions)

```csharp
public static class PdfTestHelpers
{
    // Existing methods...

    // New methods
    public static Dictionary<string, double> ExtractCharacterPositions(PdfPage page);
    public static Rect FindTextBounds(PdfDocument doc, string text);
    public static List<Rect> FindAllTextBounds(PdfDocument doc, string text);
    public static bool ContainsFormXObject(PdfDocument doc);
    public static string ExtractContentStream(PdfPage page);
}
```

### Test Categories

```csharp
// Mark tests for selective execution
[Trait("Category", "Unit")]
[Trait("Category", "Integration")]
[Trait("Category", "Security")]
[Trait("Category", "Performance")]
[Trait("Category", "ExternalTools")]

// Run specific categories
// dotnet test --filter "Category=Security"
// dotnet test --filter "Category=Performance"
```

---

## Commit Strategy

### Commit Flow

```
Feature Branch
     │
     ├─► Write Tests (RED)
     │
     ├─► Implement Feature
     │
     ├─► Tests Pass (GREEN)
     │
     ├─► Refactor if needed
     │
     ├─► All related tests pass ──► COMMIT
     │
     └─► Next feature...
```

### Commit Message Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types**: `feat`, `fix`, `test`, `docs`, `refactor`, `perf`
**Scopes**: `redaction`, `search`, `batch`, `bates`, `ui`

### Branch Strategy

```
main
  │
  └─► claude/pdf-redaction-planning-01K5FeteJT2q3JRtmaFzRrUf
        │
        ├─► Phase 0 commits (position normalization)
        │
        ├─► Phase 1 commits (search & redact)
        │
        ├─► Phase 2 commits (batch processing)
        │
        ├─► Phase 3 commits (Bates numbering)
        │
        └─► Phase 4 commits (Form XObjects)
```

---

## Success Metrics

### Phase 0 (Security)
- [ ] Position leakage risk score < 0.3 for all test cases
- [ ] All existing redaction tests still pass
- [ ] External tool validation passes

### Phase 1 (Search & Redact)
- [ ] 100% PII detection accuracy for test patterns
- [ ] < 1 second search time for 100-page document
- [ ] Selective redaction preserves non-selected matches

### Phase 2 (Batch Processing)
- [ ] Process 100 documents in < 5 minutes
- [ ] Zero data loss on cancellation
- [ ] Audit log 100% accurate

### Phase 3 (Bates Numbering)
- [ ] Sequential numbers with zero gaps
- [ ] Correct positioning on all page sizes
- [ ] Multi-document continuity verified

### Phase 4 (Form XObjects)
- [ ] Text extraction from XObjects returns empty after redaction
- [ ] Nested XObjects handled correctly
- [ ] No regression in main content stream handling

---

## Timeline Summary

| Phase | Description | Estimated Effort | Dependencies |
|-------|-------------|------------------|--------------|
| 0 | Position Normalization | 3-5 days | None |
| 1 | Search & Redact | 5-7 days | Phase 0 |
| 2 | Batch Processing | 3-5 days | Phase 1 |
| 3 | Bates Numbering | 2-3 days | None |
| 4 | Form XObjects | 3-5 days | None |
| 5 | Enterprise Features | TBD | Phases 0-4 |

**Total Estimated Effort**: 16-25 days

---

## Appendix: Test Commands Reference

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific phase tests
dotnet test --filter "FullyQualifiedName~PositionLeak"      # Phase 0
dotnet test --filter "FullyQualifiedName~SearchAndRedact"   # Phase 1
dotnet test --filter "FullyQualifiedName~BatchProcessing"   # Phase 2
dotnet test --filter "FullyQualifiedName~BatesNumbering"    # Phase 3
dotnet test --filter "FullyQualifiedName~FormXObject"       # Phase 4

# Run security tests
dotnet test --filter "Category=Security"

# Run performance tests
dotnet test --filter "Category=Performance"

# Run all redaction tests
dotnet test --filter "FullyQualifiedName~Redaction"

# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
```

---

*Document Version: 2.0*
*Last Updated: November 2024*
*Author: Implementation Planning*
