# Running the PDF Redaction Demonstration

This guide shows you how to run the demonstration program that validates PDF redaction functionality.

## What the Demo Does

The demonstration program:

1. **Generates sample PDFs** with text and shapes at known positions
2. **Applies black box redactions** at specific and random locations
3. **Saves redacted PDFs** to disk
4. **Re-opens the PDFs** in new instances
5. **Verifies content is removed** from the PDF structure (not just visually hidden)
6. **Preserves non-redacted content** to ensure we don't remove too much

## Quick Start

### On Linux/macOS:

```bash
cd /home/user/pdfe
./run-demo.sh
```

### On Windows:

```cmd
cd \home\user\pdfe
run-demo.bat
```

## What You'll See

### Console Output

```
=== PDF Redaction Demonstration ===

Output directory: /path/to/RedactionDemo

--- Test 1: Simple Text Redaction ---
✓ Created: 01_simple_original.pdf
  Content before: CONFIDENTIAL
This is public information
  Applying black box at: X=90, Y=90, W=150, H=30
✓ Saved: 01_simple_redacted.pdf
  Content after: This is public information
  ✓ SUCCESS: 'CONFIDENTIAL' was removed from PDF structure

--- Test 2: Complex Document with Shapes and Text ---
✓ Created: 02_complex_original.pdf
  Content items before:
    SECRET-DATA: EXISTS
    PUBLIC-INFO: EXISTS
    CONFIDENTIAL-123: EXISTS
  Applying black box over 'SECRET-DATA'
  Applying black box over 'CONFIDENTIAL-123'
  Applying black box over blue rectangle
✓ Saved: 02_complex_redacted.pdf
  Content items after:
    SECRET-DATA: REMOVED ✓
    CONFIDENTIAL-123: REMOVED ✓
    PUBLIC-INFO: PRESERVED ✓
  ✓ SUCCESS: Targeted content removed, other content preserved

--- Test 3: Random Redaction Areas ---
✓ Created: 03_random_original.pdf
  Words before redaction: 35
  Applying 3 random black boxes:
    Box 1: X=289, Y=142, W=88, H=71
    Box 2: X=199, Y=584, W=67, H=48
    Box 3: X=257, Y=355, W=109, H=52
✓ Saved: 03_random_redacted.pdf
  Words after redaction: 26
  ✓ SUCCESS: 9 words removed, 26 words preserved

=== All demonstrations complete! ===
```

### Generated PDF Files

The demo creates a `RedactionDemo` directory with 6 PDF files:

**Original PDFs (before redaction):**
1. `01_simple_original.pdf` - Simple document with "CONFIDENTIAL" text
2. `02_complex_original.pdf` - Complex document with sensitive and public data
3. `03_random_original.pdf` - Grid of text cells

**Redacted PDFs (after redaction):**
1. `01_simple_redacted.pdf` - "CONFIDENTIAL" removed with black box
2. `02_complex_redacted.pdf` - Sensitive data removed, public data preserved
3. `03_random_redacted.pdf` - Random areas redacted

## Verification Steps

### 1. Automatic Verification (Already Done)

The program automatically:
- Extracts text from original PDFs using PdfPig
- Extracts text from redacted PDFs
- Compares them to verify removed items are gone
- Confirms preserved items still exist
- Prints SUCCESS/FAILED for each test

### 2. Manual Visual Inspection

Open the generated PDFs in any PDF viewer:

```bash
# Linux
xdg-open RedactionDemo/01_simple_original.pdf
xdg-open RedactionDemo/01_simple_redacted.pdf

# macOS
open RedactionDemo/01_simple_original.pdf
open RedactionDemo/01_simple_redacted.pdf

# Windows
start RedactionDemo\01_simple_original.pdf
start RedactionDemo\01_simple_redacted.pdf
```

You should see:
- **Original**: All content visible
- **Redacted**: Black boxes where content was removed

### 3. Text Search Verification

