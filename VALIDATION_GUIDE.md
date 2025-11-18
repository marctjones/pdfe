# PDF Redaction Validation Guide

This guide answers your questions about test PDF preservation, validation tools, and detecting content under black boxes.

---

## Q1: Do Tests Generate and Preserve PDFs for Manual Inspection?

### ✅ **Demo Program** - YES, Preserves All PDFs

```bash
./run-demo.sh  # or run-demo.bat on Windows
```

**Location:** `RedactionDemo/` directory in current folder

**Generated Files:** 12 PDFs (6 original, 6 redacted)
- `01_simple_original.pdf` / `01_simple_redacted.pdf`
- `02_complex_original.pdf` / `02_complex_redacted.pdf`
- `03_random_original.pdf` / `03_random_redacted.pdf`
- `04_text_only_original.pdf` / `04_text_only_redacted.pdf`
- `05_shapes_only_original.pdf` / `05_shapes_only_redacted.pdf`
- `06_layered_shapes_original.pdf` / `06_layered_shapes_redacted.pdf`

**Result:** ✓ All PDFs saved for manual inspection

---

### ❌ **Test Suite** - NO, Currently Cleans Up

```bash
cd PdfEditor.Tests
dotnet test
```

**Current Behavior:**
- Generates PDFs in `/tmp/PdfEditorTests/`
- Runs tests
- **Deletes PDFs** in `Dispose()` method

**To Preserve Test PDFs:**

Edit any test file (e.g., `BlackBoxRedactionTests.cs`):

```csharp
public void Dispose()
{
    // OPTION 1: Comment out cleanup
    /*
    foreach (var file in _tempFiles)
    {
        TestPdfGenerator.CleanupTestFile(file);
    }
    */

    // OPTION 2: Print file locations
    Console.WriteLine("\n=== Test PDFs Preserved ===");
    foreach (var file in _tempFiles)
    {
        Console.WriteLine($"  {file}");
    }
}
```

Then run with verbose output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

Files will be in `/tmp/PdfEditorTests/` for inspection.

---

## Q2: Standalone Tool to Find Content Under Black Boxes?

### ✅ **Created: PdfEditor.Validator** (Custom Tool)

I just created a custom CLI tool specifically for this purpose!

**Location:** `PdfEditor.Validator/`

**Key Features:**
1. **Extract all text** from PDF (even if covered by black boxes)
2. **Compare before/after** PDFs to see what was removed
3. **Detect hidden content** by finding overlapping elements
4. **Analyze PDF structure** in detail

**Usage:**

```bash
cd PdfEditor.Validator

# 1. Extract all text (MOST IMPORTANT)
dotnet run extract-text ../RedactionDemo/01_simple_redacted.pdf

# 2. Search for sensitive terms
dotnet run extract-text redacted.pdf | grep "CONFIDENTIAL"
# No output = SUCCESS (content removed)
# Found = FAILED (content still there)

# 3. Compare before/after
dotnet run compare original.pdf redacted.pdf

# 4. Analyze in detail
dotnet run analyze document.pdf

# 5. Find hidden content
dotnet run find-hidden suspicious.pdf
```

**How It Works:**

The tool uses **PdfPig library** to extract text from the PDF structure, completely independent of visual rendering. If text exists in the PDF (even under a black box), it will be found.

**Example - Successful Redaction:**

```bash
$ dotnet run extract-text redacted.pdf
=== Page 1 ===
This is public information

✓ "CONFIDENTIAL" NOT found → Redaction worked
```

**Example - Failed Redaction:**

```bash
$ dotnet run extract-text redacted.pdf
=== Page 1 ===
CONFIDENTIAL
This is public information

✗ "CONFIDENTIAL" FOUND → Redaction failed (text still in PDF!)
```

---

## Q3: Standard CLI Tools for Finding Content Under Shapes/Black Boxes

### ✅ **Yes - Multiple Standard Tools Available**

### 1. **pdftotext** (Poppler Utils)

**Install:**
```bash
# Ubuntu/Debian
sudo apt-get install poppler-utils

# macOS
brew install poppler

# Windows
# Download from: https://blog.alivate.com.au/poppler-windows/
```

