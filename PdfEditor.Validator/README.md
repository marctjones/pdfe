# PDF Redaction Validator

Command-line tool to validate PDF redaction and detect content under black boxes.

## Purpose

This tool helps you verify that PDF redaction actually removes content from the PDF structure, rather than just covering it visually.

## Installation

```bash
cd PdfEditor.Validator
dotnet build
```

## Usage

### 1. Extract All Text (Most Important)

Extract all text from a PDF, including text that might be hidden under black boxes:

```bash
dotnet run extract-text redacted.pdf
```

**If sensitive text appears in the output, the redaction FAILED.**

### 2. Compare Before/After

Compare two PDFs to see what was removed:

```bash
dotnet run compare original.pdf redacted.pdf
```

Shows:
- Words that were removed
- Words that were preserved
- Statistics

### 3. Analyze PDF

Get detailed information about a PDF:

```bash
dotnet run analyze document.pdf
```

Shows:
- All text with positions
- Page count
- Content stream sizes

### 4. Find Hidden Content

Attempt to find overlapping text that might be hidden:

```bash
dotnet run find-hidden suspicious.pdf
```

**Note:** This only detects overlapping TEXT. It cannot detect text covered by shapes.

### 5. Show Content Stream (Advanced)

View raw PDF content stream:

```bash
dotnet run content-stream document.pdf
```

Useful for debugging redaction implementation.

## Validation Workflow

### Step 1: Generate Test PDFs

```bash
cd ../PdfEditor.Demo
dotnet run
```

This creates `RedactionDemo/` with before/after PDFs.

### Step 2: Verify Redaction

```bash
cd ../PdfEditor.Validator

# Extract text from redacted PDF
dotnet run extract-text ../PdfEditor.Demo/RedactionDemo/01_simple_redacted.pdf

# Search for sensitive term
dotnet run extract-text ../PdfEditor.Demo/RedactionDemo/01_simple_redacted.pdf | grep "CONFIDENTIAL"
```

**Expected:** No output (text was removed)
**If found:** Redaction failed (text still in PDF)

### Step 3: Compare PDFs

```bash
dotnet run compare \
  ../PdfEditor.Demo/RedactionDemo/01_simple_original.pdf \
  ../PdfEditor.Demo/RedactionDemo/01_simple_redacted.pdf
```

Should show:
- REMOVED: CONFIDENTIAL
- PRESERVED: public information

## Using Standard CLI Tools

You can also use standard PDF tools:

### pdftotext (Poppler)

```bash
# Install on Ubuntu/Debian
sudo apt-get install poppler-utils

# Extract text
pdftotext redacted.pdf - | grep "CONFIDENTIAL"

# If no output → redaction worked
# If found → redaction failed
```

### mutool (MuPDF)

```bash
# Install on Ubuntu/Debian
sudo apt-get install mupdf-tools

# Extract text
mutool draw -F text redacted.pdf | grep "CONFIDENTIAL"

# Show PDF structure
mutool show redacted.pdf x
```

### qpdf

```bash
# Install
sudo apt-get install qpdf

# Create human-readable PDF
qpdf --qdf redacted.pdf readable.pdf

# Inspect with text editor
less readable.pdf
# Search for sensitive terms
```

## Examples

### Example 1: Successful Redaction

```bash
$ dotnet run extract-text redacted.pdf
=== Extracting text from: redacted.pdf ===

=== Page 1 ===
This is public information

=== Summary ===
Total words: 4
```

✓ "CONFIDENTIAL" not found → Redaction succeeded

### Example 2: Failed Redaction

```bash
$ dotnet run extract-text redacted.pdf
=== Extracting text from: redacted.pdf ===

=== Page 1 ===
CONFIDENTIAL
This is public information

=== Summary ===
Total words: 5
```

✗ "CONFIDENTIAL" still present → Redaction failed

### Example 3: Compare Results

