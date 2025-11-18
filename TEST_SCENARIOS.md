# Comprehensive Test Scenarios for PDF Redaction

This document outlines all test scenarios that validate PDF redaction functionality.

## Overview

**Total Tests: 26 comprehensive tests**
- 5 original integration tests
- 7 comprehensive redaction tests
- 8 black box redaction tests
- 5 specialized edge case tests
- 1 DPI theory test (3 variations)

**Demo Program: 6 demonstrations**
- Generates 12 PDFs (6 original, 6 redacted)

---

## Specialized Edge Case Tests

### 1. ✅ Text-Only Documents (No Shapes)

**Test:** `TextOnlyDocument_BlackBoxRedactsText`

**Scenario:**
- PDF contains ONLY text, no shapes or graphics
- Multiple text sections: confidential and public
- Black boxes applied over specific confidential sections

**Content:**
```
Header Text
CONFIDENTIAL SECTION  ← Black Box 1
  line 1 of confidential data
  line 2 of confidential data
PUBLIC SECTION
  public information line 1
  public information line 2
ANOTHER CONFIDENTIAL BLOCK  ← Black Box 2
  Secret data here
Footer - Public
```

**Verification:**
- ✓ Confidential sections REMOVED from PDF structure
- ✓ Public sections PRESERVED
- ✓ No shapes in document (pure text test)

**Demo Files:**
- `04_text_only_original.pdf`
- `04_text_only_redacted.pdf`

---

### 2. ✅ Shapes-Only Documents (No Text)

**Test:** `ShapesOnlyDocument_BlackBoxRedactsShapes`

**Scenario:**
- PDF contains ONLY shapes/graphics, NO text at all
- Multiple shapes: rectangles, circles, triangles
- Black boxes applied over specific shapes

**Content:**
```
Blue Rectangle (50, 50)      ← Black Box 1
Green Circle (300, 50)       (preserved)
Red Rectangle (100, 250)     ← Black Box 2
Yellow Rectangle (50, 400)   (preserved)
Purple Rectangle (350, 400)  (preserved)
Orange Triangle (100, 600)   (preserved)
```

**Verification:**
- ✓ Shapes under black boxes REMOVED from content stream
- ✓ Other shapes PRESERVED
- ✓ Content stream size changes (proof of modification)
- ✓ No text in document (pure shapes test)

**Demo Files:**
- `05_shapes_only_original.pdf`
- `05_shapes_only_redacted.pdf`

---

### 3. ✅ Layered/Overlapping Shapes

**Test:** `LayeredShapes_BlackBoxCoversMultipleLayers_AllRedacted`

**Scenario:**
- PDF with multiple shapes drawn ON TOP of each other
- 4 overlapping layers in same area
- Single large black box covers entire layered area

**Content:**
```
Layered Area (100, 100 to 500, 400):
  Layer 1: Gray background (bottom)
  Layer 2: Blue rectangle
  Layer 3: Green rectangle
  Layer 4: Red circle (top)
  ← Single Black Box covers ALL layers

Separate Area:
  Purple rectangle (preserved)
```

**Verification:**
- ✓ ALL 4 layers under black box REMOVED
- ✓ Text labels for all layers REMOVED
- ✓ Separate shape outside area PRESERVED
- ✓ Proves black box affects all layers, not just top layer

**Demo Files:**
- `06_layered_shapes_original.pdf`
- `06_layered_shapes_redacted.pdf`

---

### 4. ✅ Partial Shape Coverage

**Test:** `PartialShapeCoverage_OnlyIntersectingPortionRedacted`

**Scenario:**
- Large shapes partially covered by black boxes
- Black box covers only portion of shape

**Content:**
```
Large Blue Rectangle (50, 100, 400x150)
  ← Black Box covers left portion only

Green Circle (100, 350, 200x200)
  ← Black Box covers top portion only

Long Text Line
  ← Black Box covers middle portion only
```

**Verification:**
- ✓ Shapes/text that intersect with black box are affected
- ✓ Currently removes entire intersecting operation
- ✓ Future enhancement: split operations for partial removal

**Note:** Current implementation removes entire PDF operation if any part intersects. Splitting operations (e.g., removing only part of a rectangle) is a future enhancement.

---

### 5. ✅ Multiple Shapes in Single Area

**Test:** `MultipleShapesInArea_AllRedacted`

**Scenario:**
- Cluster of 9 small shapes (grid of rectangles and circles)
- One large black box covers entire cluster
- Separate shape outside cluster

**Content:**
```
Cluster (100, 100 to 300, 300):
  [Red] [Blue] [Green]
  [Yellow] [Orange] [Purple]  ← Single Large Black Box
  [Pink] [Cyan] [Magenta]

Separate Area:
  Light Blue Rectangle (preserved)
```

**Verification:**
- ✓ All 9 shapes in cluster REMOVED
- ✓ Separate shape PRESERVED
- ✓ Proves multiple operations under single black box are removed

---

## Other Comprehensive Tests

### Random Redaction Areas
**Test:** `RedactRandomAreas_ShouldOnlyRemoveIntersectingContent`
- Grid of text cells at known positions
- 3 random black boxes at random locations
- Verifies selective removal

### Complex Documents
**Test:** `ComplexDocument_TargetedBlackBoxes_OnlySensitiveDataRemoved`
- Mix of sensitive and public data
- Targeted redaction of specific sections
- Verifies precision

### Mapped Content
**Test:** `RedactMappedContent_ShouldRemoveOnlyTargetedItems`
- Content with exact position mappings
- Precise coordinate-based redaction
- Verifies accuracy

### DPI Variations
**Test:** `RedactAtVariousDPI_ShouldWorkCorrectly`
- Tests at 72, 150, 300 DPI
- Verifies coordinate scaling