Try searching for removed text in the redacted PDFs:

1. Open `02_complex_redacted.pdf`
2. Use PDF viewer's search function (Ctrl+F / Cmd+F)
3. Search for "SECRET-DATA" → **NOT FOUND** (removed from structure)
4. Search for "CONFIDENTIAL-123" → **NOT FOUND** (removed from structure)
5. Search for "PUBLIC-INFO" → **FOUND** (preserved)

This proves the content is actually removed, not just hidden under black boxes.

## Understanding the Results

### Test 1: Simple Text Redaction

**Before:**
- Contains "CONFIDENTIAL" text
- Contains "This is public information"

**After:**
- "CONFIDENTIAL" is REMOVED (not searchable, not in PDF structure)
- "This is public information" is PRESERVED
- Black box covers where "CONFIDENTIAL" was

**Verification:**
- Text extraction shows "CONFIDENTIAL" is gone
- Searching the PDF finds no "CONFIDENTIAL" text

### Test 2: Complex Document

**Before:**
- "SECRET-DATA" (should be removed)
- "PUBLIC-INFO" (should be preserved)
- "CONFIDENTIAL-123" (should be removed)
- Blue and green rectangles (blue should be removed)

**After:**
- "SECRET-DATA" → REMOVED ✓
- "CONFIDENTIAL-123" → REMOVED ✓
- "PUBLIC-INFO" → PRESERVED ✓
- Blue rectangle → REMOVED ✓
- Green rectangle → PRESERVED ✓

**Verification:**
- Demonstrates selective redaction
- Shows we don't remove unintended content

### Test 3: Random Redaction

**Before:**
- 35 words in a grid layout

**After:**
- 26 words remain (9 removed)
- Random black boxes cover removed areas

**Verification:**
- Shows redaction works at arbitrary positions
- Some content removed, some preserved (as expected)

## Running the Full Test Suite

For comprehensive automated testing:

```bash
cd PdfEditor.Tests
dotnet test
```

This runs 21 tests including:
- 5 original integration tests
- 7 comprehensive redaction tests
- 8 black box redaction tests (including DPI variations)

## What This Proves

✅ **Content is actually removed** from PDF structure (not just visually hidden)
✅ **Verification is independent** - uses separate library (PdfPig) to extract text
✅ **Selective removal works** - only targeted content is removed
✅ **Preservation works** - non-redacted content remains intact
✅ **Random positions work** - not limited to specific coordinates
✅ **Shapes and text** - both are properly redacted

## Troubleshooting

### "dotnet: command not found"

Install .NET 8.0 SDK:
- Windows: `winget install Microsoft.DotNet.SDK.8`
- Linux: `wget https://dot.net/v1/dotnet-install.sh && bash dotnet-install.sh --channel 8.0`
- macOS: `brew install dotnet@8`

### "Build failed"

```bash
cd PdfEditor.Demo
dotnet restore
dotnet build
```

### "All tests show FAILED"

Check that `RedactionService.cs` has content removal enabled:
```csharp
// Should be:
_logger.LogDebug("Step 1: Removing content within redaction area");
RemoveContentInArea(page, scaledArea);

// Not:
// RemoveContentInArea(page, scaledArea);  // commented out
```

## Next Steps

1. **Run the demo** to see it in action
2. **Open the generated PDFs** to visually inspect
3. **Try searching** for removed content (you won't find it)
4. **Run the full test suite** for comprehensive validation
5. **Review the code** in `PdfEditor.Demo/Program.cs` to understand the implementation

## Files to Review

- `PdfEditor.Demo/Program.cs` - Demonstration code
- `PdfEditor/Services/RedactionService.cs` - Redaction implementation
- `PdfEditor/Services/Redaction/ContentStreamParser.cs` - PDF parsing
- `PdfEditor.Tests/Integration/BlackBoxRedactionTests.cs` - Comprehensive tests
- `TEST_SUITE_GUIDE.md` - Complete test suite documentation
