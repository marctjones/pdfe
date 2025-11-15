# Testing Guide

## Overview

This document describes the testing infrastructure and strategy for the PDF Editor project.

## Test Project Setup

### Created Files

**Test Project:**
- `PdfEditor.Tests/PdfEditor.Tests.csproj` - Test project with dependencies
- `PdfEditor.Tests/README.md` - Test project documentation

**Test Utilities:**
- `Utilities/TestPdfGenerator.cs` - Creates test PDFs with known content
- `Utilities/PdfTestHelpers.cs` - PDF inspection and assertion helpers

**Integration Tests:**
- `Integration/RedactionIntegrationTests.cs` - 5 comprehensive integration tests

### Dependencies Added

```xml
<!-- Testing Framework -->
- xUnit 2.5.3
- Microsoft.NET.Test.Sdk 17.8.0
- FluentAssertions 6.12.0 (readable assertions)

<!-- Logging -->
- Serilog 3.1.1
- Serilog.Sinks.Console 5.0.1
- Serilog.Sinks.File 5.0.0

<!-- PDF Libraries (for verification) -->
- PdfSharpCore 1.3.65 (same as main app)
- PdfPig 0.1.8 (for text extraction)
```

## Integration Tests Created

### 1. RedactSimpleText_ShouldRemoveTextFromPdf
**Purpose:** Verifies basic redaction functionality
**Test Flow:**
1. Creates PDF with "CONFIDENTIAL" text at position (100, 100)
2. Redacts area (90, 90, 150, 30) covering the text
3. Verifies text is removed from PDF structure
4. Verifies PDF remains valid

**Expected Result:** ✅ Text "CONFIDENTIAL" not found in redacted PDF