### Content Integrity
**Test:** `RandomBlackBoxes_VerifyContentIntegrity_NoCrosstalk`
- Single black box over one item
- Ensures no unintended removal

### Permanent Removal
**Test:** `SaveAndReload_VerifyPermanentRemoval`
- Saves and reloads PDF
- Verifies removal persists
- Checks content stream level

---

## Running the Tests

### All Tests
```bash
cd PdfEditor.Tests
dotnet test
```

### Specialized Tests Only
```bash
dotnet test --filter "FullyQualifiedName~SpecializedRedactionTests"
```

### Specific Edge Case Test
```bash
dotnet test --filter "TextOnlyDocument_BlackBoxRedactsText"
dotnet test --filter "ShapesOnlyDocument_BlackBoxRedactsShapes"
dotnet test --filter "LayeredShapes_BlackBoxCoversMultipleLayers"
```

---

## Running the Demo

### Full Demo (All 6 Scenarios)
```bash
./run-demo.sh  # Linux/macOS
run-demo.bat   # Windows
```

### Generated Files
The demo creates `RedactionDemo/` with 12 PDFs:

**Original PDFs:**
1. `01_simple_original.pdf` - Simple text
2. `02_complex_original.pdf` - Mixed content
3. `03_random_original.pdf` - Grid content
4. `04_text_only_original.pdf` - **Text only, no shapes**
5. `05_shapes_only_original.pdf` - **Shapes only, no text**
6. `06_layered_shapes_original.pdf` - **Overlapping layers**

**Redacted PDFs:**
1. `01_simple_redacted.pdf`
2. `02_complex_redacted.pdf`
3. `03_random_redacted.pdf`
4. `04_text_only_redacted.pdf` - **Confidential text removed**
5. `05_shapes_only_redacted.pdf` - **Shapes removed**
6. `06_layered_shapes_redacted.pdf` - **All layers removed**

---

## Test Coverage Summary

| Scenario | Text Only | Shapes Only | Mixed | Layered | Partial |
|----------|-----------|-------------|-------|---------|---------|
| Simple text redaction | ✓ | - | - | - | - |
| Complex document | - | - | ✓ | - | - |
| Random areas | - | - | ✓ | - | - |
| **Text-only document** | **✓** | - | - | - | - |
| **Shapes-only document** | - | **✓** | - | - | - |
| **Layered shapes** | - | - | - | **✓** | - |
| **Partial coverage** | - | - | - | - | **✓** |
| Multiple in area | - | ✓ | - | - | - |
| Mapped content | - | - | ✓ | - | - |
| DPI variations | - | - | ✓ | - | - |
| Content integrity | - | - | ✓ | - | - |
| Permanent removal | - | - | ✓ | - | - |

---

## Verification Methods

### 1. Text Extraction (PdfPig)
- Extracts actual text from PDF structure
- Confirms text is removed, not just hidden
- Works for text-only and mixed documents

### 2. Content Stream Analysis
- Examines raw PDF content stream bytes
- Verifies operations are removed
- Works for shapes-only documents

### 3. Word Count Comparison
- Counts words before/after redaction
- Verifies selective removal
- Confirms preservation of non-redacted content

### 4. Visual Inspection
- Open PDFs in any viewer
- See black boxes
- Try searching for removed content (won't find it)

---

## Edge Cases Covered

✅ **Text without shapes** - Pure text documents
✅ **Shapes without text** - Pure graphics documents
✅ **Layered content** - Multiple overlapping elements
✅ **Partial coverage** - Black box covers part of shape
✅ **Multiple elements** - Cluster of items under one black box
✅ **Random positions** - Arbitrary redaction locations
✅ **DPI variations** - Different rendering resolutions
✅ **Content preservation** - Non-redacted content intact
✅ **Permanent removal** - Survives save/reload
✅ **Structure integrity** - PDF remains valid

---

## What's NOT Covered (Future Enhancements)

⚠️ **Inline Images** - `BI...ID...EI` operators
⚠️ **Rotated Pages** - Page rotation transformations
⚠️ **Form XObjects** - Nested content streams
⚠️ **Clipping Paths** - Complex clipping operations
⚠️ **Split Operations** - Partial shape removal (removes entire shape if any part intersects)
⚠️ **Metadata** - PDF metadata, XMP, Info dictionary
⚠️ **Revision History** - Multiple PDF revisions

---

## Expected Results

When you run the tests or demo:

✅ **All tests pass** (26 tests)
✅ **Content under black boxes is removed** from PDF structure
✅ **Content outside black boxes is preserved**
✅ **PDFs remain structurally valid** after redaction
✅ **Removal is permanent** (not just visual hiding)
✅ **Works with text-only documents**
✅ **Works with shapes-only documents**
✅ **Works with layered/overlapping content**
✅ **Handles partial coverage**
✅ **Handles multiple items in one area**

---

## Files Reference

**Test Files:**
- `PdfEditor.Tests/Integration/RedactionIntegrationTests.cs` - Original 5 tests
- `PdfEditor.Tests/Integration/ComprehensiveRedactionTests.cs` - 7 comprehensive tests
- `PdfEditor.Tests/Integration/BlackBoxRedactionTests.cs` - 8 black box tests
- `PdfEditor.Tests/Integration/SpecializedRedactionTests.cs` - 5 edge case tests

**Generators:**
- `PdfEditor.Tests/Utilities/TestPdfGenerator.cs` - All PDF generators

**Demo:**
- `PdfEditor.Demo/Program.cs` - Demonstration program

**Documentation:**
- `TEST_SUITE_GUIDE.md` - Complete test suite guide
- `RUNNING_THE_DEMO.md` - How to run the demo
- `TEST_SCENARIOS.md` - This file