**Usage:**
```bash
# Extract all text
pdftotext redacted.pdf output.txt
cat output.txt

# Or pipe to grep
pdftotext redacted.pdf - | grep "CONFIDENTIAL"

# If no output → text was removed
# If found → text still in PDF
```

**What it does:** Extracts ALL text from PDF structure, regardless of visual appearance.

---

### 2. **mutool** (MuPDF Tools)

**Install:**
```bash
# Ubuntu/Debian
sudo apt-get install mupdf-tools

# macOS
brew install mupdf-tools

# Windows
# Download from: https://mupdf.com/downloads/
```

**Usage:**
```bash
# Extract text
mutool draw -F text redacted.pdf > output.txt

# Show PDF structure
mutool show redacted.pdf

# Clean and decompress PDF
mutool clean -d redacted.pdf clean.pdf
```

**What it does:** Powerful PDF manipulation and analysis tool. Can extract text, show internal structure, and more.

---

### 3. **qpdf**

**Install:**
```bash
# Ubuntu/Debian
sudo apt-get install qpdf

# macOS
brew install qpdf

# Windows
# Download from: https://github.com/qpdf/qpdf/releases
```

**Usage:**
```bash
# Create human-readable PDF for inspection
qpdf --qdf redacted.pdf readable.pdf

# Inspect with text editor
less readable.pdf
# Search for "CONFIDENTIAL" manually

# Decompress streams
qpdf --decode-level=all redacted.pdf decoded.pdf
```

**What it does:** Creates "QDF" (QPDF Document Format) - a human-readable version of PDF where you can see all operators and text.

---

### 4. **pdftk** (PDF Toolkit)

**Install:**
```bash
# Ubuntu/Debian
sudo apt-get install pdftk

# macOS
brew install pdftk-java

# Windows
# Download from: https://www.pdflabs.com/tools/pdftk-the-pdf-toolkit/
```

**Usage:**
```bash
# Extract data
pdftk redacted.pdf dump_data output info.txt

# Uncompress PDF for inspection
pdftk redacted.pdf output uncompressed.pdf uncompress
```

---

### 5. **exiftool** (For Metadata)

**Install:**
```bash
# Ubuntu/Debian
sudo apt-get install libimage-exiftool-perl

# macOS
brew install exiftool
```

**Usage:**
```bash
# Check PDF metadata
exiftool redacted.pdf

# Remove metadata
exiftool -all= redacted.pdf
```

**Note:** Redaction should also remove metadata! Sensitive data can hide there too.

---

## Complete Validation Workflow

### Step 1: Generate Test PDFs

```bash
cd PdfEditor.Demo
dotnet run
```

Creates: `RedactionDemo/` with 12 PDFs

---

### Step 2: Manual Visual Inspection

```bash
# Open both versions
xdg-open RedactionDemo/04_text_only_original.pdf
xdg-open RedactionDemo/04_text_only_redacted.pdf

# Compare visually
# - Original: See all content
# - Redacted: See black boxes
```

---

### Step 3: Text Extraction Validation

**Using Custom Tool:**
```bash
cd PdfEditor.Validator

dotnet run extract-text ../RedactionDemo/04_text_only_redacted.pdf | grep "CONFIDENTIAL"
# No output = ✓ SUCCESS
```

**Using pdftotext:**
```bash
pdftotext RedactionDemo/04_text_only_redacted.pdf - | grep "CONFIDENTIAL"
# No output = ✓ SUCCESS
```

**Using mutool:**
```bash
mutool draw -F text RedactionDemo/04_text_only_redacted.pdf | grep "CONFIDENTIAL"
# No output = ✓ SUCCESS
```

---

### Step 4: Before/After Comparison

**Using Custom Tool:**
```bash
cd PdfEditor.Validator

dotnet run compare \
  ../RedactionDemo/04_text_only_original.pdf \
  ../RedactionDemo/04_text_only_redacted.pdf
```

**Expected Output:**
```
=== Statistics ===
Words REMOVED:   X
Words PRESERVED: Y

=== REMOVED Content ===
  - CONFIDENTIAL
  - confidential
  - data
  - Secret

=== Redaction Verification ===
✓ GOOD: Some content removed, some preserved (selective redaction)
```

---

### Step 5: Content Stream Analysis (Advanced)

**Using Custom Tool:**
```bash
dotnet run content-stream RedactionDemo/04_text_only_redacted.pdf
```