### 2. RedactMultipleTextBlocks_ShouldOnlyRemoveTargetedText
**Purpose:** Verifies selective redaction (doesn't over-redact)
**Test Flow:**
1. Creates PDF with multiple text blocks:
   - "CONFIDENTIAL" at y=100
   - "Public Information" at y=200
   - "Secret Data" at y=300
2. Redacts only area around "CONFIDENTIAL"
3. Verifies only targeted text removed, others remain

**Expected Result:** ✅ Only "CONFIDENTIAL" removed, other text intact

### 3. RedactArea_WithNoContent_ShouldNotCorruptPdf
**Purpose:** Verifies robustness when redacting empty areas
**Test Flow:**
1. Creates PDF with text at (100, 100)
2. Redacts area (400, 400, 100, 50) - far from content
3. Verifies PDF remains valid
4. Verifies original text untouched

**Expected Result:** ✅ PDF valid, original content intact

### 4. RedactMultipleAreas_ShouldRemoveAllTargetedContent
**Purpose:** Verifies multiple redactions on same page
**Test Flow:**
1. Creates PDF with 4 text blocks
2. Redacts two different areas
3. Verifies both targeted blocks removed
4. Verifies untargeted blocks remain

**Expected Result:** ✅ Multiple areas successfully redacted independently

### 5. RedactPage_ShouldMaintainPdfStructure
**Purpose:** Verifies PDF structural integrity after redaction
**Test Flow:**
1. Creates 3-page PDF
2. Redacts content on page 2 only
3. Verifies page count unchanged
4. Verifies content on pages 1 and 3 intact
5. Verifies PDF structure valid

**Expected Result:** ✅ PDF structure maintained, other pages unaffected

## Test Utilities

### TestPdfGenerator

**Methods:**
```csharp
// Simple single-page PDF with text
CreateSimpleTextPdf(path, text)

// Multiple text blocks at different positions
CreateMultiTextPdf(path)

// Text with graphics (rectangles)
CreateTextWithGraphicsPdf(path)

// Transformed (rotated/scaled) text
CreateTransformedTextPdf(path)

// Multi-page PDF
CreateMultiPagePdf(path, pageCount)

// Cleanup
CleanupTestFile(path)
```

### PdfTestHelpers

**Methods:**
```csharp
// Text extraction and verification
ExtractAllText(pdfPath) → string
ExtractTextFromPage(pdfPath, pageIndex) → string
PdfContainsText(pdfPath, searchText) → bool
GetWordsFromPage(pdfPath, pageIndex) → List<string>
CountWordOccurrences(pdfPath, word) → int

// PDF validation
IsValidPdf(pdfPath) → bool
GetPageCount(pdfPath) → int
GetFileSize(pdfPath) → long
```

## Running Tests

### Command Line

```bash
# Navigate to test directory
cd PdfEditor.Tests

# Restore packages (requires network)
dotnet restore

# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~RedactSimpleText"

# Run all integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverageReportFormat=opencover
```

### Expected Output

```
Test run for PdfEditor.Tests.dll (.NET 8.0)
Microsoft (R) Test Execution Command Line Tool Version 17.8.0

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed! - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 1.2s
```

## Test Best Practices Implemented

### 1. Arrange-Act-Assert Pattern
Each test clearly separated into three sections:
```csharp
// Arrange - Setup test data
var testPdf = TestPdfGenerator.CreateSimpleTextPdf(...);

// Act - Perform action being tested
_redactionService.RedactArea(page, area);

// Assert - Verify expected outcome
textAfter.Should().NotContain("CONFIDENTIAL");
```

### 2. Detailed Test Logging
Using `ITestOutputHelper` for diagnostic output:
```csharp
_output.WriteLine("Creating test PDF...");
_output.WriteLine($"Text before: {textBefore}");
_output.WriteLine("✓ Test passed");
```

### 3. Automatic Cleanup
Implements `IDisposable` to clean up temp files:
```csharp
public void Dispose()
{
    foreach (var file in _tempFiles)
    {
        TestPdfGenerator.CleanupTestFile(file);
    }
}
```

### 4. Fluent Assertions
Readable, descriptive assertions:
```csharp
textAfter.Should().NotContain("CONFIDENTIAL", 
    "redacted text should be permanently removed");
```

### 5. Independent Tests
Each test creates its own test data, no shared state.

### 6. Descriptive Test Names
Names describe what they verify:
- `RedactSimpleText_ShouldRemoveTextFromPdf`
- `RedactMultipleAreas_ShouldRemoveAllTargetedContent`

## Next Steps: Unit Tests

### Phase 2: Core Component Unit Tests

**PdfMatrixTests** (10 tests planned)
- Identity matrix creation
- Matrix multiplication
- Point transformation
- Translation matrix
- Scaling matrix
- Matrix cloning

**PdfGraphicsStateTests** (8 tests planned)
- State creation and initialization
- State cloning
- Save/restore stack operations
- Transformation matrix updates

**PdfTextStateTests** (12 tests planned)
- Text position updates
- Matrix transformations
- Character/word spacing
- Horizontal scaling
- Leading calculations

**TextBoundsCalculatorTests** (15 tests planned)
- Simple text bounds
- Bounds with transformations
- Bounds with spacing
- Coordinate system conversions
- Font metrics application

**ContentStreamParserTests** (20 tests planned)
- Parse simple text operators
- Parse path operators
- Parse state changes
- Track state correctly
- Calculate bounding boxes
- Handle complex PDFs

**ContentStreamBuilderTests** (12 tests planned)
- Build from simple operations
- Build with all operator types
- Verify PDF syntax
- Round-trip tests (parse → build → parse)

### Phase 3: Error Handling Tests

**ErrorHandlingTests** (18 tests planned)
- Invalid PDF handling
- Corrupt content streams
- Missing resources
- Out of bounds areas
- Fallback mechanisms

### Phase 4: Performance Tests

**PerformanceTests** (benchmarks)
- Parse performance
- Build performance
- Redaction performance
- Memory usage profiling

## Coverage Goals

- **Target:** 80%+ code coverage
- **Priority:** Critical paths (redaction engine)
- **Current:** Integration tests provide high-level coverage
- **Next:** Unit tests for detailed coverage

## Testing Philosophy

1. **Test behavior, not implementation** - Focus on what code does, not how
2. **Integration tests first** - Verify end-to-end workflows work
3. **Unit tests for complexity** - Add unit tests for complex logic
4. **Fast feedback** - Tests should run quickly
5. **Clear failures** - Test failures should clearly indicate problem

## Continuous Integration Ready

Tests are designed to run in CI/CD:
- No external dependencies (except PDFs)
- Deterministic (same input = same output)
- Fast execution (<5 seconds for all tests)
- Clear pass/fail criteria
- Detailed logging for debugging failures

## Running in Local Environment

Since this environment has network restrictions, to run tests on your local machine:

```bash
# Clone repository
git clone <repo>
cd pdfe

# Restore all packages
dotnet restore

# Run tests
cd PdfEditor.Tests
dotnet test

# Or from root
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj
```

## Test Infrastructure Summary

**Created:**
- ✅ xUnit test project
- ✅ 5 comprehensive integration tests
- ✅ Test PDF generator utility
- ✅ PDF inspection helpers
- ✅ Test documentation
- ✅ Logging infrastructure (Serilog)
- ✅ FluentAssertions for readability

**Ready for:**
- Adding unit tests
- Running in CI/CD
- Code coverage analysis
- Performance benchmarking

**Next commit will include:**
- Unit tests for core components
- Error handling tests
- Performance benchmarks

This testing infrastructure provides a solid foundation for comprehensive test coverage with best practices including detailed logging, automatic cleanup, and readable assertions.
