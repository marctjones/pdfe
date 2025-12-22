# PdfEditor.Redaction Library - Implementation Plan

## Executive Summary

This document outlines the implementation plan for `PdfEditor.Redaction`, an independent library for TRUE glyph-level PDF redaction. The library will be developed using test-driven development (TDD), with each text storage mechanism tested and validated independently before moving to the next.

**Primary Goal**: Remove text at the glyph level from PDF content streams, ensuring redacted text cannot be extracted by any PDF text extraction tool.

**Key Principle**: Use PdfPig as the single source of truth for letter positions (since text selection already works reliably), then surgically modify content streams to remove specific glyphs.

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [PDF Text Storage Mechanisms](#pdf-text-storage-mechanisms)
3. [Architecture Overview](#architecture-overview)
4. [Directory Structure](#directory-structure)
5. [Core Interfaces and Classes](#core-interfaces-and-classes)
6. [Test-Driven Implementation Phases](#test-driven-implementation-phases)
7. [Test Fixtures and Resources](#test-fixtures-and-resources)
8. [Library Source Code Analysis](#library-source-code-analysis)
9. [Quality Requirements](#quality-requirements)
10. [Risk Mitigation](#risk-mitigation)

---

## Problem Statement

### Current Issues

The existing redaction implementation has several problems:

1. **Inaccurate Bounding Boxes**: `TextBoundsCalculator` produces inaccurate bounding boxes (documented in code comments as "can be 1000s of points wide")

2. **Scrambled Content Streams**: PDFs can encode letters in any order in the content stream - visual layout is determined by transformation matrices, not stream order

3. **Matching Failures**: `CharacterMatcher` attempts to match PdfPig letters to parsed operations using spatial proximity, which fails for complex PDFs

4. **Coordinate System Complexity**: Multiple coordinate conversions between PDF (bottom-left origin), Avalonia (top-left origin), and image pixels

### What Works

- **Text selection works reliably** using PdfPig's `Letter` objects
- PdfPig handles font decoding, transformation matrices, and provides accurate `GlyphRectangle` coordinates
- The user can select text precisely; this proves PdfPig's letter positions are accurate

### Solution Approach

Use PdfPig letter positions as the **primary source of truth**, then work backwards to:
1. Identify which content stream operators produced those letters
2. Surgically modify those operators to remove specific glyphs
3. Rebuild the content stream with modifications

---

## PDF Text Storage Mechanisms - Complete Taxonomy

This section provides a comprehensive enumeration of ALL ways text can be stored in PDF documents.
Each mechanism requires specific handling for redaction. This taxonomy is based on the
[PDF 32000-1:2008 specification](https://opensource.adobe.com/dc-acrobat-sdk-docs/) and
references from [Syncfusion PDF Operators](https://www.syncfusion.com/succinctly-free-ebooks/pdf/text-operators),
[PDF Association Cheat Sheet](https://pdfa.org/wp-content/uploads/2023/08/PDF-Operators-CheatSheet.pdf),
and [PdfPig font notes](https://github.com/UglyToad/PdfPig/blob/master/font-notes.md).

### Category 1: Text Showing Operators (Content Stream)

These operators actually render text glyphs. **All text redaction ultimately involves modifying these operators.**

| Operator | Name | Syntax | Description | Complexity |
|----------|------|--------|-------------|------------|
| `Tj` | Show String | `(string) Tj` | Show a single text string at current position | Low |
| `TJ` | Show Array | `[(str) num (str) ...] TJ` | Show strings with positioning adjustments (kerning) | Medium |
| `'` | Quote | `(string) '` | Move to next line and show string (equivalent to `T* (string) Tj`) | Low |
| `"` | Double Quote | `aw ac (string) "` | Set word/char spacing, move to next line, show string | Low |

**Implementation Priority**: Tj → TJ → ' → "

### Category 2: Text Positioning Operators (Content Stream)

These operators affect WHERE text appears but don't contain text themselves. Understanding them is critical for accurate position calculation.

| Operator | Name | Syntax | Description | State Affected |
|----------|------|--------|-------------|----------------|
| `BT` | Begin Text | `BT` | Begin a text object; resets text matrix | Text matrix → identity |
| `ET` | End Text | `ET` | End text object | Exits text mode |
| `Tm` | Text Matrix | `a b c d e f Tm` | Set text matrix and line matrix absolutely | Absolute position/scale |
| `Td` | Move Text | `tx ty Td` | Move to relative position | Relative offset |
| `TD` | Move Text + Leading | `tx ty TD` | Move and set leading = -ty | Relative + leading |
| `T*` | Next Line | `T*` | Move to start of next line using leading | Uses TL value |

**Critical for Redaction**: Text matrix state must be tracked to match PdfPig letter positions.

### Category 3: Text State Operators (Content Stream)

These affect text rendering properties. Must be tracked for accurate glyph metrics.

| Operator | Name | Syntax | Description | Initial Value |
|----------|------|--------|-------------|---------------|
| `Tf` | Set Font | `/name size Tf` | Set font and size | None (required) |
| `Tc` | Char Spacing | `spacing Tc` | Space between characters | 0 |
| `Tw` | Word Spacing | `spacing Tw` | Additional space for space char (0x20) | 0 |
| `Tz` | Horizontal Scale | `scale Tz` | Horizontal scaling percentage | 100 |
| `TL` | Leading | `leading TL` | Line spacing for T*, ', " | 0 |
| `Tr` | Render Mode | `mode Tr` | Fill, stroke, clip, invisible (0-7) | 0 |
| `Ts` | Text Rise | `rise Ts` | Superscript/subscript offset | 0 |

**Render Modes** (Tr operator):
- 0: Fill text (normal)
- 1: Stroke text (outline)
- 2: Fill then stroke
- 3: Invisible (but still selectable!)
- 4-7: Same as 0-3 but add to clipping path

**Important**: Mode 3 (invisible) text is STILL extractable and must be redacted!

### Category 4: String Encoding Formats (Within Operators)

How the actual bytes in text operators map to characters.

#### 4.1 Literal Strings
```
(Hello World)           # Simple ASCII
(Hello \(World\))       # Escaped parentheses
(Line1\nLine2)          # Newline escape
(Tab\there)             # Tab escape
(\101\102\103)          # Octal escapes (ABC)
(\\backslash)           # Escaped backslash
```

**Escape Sequences in Literal Strings**:
| Sequence | Meaning |
|----------|---------|
| `\n` | Newline (0x0A) |
| `\r` | Carriage return (0x0D) |
| `\t` | Tab (0x09) |
| `\b` | Backspace (0x08) |
| `\f` | Form feed (0x0C) |
| `\(` | Left parenthesis |
| `\)` | Right parenthesis |
| `\\` | Backslash |
| `\ddd` | Octal character code (1-3 digits) |

#### 4.2 Hexadecimal Strings
```
<48656C6C6F>            # "Hello" in hex
<48 65 6C 6C 6F>        # Same, with whitespace (ignored)
<4865 6C6C 6F>          # Same, grouped differently
```

**Note**: If odd number of hex digits, final digit is assumed to be 0.

#### 4.3 Multi-byte Strings (CID Fonts)
```
<0048004500490049>      # CID font with 2-byte codes
```

### Category 5: Font Types

Different font types require different encoding handling.

#### 5.1 Simple Fonts (Single-byte)

| Type | Description | Encoding | Complexity |
|------|-------------|----------|------------|
| Type 1 | PostScript Type 1 outlines | 256-entry encoding table | Low |
| TrueType | TrueType outlines | 256-entry encoding table | Low |
| Type 3 | Glyphs defined as content streams | 256-entry encoding table | High |
| MM Type 1 | Multiple Master Type 1 | 256-entry encoding table | Medium |

**Simple Font Encoding Sources** (in priority order):
1. `/Encoding` dictionary with `/Differences` array
2. `/Encoding` name (StandardEncoding, MacRomanEncoding, WinAnsiEncoding, MacExpertEncoding)
3. Font's built-in encoding

#### 5.2 Composite Fonts (CID, Multi-byte)

| Type | Description | Encoding | Complexity |
|------|-------------|----------|------------|
| Type 0 | Composite font referencing CIDFont | CMap | High |
| CIDFontType0 | CID font with Type 1 outlines | CID values | High |
| CIDFontType2 | CID font with TrueType outlines | CID + CIDToGIDMap | High |

**CID Font Components**:
- **CMap**: Maps character codes → CID values
- **CIDFont**: Maps CID values → glyph descriptions
- **CIDToGIDMap**: (TrueType only) Maps CID → Glyph Index

#### 5.3 Predefined CMaps for CID Fonts

| CMap | Description |
|------|-------------|
| Identity-H | Horizontal, CID = character code |
| Identity-V | Vertical, CID = character code |
| UniJIS-UTF16-H | Japanese, UTF-16 to CID |
| UniGB-UTF16-H | Simplified Chinese |
| UniKS-UTF16-H | Korean |
| UniCNS-UTF16-H | Traditional Chinese |

### Category 6: Font Encoding Mechanisms

#### 6.1 Standard Encodings

| Encoding | Description | Character Set |
|----------|-------------|---------------|
| StandardEncoding | Adobe standard | Latin subset |
| MacRomanEncoding | Mac OS Roman | Extended Latin |
| WinAnsiEncoding | Windows CP1252 | Extended Latin |
| MacExpertEncoding | Expert character set | Small caps, fractions |
| PDFDocEncoding | PDF metadata encoding | Full Latin-1 + extras |

#### 6.2 Custom Encoding with Differences
```
/Encoding <<
  /Type /Encoding
  /BaseEncoding /WinAnsiEncoding
  /Differences [
    128 /Euro /bullet
    144 /trademark
  ]
>>
```

#### 6.3 ToUnicode CMap

Provides Unicode mapping for text extraction (NOT for glyph rendering).

```
/ToUnicode stream containing:
  beginbfchar
  <01> <0041>        % Glyph 01 → Unicode U+0041 (A)
  <02> <0042>        % Glyph 02 → Unicode U+0042 (B)
  endbfchar
  beginbfrange
  <03> <05> <0043>   % Glyphs 03-05 → Unicode U+0043-U+0045 (C-E)
  endbfrange
```

**Critical**: ToUnicode is what PdfPig uses for text extraction. We need the reverse mapping!

### Category 7: Structural Locations for Text

Text can appear in multiple structural locations within a PDF.

#### 7.1 Page Content Stream(s)

**Primary location.** Pages can have:
- Single content stream: `/Contents stream`
- Array of content streams: `/Contents [stream1 stream2 ...]`

All streams are concatenated for rendering.

#### 7.2 Form XObjects

Reusable content referenced via `/XObject` resources.

```
% In page content:
/Form1 Do          % Invoke Form XObject

% Form XObject has its own content stream with text operators
```

**Challenges**:
- Own coordinate system (transformation matrix)
- Can be nested (Form XObject invoking another Form XObject)
- Same XObject can be used multiple times on a page
- Must track which XObject contains which text

#### 7.3 Type 3 Font Glyph Streams

Each glyph in a Type 3 font is a mini content stream.

```
/CharProcs <<
  /A stream        % Content stream defining glyph 'A'
  /B stream        % Content stream defining glyph 'B'
>>
```

**Redaction approach**: Must modify the glyph definition itself, or replace with empty glyph.

#### 7.4 Pattern Streams

Text in tiling patterns.

```
/Pattern <<
  /P1 << /PatternType 1 ... stream >>
>>
```

#### 7.5 Annotation Appearance Streams

Text in annotation appearances (comments, form fields, stamps).

```
/AP <<
  /N stream        % Normal appearance
  /R stream        # Rollover appearance
  /D stream        % Down appearance
>>
```

**Note**: Form field values have both appearance stream AND `/V` value dictionary entry.

#### 7.6 Optional Content Groups (Layers)

Text can be conditionally visible based on layer visibility.

```
/OC /LayerName BDC    % Begin marked content
  (Text on layer) Tj
EMC                    % End marked content
```

### Category 8: Marked Content and Structure

Additional text-related data in structured PDFs.

#### 8.1 Marked Content Operators

| Operator | Syntax | Description |
|----------|--------|-------------|
| `BMC` | `/tag BMC` | Begin marked content sequence |
| `BDC` | `/tag propDict BDC` | Begin marked content with properties |
| `EMC` | `EMC` | End marked content sequence |

#### 8.2 ActualText Property

Replacement text for marked content (used when glyphs don't map to Unicode).

```
/Span << /ActualText (Actual Unicode Text) >> BDC
  <0001 0002 0003> Tj    % Glyphs that don't have Unicode mapping
EMC
```

**Critical**: ActualText can contain text that isn't in the glyph operators themselves!

#### 8.3 Alt Text Property

Alternative text for accessibility (different from ActualText).

```
/Figure << /Alt (Description of figure) >> BDC
  ... content ...
EMC
```

### Category 9: Metadata Text (Non-Content Stream)

Text stored in PDF structure, not content streams.

| Location | Description | Redaction Required? |
|----------|-------------|---------------------|
| `/Info` dictionary | Title, Author, Subject, Keywords | Yes (if contains sensitive data) |
| XMP metadata stream | Embedded XML metadata | Yes |
| Document outline | Bookmark titles | Possibly |
| Named destinations | Named link targets | Possibly |
| File attachments | Embedded files | Separate concern |

### Category 10: Edge Cases and Special Scenarios

#### 10.1 Invisible Text (Tr mode 3)

Text rendered with mode 3 is invisible but extractable. Common in:
- Scanned documents with OCR layer
- Copy-protection attempts

**Must redact even if not visible!**

#### 10.2 Text Outside Page Boundaries

Text can be positioned outside the visible page area (negative coordinates or beyond MediaBox).

#### 10.3 Clipped Text

Text can be clipped by:
- Clipping paths (`W`, `W*` operators)
- Render mode 4-7 (clip modes)

**Visible portion may differ from extractable text!**

#### 10.4 Ligatures and Complex Scripts

Single glyph representing multiple Unicode characters:
- `fi` ligature → U+FB01 or U+0066 U+0069
- Arabic connected forms
- Indic conjuncts

ToUnicode CMap should handle this, but may map to single codepoint.

#### 10.5 Right-to-Left and Vertical Text

- Hebrew, Arabic: RTL reading order
- CJK: May use vertical writing mode (Identity-V CMap)

PdfPig reports characters in reading order, not stream order.

---

### Summary: Implementation Priority Matrix

| Category | Priority | Phase | Notes |
|----------|----------|-------|-------|
| Tj operator | P0 - Critical | 1 | Most common, simplest |
| TJ operator | P0 - Critical | 2 | Very common for professional PDFs |
| ' and " operators | P1 - High | 3 | Less common but straightforward |
| WinAnsi/MacRoman encoding | P0 - Critical | 1 | Most Western documents |
| Custom /Encoding + Differences | P1 - High | 4 | Many professional PDFs |
| ToUnicode CMap | P1 - High | 4 | Required for CID fonts |
| Page content streams | P0 - Critical | 1 | Primary target |
| Form XObjects | P1 - High | 5 | Common in forms, templates |
| CID fonts (CJK) | P2 - Medium | 4 | Required for Asian languages |
| Type 3 fonts | P3 - Low | Future | Rare |
| Invisible text (Tr=3) | P1 - High | 3 | Security critical |
| ActualText in marked content | P2 - Medium | Future | Accessibility PDFs |
| Annotation appearance streams | P3 - Low | Future | Forms, comments |
| Metadata (/Info, XMP) | P2 - Medium | Future | Separate from content redaction |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         PdfEditor.Redaction                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌──────────────────┐    ┌──────────────────┐    ┌───────────────┐ │
│  │  RedactionEngine │───▶│  LetterProvider  │───▶│    PdfPig     │ │
│  │  (Orchestrator)  │    │  (Extraction)    │    │  (Letters)    │ │
│  └────────┬─────────┘    └──────────────────┘    └───────────────┘ │
│           │                                                         │
│           ▼                                                         │
│  ┌──────────────────┐    ┌──────────────────┐                      │
│  │ LetterOperation  │───▶│  ContentStream   │                      │
│  │    Matcher       │    │    Locator       │                      │
│  └────────┬─────────┘    └──────────────────┘                      │
│           │                                                         │
│           ▼                                                         │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                    Operator Handlers                          │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────────┐ │  │
│  │  │    Tj    │  │    TJ    │  │  Quote   │  │  Positioning │ │  │
│  │  │ Handler  │  │ Handler  │  │ Handler  │  │   Handler    │ │  │
│  │  └──────────┘  └──────────┘  └──────────┘  └──────────────┘ │  │
│  └──────────────────────────────────────────────────────────────┘  │
│           │                                                         │
│           ▼                                                         │
│  ┌──────────────────┐    ┌──────────────────┐                      │
│  │  ContentStream   │───▶│   PDF Output     │                      │
│  │    Rewriter      │    │  (PdfSharpCore)  │                      │
│  └──────────────────┘    └──────────────────┘                      │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
1. Input: PDF bytes + Redaction area (rectangle)
                    │
                    ▼
2. PdfPig extracts all Letters with accurate positions
                    │
                    ▼
3. Identify Letters inside redaction area (same as text selection)
                    │
                    ▼
4. For each Letter to redact:
   a. Locate the content stream operator that produced it
   b. Identify the byte position of that glyph within the operator
                    │
                    ▼
5. For each affected operator:
   a. Parse the operator and its operands
   b. Remove the specific glyph bytes
   c. Emit the modified operator
                    │
                    ▼
6. Rebuild content stream with:
   - All unaffected operators unchanged
   - Modified text operators with glyphs removed
                    │
                    ▼
7. Replace page content stream in PDF
                    │
                    ▼
8. Optionally draw black rectangle overlay
                    │
                    ▼
9. Output: Modified PDF bytes
```

---

## Directory Structure

```
/home/marc/pdfe/
├── PdfEditor.Redaction/                 # Independent redaction library
│   ├── PdfEditor.Redaction.csproj
│   ├── IMPLEMENTATION_PLAN.md           # This document
│   │
│   ├── Core/                            # Core abstractions
│   │   ├── IRedactionEngine.cs          # Main entry point
│   │   ├── ILetterProvider.cs           # Letter extraction abstraction
│   │   ├── IContentStreamModifier.cs    # Content stream modification
│   │   ├── RedactionRequest.cs          # Immutable request object
│   │   └── RedactionResult.cs           # Result with diagnostics
│   │
│   ├── LetterAnalysis/                  # PdfPig-based extraction
│   │   ├── PdfPigLetterProvider.cs      # Extracts Letters from PDF
│   │   ├── PositionedLetter.cs          # Letter with coordinate info
│   │   ├── LetterGroup.cs               # Groups letters by text run
│   │   └── LetterGrouper.cs             # Groups letters logically
│   │
│   ├── ContentStream/                   # Content stream manipulation
│   │   ├── Parsing/
│   │   │   ├── ContentStreamTokenizer.cs    # Low-level byte tokenizer
│   │   │   ├── TextOperationLocator.cs      # Finds text operations
│   │   │   ├── OperatorInfo.cs              # Operator metadata
│   │   │   └── EncodingResolver.cs          # Font encoding handling
│   │   │
│   │   ├── Operators/                   # Per-operator handlers
│   │   │   ├── ITextOperatorHandler.cs      # Handler interface
│   │   │   ├── TjOperatorHandler.cs         # Simple string: (text) Tj
│   │   │   ├── TJOperatorHandler.cs         # Array: [(a) 10 (b)] TJ
│   │   │   ├── QuoteOperatorHandler.cs      # ' and " operators
│   │   │   └── TextOperatorRegistry.cs      # Registry of handlers
│   │   │
│   │   ├── Building/
│   │   │   ├── ContentStreamRewriter.cs     # Rewrite with modifications
│   │   │   ├── PartialTextEmitter.cs        # Emit partial text ops
│   │   │   └── ByteArrayBuilder.cs          # Efficient byte building
│   │   │
│   │   └── XObjects/
│   │       ├── FormXObjectLocator.cs        # Find Form XObjects
│   │       └── XObjectContentHandler.cs     # Handle XObject streams
│   │
│   ├── Matching/                        # Letter to operation matching
│   │   ├── LetterOperationMatcher.cs    # Core matching algorithm
│   │   ├── MatchResult.cs               # Match result with confidence
│   │   ├── FontMatcher.cs               # Match by font properties
│   │   └── SpatialIndex.cs              # Spatial lookup optimization
│   │
│   ├── Redaction/                       # Redaction orchestration
│   │   ├── RedactionEngine.cs           # Main orchestrator
│   │   ├── GlyphRemover.cs              # Remove specific glyphs
│   │   ├── VisualOverlayGenerator.cs    # Black box overlay
│   │   └── RedactionValidator.cs        # Verify redaction worked
│   │
│   └── Diagnostics/                     # Logging and debugging
│       ├── RedactionDiagnostics.cs      # Diagnostic data collection
│       ├── MatchingReport.cs            # Detailed matching report
│       ├── ContentStreamDumper.cs       # Dump stream for debugging
│       └── LetterMapVisualizer.cs       # Visualize letter positions
│
├── PdfEditor.Redaction.Tests/           # Test library
│   ├── PdfEditor.Redaction.Tests.csproj
│   │
│   ├── Unit/                            # Unit tests by module
│   │   ├── LetterAnalysis/
│   │   │   ├── PdfPigLetterProviderTests.cs
│   │   │   ├── LetterGrouperTests.cs
│   │   │   └── PositionedLetterTests.cs
│   │   │
│   │   ├── ContentStream/
│   │   │   ├── Parsing/
│   │   │   │   ├── ContentStreamTokenizerTests.cs
│   │   │   │   ├── TextOperationLocatorTests.cs
│   │   │   │   └── EncodingResolverTests.cs
│   │   │   │
│   │   │   ├── Operators/
│   │   │   │   ├── TjOperatorHandlerTests.cs
│   │   │   │   ├── TJOperatorHandlerTests.cs
│   │   │   │   └── QuoteOperatorHandlerTests.cs
│   │   │   │
│   │   │   ├── Building/
│   │   │   │   ├── ContentStreamRewriterTests.cs
│   │   │   │   └── PartialTextEmitterTests.cs
│   │   │   │
│   │   │   └── XObjects/
│   │   │       └── FormXObjectLocatorTests.cs
│   │   │
│   │   └── Matching/
│   │       ├── LetterOperationMatcherTests.cs
│   │       └── SpatialIndexTests.cs
│   │
│   ├── Integration/                     # End-to-end tests
│   │   ├── SimpleTjRedactionTests.cs        # Phase 1
│   │   ├── TJArrayRedactionTests.cs         # Phase 2
│   │   ├── PositioningRedactionTests.cs     # Phase 3
│   │   ├── EncodingRedactionTests.cs        # Phase 4
│   │   ├── XObjectRedactionTests.cs         # Phase 5
│   │   ├── RealWorldPdfTests.cs             # Phase 6
│   │   └── ExternalValidationTests.cs       # pdftotext/qpdf validation
│   │
│   ├── Fixtures/                        # Test PDF generation
│   │   ├── TestPdfFactory.cs            # Main factory class
│   │   ├── TjTestPdfBuilder.cs          # Tj operator test PDFs
│   │   ├── TJTestPdfBuilder.cs          # TJ operator test PDFs
│   │   ├── PositioningTestPdfBuilder.cs # Various positioning scenarios
│   │   ├── EncodingTestPdfBuilder.cs    # Font encoding scenarios
│   │   ├── XObjectTestPdfBuilder.cs     # Form XObject scenarios
│   │   └── ScrambledOrderPdfBuilder.cs  # Out-of-order letter streams
│   │
│   └── Resources/
│       └── sample-pdfs/                 # Real-world test PDFs
│           ├── birth-certificate-request-scrambled.pdf
│           ├── README.md                # Description of each PDF
│           └── (additional problematic PDFs)
│
└── lib-sources/                         # Library source code (gitignored)
    ├── PdfPig/                          # PdfPig source for reference
    └── PdfSharpCore/                    # PdfSharpCore source for reference
```

---

## Core Interfaces and Classes

### IRedactionEngine (Main Entry Point)

```csharp
/// <summary>
/// Main entry point for PDF redaction operations.
/// Handles the complete workflow from letter identification to content stream modification.
/// </summary>
public interface IRedactionEngine
{
    /// <summary>
    /// Redact text within the specified area from a PDF page.
    /// </summary>
    /// <param name="pdfStream">Input PDF stream (will not be modified)</param>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="redactionArea">Area in PDF points (bottom-left origin)</param>
    /// <param name="options">Redaction options</param>
    /// <returns>Result containing modified PDF and diagnostics</returns>
    RedactionResult Redact(
        Stream pdfStream,
        int pageNumber,
        PdfRectangle redactionArea,
        RedactionOptions? options = null);

    /// <summary>
    /// Analyze a page and return detailed diagnostics.
    /// Useful for debugging matching issues.
    /// </summary>
    PageAnalysis AnalyzePage(Stream pdfStream, int pageNumber);

    /// <summary>
    /// Get all letters from a page with their positions.
    /// </summary>
    IReadOnlyList<PositionedLetter> GetLetters(Stream pdfStream, int pageNumber);
}
```

### ILetterProvider (Letter Extraction)

```csharp
/// <summary>
/// Abstracts letter extraction from PDF pages.
/// Primary implementation uses PdfPig.
/// </summary>
public interface ILetterProvider
{
    /// <summary>
    /// Extract all letters from a page with position information.
    /// </summary>
    IReadOnlyList<PositionedLetter> GetLetters(Stream pdfStream, int pageNumber);

    /// <summary>
    /// Get letters that fall within the specified area.
    /// Uses center-point containment by default.
    /// </summary>
    IReadOnlyList<PositionedLetter> GetLettersInArea(
        Stream pdfStream,
        int pageNumber,
        PdfRectangle area);
}
```

### ITextOperatorHandler (Per-Operator Logic)

```csharp
/// <summary>
/// Handles parsing and modification of a specific text operator type.
/// Each operator type (Tj, TJ, ', ") has its own handler.
/// </summary>
public interface ITextOperatorHandler
{
    /// <summary>
    /// The operator name(s) this handler processes.
    /// </summary>
    IReadOnlyList<string> SupportedOperators { get; }

    /// <summary>
    /// Parse the operator and extract glyph information.
    /// </summary>
    /// <param name="operatorBytes">Raw bytes of the operator including operands</param>
    /// <param name="textState">Current text state (font, size, etc.)</param>
    /// <param name="graphicsState">Current graphics state (CTM, etc.)</param>
    /// <returns>Information about each glyph produced by this operator</returns>
    OperatorParseResult Parse(
        ReadOnlySpan<byte> operatorBytes,
        TextState textState,
        GraphicsState graphicsState);

    /// <summary>
    /// Emit a modified version of the operator with specified glyphs removed.
    /// </summary>
    /// <param name="operatorBytes">Original operator bytes</param>
    /// <param name="glyphIndicesToRemove">Indices of glyphs to remove (0-based)</param>
    /// <param name="textState">Text state for encoding</param>
    /// <returns>Modified operator bytes (may be empty if all glyphs removed)</returns>
    byte[] EmitWithRemovals(
        ReadOnlySpan<byte> operatorBytes,
        IReadOnlySet<int> glyphIndicesToRemove,
        TextState textState);
}
```

### PositionedLetter (Letter with Context)

```csharp
/// <summary>
/// A letter extracted from a PDF with full position and context information.
/// Wraps PdfPig's Letter with additional metadata for matching.
/// </summary>
public sealed class PositionedLetter
{
    /// <summary>The Unicode character value.</summary>
    public string Value { get; }

    /// <summary>Glyph bounding box in PDF coordinates (bottom-left origin).</summary>
    public PdfRectangle GlyphBounds { get; }

    /// <summary>Center point of the glyph.</summary>
    public PdfPoint Center { get; }

    /// <summary>Font name as specified in the PDF.</summary>
    public string FontName { get; }

    /// <summary>Font size in points.</summary>
    public double FontSize { get; }

    /// <summary>Index of this letter in the page's letter sequence.</summary>
    public int LetterIndex { get; }

    /// <summary>Original PdfPig Letter object for additional data access.</summary>
    public Letter OriginalLetter { get; }

    /// <summary>
    /// Check if this letter's center is within the specified area.
    /// This is the primary method for determining if a letter should be redacted.
    /// </summary>
    public bool CenterWithin(PdfRectangle area);

    /// <summary>
    /// Check if this letter's glyph intersects with the specified area.
    /// More aggressive than center-point checking.
    /// </summary>
    public bool Intersects(PdfRectangle area);
}
```

### RedactionResult (Result with Diagnostics)

```csharp
/// <summary>
/// Result of a redaction operation with detailed diagnostics.
/// </summary>
public sealed class RedactionResult
{
    /// <summary>Whether the redaction was successful.</summary>
    public bool Success { get; }

    /// <summary>The modified PDF bytes (null if failed).</summary>
    public byte[]? ModifiedPdf { get; }

    /// <summary>Letters that were successfully redacted.</summary>
    public IReadOnlyList<PositionedLetter> RedactedLetters { get; }

    /// <summary>Letters that could not be redacted (with reasons).</summary>
    public IReadOnlyList<RedactionFailure> FailedLetters { get; }

    /// <summary>Content stream operators that were modified.</summary>
    public IReadOnlyList<ModifiedOperator> ModifiedOperators { get; }

    /// <summary>Detailed diagnostics for debugging.</summary>
    public RedactionDiagnostics Diagnostics { get; }

    /// <summary>Error message if the operation failed.</summary>
    public string? ErrorMessage { get; }
}
```

---

## Test-Driven Implementation Phases

### Phase 0: Infrastructure Setup
**Duration**: 1-2 days
**Goal**: Project skeleton with test infrastructure

- [ ] Create project files (.csproj)
- [ ] Add package references (PdfPig, PdfSharpCore, xUnit, FluentAssertions)
- [ ] Set up logging infrastructure (Microsoft.Extensions.Logging)
- [ ] Create basic TestPdfFactory that generates minimal PDFs
- [ ] Verify PdfPig can read generated PDFs
- [ ] Set up code coverage reporting

**Tests**:
- TestPdfFactory can create a PDF with "Hello" using Tj operator
- PdfPig can extract letters from generated PDF
- Letter positions are accurate

### Phase 1: Simple Tj Operator
**Duration**: 3-5 days
**Goal**: Full redaction support for `(text) Tj` operator

#### 1.1 Letter Extraction
- [ ] Implement `PdfPigLetterProvider`
- [ ] Implement `PositionedLetter` wrapper
- [ ] Test letter extraction from simple PDFs

**Tests**:
```
- Extract letters from "(Hello) Tj" - verify 5 letters
- Extract letters from "(Hello World) Tj" - verify 11 letters including space
- Verify letter positions are in PDF coordinates
- Verify font name and size are captured
```

#### 1.2 Tj Parsing
- [ ] Implement `ContentStreamTokenizer` (low-level)
- [ ] Implement `TjOperatorHandler.Parse()`
- [ ] Handle literal strings with escapes
- [ ] Handle hex strings

**Tests**:
```
- Parse "(Hello) Tj" - 5 glyphs at correct positions
- Parse "(Hello\(World\)) Tj" - handles escaped parens
- Parse "(\101\102) Tj" - handles octal escapes
- Parse "<48656C6C6F> Tj" - handles hex strings
- Parse "() Tj" - handles empty string
```

#### 1.3 Tj Modification
- [ ] Implement `TjOperatorHandler.EmitWithRemovals()`
- [ ] Remove single character
- [ ] Remove multiple characters
- [ ] Remove all characters

**Tests**:
```
- Remove index 0 from "(Hello) Tj" → "(ello) Tj"
- Remove index 4 from "(Hello) Tj" → "(Hell) Tj"
- Remove indices 1,2,3 from "(Hello) Tj" → "(Ho) Tj"
- Remove all indices → operator removed entirely
- Handle escape sequences during removal
```

#### 1.4 Tj Integration
- [ ] Implement `ContentStreamRewriter` for Tj
- [ ] Full redaction workflow for simple PDFs
- [ ] Verify with pdftotext

**Tests**:
```
- Redact "World" from "Hello World" PDF
- Verify pdftotext shows "Hello" only
- Redact first word, verify second remains
- Redact middle characters, verify surrounding remain
- Multiple redactions on same page
```

### Phase 2: TJ Array Operator
**Duration**: 3-5 days
**Goal**: Full redaction support for `[(a) 10 (bc)] TJ` operator

#### 2.1 TJ Parsing
- [ ] Implement `TJOperatorHandler.Parse()`
- [ ] Handle array structure
- [ ] Handle numeric positioning values
- [ ] Map array elements to glyph indices

**Tests**:
```
- Parse "[(Hello)] TJ" - single element array
- Parse "[(H) (e) (l) (l) (o)] TJ" - one char per element
- Parse "[(H) -20 (ello)] TJ" - with kerning
- Parse "[(AB) 50 (CD) -30 (EF)] TJ" - complex array
- Verify kerning values affect positions
```

#### 2.2 TJ Modification
- [ ] Implement `TJOperatorHandler.EmitWithRemovals()`
- [ ] Remove from single element
- [ ] Remove spanning multiple elements
- [ ] Preserve numeric values

**Tests**:
```
- Remove 'e' from "[(H) -20 (ello)] TJ"
- Remove 'H' (first element becomes empty)
- Remove spanning boundary: 'Hel' across elements
- Verify numeric kerning values preserved
- Remove all → operator removed
```

#### 2.3 TJ Integration
- [ ] Full redaction for TJ-based PDFs
- [ ] Mixed Tj and TJ in same PDF

**Tests**:
```
- Create PDF using TJ with kerning
- Redact specific word
- Verify positioning of remaining text unchanged
- Verify with pdftotext
```

### Phase 3: Quote Operators and Positioning
**Duration**: 2-3 days
**Goal**: Support for `'`, `"` operators and positioning operators

#### 3.1 Quote Operators
- [ ] Implement `QuoteOperatorHandler`
- [ ] Handle `'` (next line and show)
- [ ] Handle `"` (set spacing, next line, show)

**Tests**:
```
- Parse "(Hello) '" - same as T* then Tj
- Parse "10 20 (Hello) \"" - with spacing operands
- Modify quote operators same as Tj
```

#### 3.2 Positioning State Tracking
- [ ] Track `Tm`, `Td`, `TD`, `T*` effects
- [ ] Maintain text state through BT...ET blocks
- [ ] Handle font changes within block

**Tests**:
```
- Multi-line text with Td positioning
- Text at various Tm positions
- Font size changes affecting positions
- Verify positions match PdfPig output
```

### Phase 4: Font Encodings
**Duration**: 3-5 days
**Goal**: Handle various font encoding schemes

#### 4.1 Standard Encodings
- [ ] WinAnsiEncoding
- [ ] MacRomanEncoding
- [ ] StandardEncoding

**Tests**:
```
- PDF with WinAnsiEncoding font
- Special characters (accented, symbols)
- Verify byte values match encoding
```

#### 4.2 Custom Encodings
- [ ] Handle /Encoding /Differences
- [ ] Handle /ToUnicode CMap
- [ ] Reverse mapping for modification

**Tests**:
```
- Font with custom /Differences array
- Font with /ToUnicode CMap
- Glyph name mapping
```

#### 4.3 CID Fonts (if needed)
- [ ] Handle Identity-H encoding
- [ ] Handle CID font structure
- [ ] Multi-byte character handling

**Tests**:
```
- CJK text with CID font
- Multi-byte glyph codes
```

### Phase 5: Form XObjects
**Duration**: 2-3 days
**Goal**: Handle text in Form XObjects

#### 5.1 XObject Detection
- [ ] Identify text in XObjects vs page content
- [ ] Locate correct XObject to modify

**Tests**:
```
- PDF with text in Form XObject
- Detect which XObject contains target text
```

#### 5.2 XObject Modification
- [ ] Apply same operator handlers to XObject streams
- [ ] Handle XObject matrix transformations

**Tests**:
```
- Redact text from Form XObject
- Verify XObject matrix doesn't break positioning
- XObject used multiple times on page
```

### Phase 6: Real-World Validation
**Duration**: 3-5 days
**Goal**: Validate against problematic real-world PDFs

#### 6.1 Scrambled Order PDFs
- [ ] Test with birth-certificate-request PDF
- [ ] Handle out-of-order content streams

**Tests**:
```
- Redact "FIRST" from scrambled PDF
- Redact "MAKING" from scrambled PDF
- Verify no over-redaction
- Verify text extraction shows removal
```

#### 6.2 External Tool Validation
- [ ] Validate with pdftotext
- [ ] Validate with qpdf
- [ ] Validate with mutool

**Tests**:
```
- pdftotext cannot extract redacted text
- qpdf reports valid PDF structure
- mutool clean succeeds
```

#### 6.3 Performance Baseline
- [ ] Establish timing baselines
- [ ] Profile hot paths
- [ ] Document acceptable performance

---

## Test Fixtures and Resources

### Generated Test PDFs

Each test PDF builder creates PDFs with specific characteristics:

#### TjTestPdfBuilder
```csharp
// Creates PDFs using simple Tj operator
TjTestPdfBuilder.CreateSimpleAscii("Hello World");
TjTestPdfBuilder.CreateWithEscapes("Hello (World)");
TjTestPdfBuilder.CreateHexString("Hello");
TjTestPdfBuilder.CreateMultipleLines("Line 1", "Line 2");
```

#### TJTestPdfBuilder
```csharp
// Creates PDFs using TJ array operator
TJTestPdfBuilder.CreateSimpleArray("Hello");
TJTestPdfBuilder.CreateWithKerning("Hello", kerning: -20);
TJTestPdfBuilder.CreateMixedArray("Hello World");
TJTestPdfBuilder.CreatePerCharacterArray("ABC");
```

#### ScrambledOrderPdfBuilder
```csharp
// Creates PDFs with letters in non-reading-order
ScrambledOrderPdfBuilder.CreateScrambled("Hello World");
ScrambledOrderPdfBuilder.CreatePerCharacterTm("Hello"); // Each char has own Tm
```

### Real-World Test PDFs

Store in `Resources/sample-pdfs/`:

| File | Description | Issues Demonstrated |
|------|-------------|---------------------|
| `birth-certificate-request-scrambled.pdf` | Official form with scrambled letters | Out-of-order content stream |
| (add more as discovered) | | |

### Test Validation Utilities

```csharp
public static class RedactionTestHelpers
{
    /// <summary>
    /// Extract text from PDF using pdftotext and verify redacted text is gone.
    /// </summary>
    public static void VerifyTextRemoved(byte[] pdf, string redactedText);

    /// <summary>
    /// Verify PDF structure is valid using qpdf.
    /// </summary>
    public static void VerifyValidStructure(byte[] pdf);

    /// <summary>
    /// Extract text using PdfPig and verify redacted text is gone.
    /// </summary>
    public static void VerifyPdfPigTextRemoved(byte[] pdf, string redactedText);
}
```

---

## Library Source Code Analysis

### Why Download Library Sources

1. **Understand internals**: See exactly how PdfPig's Letter objects are created
2. **Find hidden APIs**: Internal/private members that expose useful data
3. **Debug into library**: Step through parsing to understand behavior
4. **Verify assumptions**: Confirm how edge cases are handled

### PdfPig Analysis Goals

Key questions to answer by examining PdfPig source:

1. **Letter → Operator Mapping**: Does PdfPig track which operator produced each Letter?
2. **Internal Data**: What internal properties exist on Letter that aren't public?
3. **Content Stream Access**: Can we get raw content stream bytes through PdfPig?
4. **Font Access**: How does PdfPig access font dictionaries for encoding?

### PdfSharpCore Analysis Goals

1. **Content Stream Manipulation**: How does it read/write content streams?
2. **Byte-Level Access**: Can we do surgical byte modifications?
3. **Structure Preservation**: Does it preserve structure on modification?

### Potential Fork Decision Points

**Fork PdfPig if**:
- It internally tracks Letter → operator mapping but doesn't expose it
- Adding a small public API would give us what we need
- The change is low-risk and maintainable

**Fork PdfSharpCore if**:
- We need lower-level content stream access
- Current APIs rewrite/recompress content unnecessarily

**Don't fork if**:
- We can achieve goals through public APIs
- Workarounds are simple and maintainable

---

## Quality Requirements

### Code Coverage Targets

| Category | Target | Notes |
|----------|--------|-------|
| Unit Tests | > 95% | All code paths tested |
| Integration Tests | > 90% | All operator types covered |
| Branch Coverage | > 90% | All conditionals tested |
| Real-World Tests | 100% of known problematic PDFs | No regressions |

### Logging Requirements

Every significant operation must log:
- **Input parameters** (sanitized)
- **Decision points** (why a path was taken)
- **Results** (what was done)
- **Timing** (for performance analysis)

Log levels:
- **Trace**: Byte-level operations, individual glyph processing
- **Debug**: Operator parsing, matching decisions
- **Information**: Redaction requests, results
- **Warning**: Fallback behaviors, partial failures
- **Error**: Failures that prevent redaction

### Documentation Requirements

- XML documentation on all public members
- README in each directory explaining purpose
- Architecture decision records for significant choices
- Examples in test code

---

## Risk Mitigation

### Risk: Font Encoding Complexity

**Mitigation**:
- Start with standard encodings
- Log warnings for unsupported encodings
- Graceful degradation to operation-level redaction as fallback

### Risk: XObject Complexity

**Mitigation**:
- Detect XObject text and warn if cannot redact
- Implement XObject support in later phase
- Document limitations clearly

### Risk: Performance Issues

**Mitigation**:
- Focus on correctness first
- Profile after functionality complete
- Use spatial indexing for large documents
- Cache font encoding mappings

### Risk: PDF Structure Corruption

**Mitigation**:
- Validate output with qpdf after every modification
- Test with multiple PDF viewers
- Keep modifications surgical (minimal changes)

### Risk: Incomplete Redaction

**Mitigation**:
- Always verify with external tools (pdftotext)
- Return detailed diagnostics showing what was/wasn't redacted
- Log warnings for anything that couldn't be processed

---

## Next Steps

1. **Create project files** and add to solution
2. **Download library sources** for reference
3. **Implement Phase 0** (infrastructure)
4. **Begin Phase 1** (Tj operator)

This plan will be updated as implementation progresses and new insights are gained.