```bash
$ dotnet run compare original.pdf redacted.pdf
=== Comparing PDFs ===

=== Statistics ===
Words in BEFORE: 5
Words in AFTER:  4
Words REMOVED:   1
Words PRESERVED: 4

=== REMOVED Content ===
  - CONFIDENTIAL

=== Redaction Verification ===
✓ GOOD: Some content removed, some preserved (selective redaction)
```

## Modifying Tests to Preserve PDFs

If you want the test suite to preserve PDFs for manual inspection:

**Edit `PdfEditor.Tests/Integration/BlackBoxRedactionTests.cs`:**

```csharp
public void Dispose()
{
    // COMMENT OUT cleanup to preserve PDFs
    // foreach (var file in _tempFiles)
    // {
    //     TestPdfGenerator.CleanupTestFile(file);
    // }

    // Instead, print where files are saved
    Console.WriteLine("\nTest PDFs saved to:");
    foreach (var file in _tempFiles)
    {
        Console.WriteLine($"  {file}");
    }
}
```

Then run tests:

```bash
cd PdfEditor.Tests
dotnet test --logger "console;verbosity=detailed"
```

PDFs will be in `/tmp/PdfEditorTests/`

## Automation Script

Create a validation script:

```bash
#!/bin/bash
# validate-redaction.sh

ORIGINAL=$1
REDACTED=$2
SENSITIVE_TERM=$3

echo "=== Redaction Validation ==="
echo "Original: $ORIGINAL"
echo "Redacted: $REDACTED"
echo "Checking for: $SENSITIVE_TERM"
echo ""

# Extract text from redacted PDF
TEXT=$(dotnet run --project PdfEditor.Validator extract-text "$REDACTED" 2>/dev/null)

# Check if sensitive term exists
if echo "$TEXT" | grep -q "$SENSITIVE_TERM"; then
    echo "✗ FAILED: '$SENSITIVE_TERM' found in redacted PDF"
    echo "Redaction did not remove the content from PDF structure"
    exit 1
else
    echo "✓ SUCCESS: '$SENSITIVE_TERM' not found in redacted PDF"
    echo "Content was properly removed from PDF structure"
    exit 0
fi
```

Usage:

```bash
chmod +x validate-redaction.sh
./validate-redaction.sh original.pdf redacted.pdf "CONFIDENTIAL"
```

## CI/CD Integration

Add to your CI/CD pipeline:

```yaml
# .github/workflows/validate-redaction.yml
name: Validate Redaction

on: [push, pull_request]

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2

      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'

      - name: Run Demo
        run: |
          cd PdfEditor.Demo
          dotnet run

      - name: Validate Redaction
        run: |
          cd PdfEditor.Validator

          # Check that CONFIDENTIAL was removed
          TEXT=$(dotnet run extract-text ../PdfEditor.Demo/RedactionDemo/01_simple_redacted.pdf)

          if echo "$TEXT" | grep -q "CONFIDENTIAL"; then
            echo "FAILED: CONFIDENTIAL still in PDF"
            exit 1
          fi

          echo "SUCCESS: Redaction verified"
```

## Limitations

### What This Tool CAN Detect

✓ Text that exists in PDF structure (even if covered)
✓ Text differences between before/after PDFs
✓ Overlapping text elements
✓ Content stream changes

### What This Tool CANNOT Detect

✗ Text covered by shapes/graphics (need PDF rendering)
✗ Text covered by images
✗ Visual-only redaction (black rectangles)
✗ Metadata in PDF (use `exiftool` for that)

For shapes covering text, you need to:
1. Render the PDF to image
2. Compare pixel-by-pixel
3. Or use the actual test suite

## Summary

**Quick validation:**
```bash
# Most important check
dotnet run extract-text redacted.pdf | grep "SENSITIVE_TERM"

# No output = SUCCESS (content removed)
# Found = FAILED (content still there)
```

This validator tool answers your question: **Is content actually removed or just visually hidden?**