**Using qpdf:**
```bash
qpdf --qdf RedactionDemo/04_text_only_redacted.pdf readable.pdf
less readable.pdf
# Search for PDF operators like "Tj" (show text)
# Check if sensitive text appears
```

---

## Automated Validation Script

Create `validate-all.sh`:

```bash
#!/bin/bash
set -e

echo "=== PDF Redaction Validation Suite ==="
echo ""

# Test all redacted PDFs
FAILED=0

for REDACTED in RedactionDemo/*_redacted.pdf; do
    BASENAME=$(basename "$REDACTED" _redacted.pdf)
    echo "Checking: $BASENAME"

    # Extract text
    TEXT=$(pdftotext "$REDACTED" - 2>/dev/null)

    # Check for common sensitive terms
    for TERM in "CONFIDENTIAL" "SECRET" "confidential" "secret"; do
        if echo "$TEXT" | grep -q "$TERM"; then
            echo "  ✗ FAILED: Found '$TERM'"
            FAILED=$((FAILED + 1))
        fi
    done

    # Check for PUBLIC (should be preserved in some docs)
    if [[ $BASENAME == "04_text_only" ]]; then
        if echo "$TEXT" | grep -q "PUBLIC"; then
            echo "  ✓ PUBLIC content preserved"
        else
            echo "  ⚠ WARNING: PUBLIC content also removed"
        fi
    fi

    echo ""
done

if [ $FAILED -eq 0 ]; then
    echo "=== ALL VALIDATIONS PASSED ==="
    exit 0
else
    echo "=== $FAILED VALIDATIONS FAILED ==="
    exit 1
fi
```

Run it:
```bash
chmod +x validate-all.sh
./validate-all.sh
```

---

## What Each Tool Can/Cannot Detect

### ✓ **Can Detect:**

| Tool | Text Under Black Boxes | Text Under Shapes | Overlapping Text |
|------|----------------------|-------------------|------------------|
| **PdfEditor.Validator** | ✓ Yes | ✓ Yes | ✓ Yes |
| **pdftotext** | ✓ Yes | ✓ Yes | ✓ Yes |
| **mutool** | ✓ Yes | ✓ Yes | ✓ Yes |
| **qpdf** | ✓ Yes (manual) | ✓ Yes (manual) | ✓ Yes (manual) |

### ✗ **Cannot Detect:**

- Visually rendering what's "on top" (would need pixel comparison)
- Whether black boxes are "really" covering content (that's a visual question)
- Image-based content (scanned documents)
- Metadata/XMP data (use `exiftool` for that)

---

## Key Insight

**The question "Is content under a black box?" is ambiguous:**

1. **Visual Question:** "What do I see when I render this PDF?"
   - Answer: Render to image, look at pixels
   - Not what we care about for security

2. **Structural Question:** "Does the text/content exist in the PDF file?"
   - Answer: Use text extraction tools
   - **This is what matters for redaction!**

**Our tests validate #2** - that content is **actually removed from the PDF structure**, not just covered up.

---

## Summary

### Your Questions Answered:

**Q1: Do tests preserve PDFs for manual checking?**
- ✅ Demo program: YES (`RedactionDemo/` folder)
- ❌ Test suite: NO (but easily changed)

**Q2: Is there a standalone tool to find content under black boxes?**
- ✅ YES: Just created `PdfEditor.Validator`
- ✅ Also: `pdftotext`, `mutool`, `qpdf`, `pdftk`

**Q3: Command line tool to identify content under shapes/black boxes?**
- ✅ YES: Multiple options
  - **Custom:** `PdfEditor.Validator`
  - **Standard:** `pdftotext` (easiest)
  - **Advanced:** `mutool`, `qpdf`

### Quick Validation Command

```bash
# Single most important check:
pdftotext redacted.pdf - | grep "CONFIDENTIAL"

# No output = ✓ Content removed successfully
# Found = ✗ Redaction failed (text still in PDF)
```

### Files Created

- `PdfEditor.Validator/Program.cs` - Custom validation tool
- `PdfEditor.Validator/README.md` - Complete usage guide
- This guide: `VALIDATION_GUIDE.md`

All committed to branch `claude/add-test-suite-01NC1QMbq4qJjHAHbuf4gN15` ✅
