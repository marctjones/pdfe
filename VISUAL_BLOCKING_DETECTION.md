# Detecting Visually Blocked Content in PDFs

This guide explains how to detect content that is **visually blocked** (covered up) by black boxes or other shapes when you render the PDF.

## The Key Distinction

### ❌ What Previous Tools Do
**Extract text from PDF structure** - finds text whether visible or not
- `pdftotext` - extracts all text
- `mutool` - extracts all content
- Our `extract-text` command

### ✅ What You Actually Want
**Detect content that is visually covered** when you view the PDF
- Check z-order (drawing order)
- Identify black rectangles
- Find what's underneath them
- Requires analyzing PDF rendering, not just structure

## Understanding PDF Z-Order

PDFs don't have explicit "layers" - objects are drawn in sequence:

```
1. Draw text "CONFIDENTIAL" at (100, 100)    ← Drawn first (underneath)
2. Set color to black (0 0 0)
3. Draw rectangle at (90, 90, 200x50)        ← Drawn last (on top)
```

**Result:** Text is visually blocked but still in PDF structure.

## Method 1: Before/After Comparison (Recommended)

The most reliable way to detect visually blocked content:

### Step 1: Generate Test PDFs

```bash
cd PdfEditor.Demo
dotnet run
```

Creates `RedactionDemo/` with before/after pairs.

### Step 2: Extract Text from Both

```bash
cd ../PdfEditor.Validator

# Before redaction
dotnet run extract-text ../PdfEditor.Demo/RedactionDemo/04_text_only_original.pdf > before.txt

# After redaction
dotnet run extract-text ../PdfEditor.Demo/RedactionDemo/04_text_only_redacted.pdf > after.txt

# Compare
diff before.txt after.txt
```

### Step 3: Analyze Differences

**Content in `before.txt` but NOT in `after.txt` = Content that was under black boxes**

Example:
```bash
$ dotnet run compare \
    RedactionDemo/04_text_only_original.pdf \
    RedactionDemo/04_text_only_redacted.pdf

=== REMOVED Content ===
  - CONFIDENTIAL        ← This WAS under a black box
  - confidential
  - data
  - Secret

=== PRESERVED Content ===
  - PUBLIC             ← This was NOT under a black box
  - information
```

## Method 2: Visual Blocking Detector (New Tool)

### Usage

```bash
cd PdfEditor.Validator

dotnet run detect-blocking document.pdf
```

### What It Does

1. **Parses PDF content stream** to find drawing order
2. **Identifies black rectangles** (0 0 0 color + rectangle operators)
3. **Finds text/shapes drawn BEFORE** black rectangles
4. **Reports potentially blocked content**

### Example Output

```
=== Analyzing Visual Blocking ===

Method 1: Content Stream Analysis
Found 2 black rectangle(s):
  Black box at drawing order 45
  Black box at drawing order 67

⚠ Found 3 potentially blocked item(s):
  Text: (CONFIDENTIAL) BT /F1 12 Tf 100 100 Td (CONFIDENTIAL) Tj ET
    Drawing order: 12 (drawn before black box at 45)

  Text: (Secret data) BT /F1 12 Tf 100 430 Td (Secret data) Tj ET
    Drawing order: 25 (drawn before black box at 67)

✓ PUBLIC text at drawing order 78 NOT blocked (drawn after black boxes)
```

### Limitations

⚠️ **This method has limitations:**
- Simplified parsing (doesn't handle all PDF operators)
- Doesn't account for complex transformations
- May miss some edge cases
- Best used with simple PDFs

## Method 3: Render to Image & Analyze Pixels

For **pixel-perfect** visual blocking detection, render the PDF and analyze the image.

### Using ImageMagick

```bash
# Install
sudo apt-get install imagemagick ghostscript

# Render PDF to PNG
convert -density 150 redacted.pdf redacted.png

# Find black regions
convert redacted.png -fill white +opaque black black_regions.png

# Compare with original
convert original.pdf original.png
compare original.png redacted.png diff.png
```

### Using Python + PIL/Pillow

```python
from pdf2image import convert_from_path
import numpy as np

# Render PDFs
before_images = convert_from_path('original.pdf', dpi=150)
after_images = convert_from_path('redacted.pdf', dpi=150)

# Convert to numpy arrays
before_np = np.array(before_images[0])
after_np = np.array(after_images[0])

# Find black pixels in 'after' image
black_mask = (after_np[:,:,0] < 10) & (after_np[:,:,1] < 10) & (after_np[:,:,2] < 10)

# Check what was there before
blocked_content = before_np[black_mask]

# Count how many pixels changed to black
num_blocked_pixels = np.sum(black_mask)
print(f"Black pixels: {num_blocked_pixels}")
```

### Using PDFtoImage (C# - Already in Project)

```csharp
using PDFtoImage;

// Render both PDFs
var beforeImage = PDFtoImage.Conversion.ToImage("original.pdf");
var afterImage = PDFtoImage.Conversion.ToImage("redacted.pdf");

// Compare pixel by pixel
// Find regions that are black in 'after' but not in 'before'
// Those regions are visually blocked
```

## Method 4: OCR Comparison

Use OCR to see what's **visually readable** vs what's in the structure.

### Using Tesseract OCR

```bash
# Install
sudo apt-get install tesseract-ocr

# Render PDF to image
convert -density 300 redacted.pdf redacted.png

# OCR the image
tesseract redacted.png ocr_output

# Compare OCR output with text extraction
pdftotext redacted.pdf pdf_text.txt

diff ocr_output.txt pdf_text.txt
```

**Interpretation:**
- Text in `pdf_text.txt` but NOT in `ocr_output.txt` = **Visually blocked**
- OCR can't see it, but it's still in the PDF structure

## Practical Examples

### Example 1: Validate Redaction Worked

**Question:** Did black boxes actually cover the content?

```bash
# Method 1: Quick check (recommended)
dotnet run compare original.pdf redacted.pdf

# Look for:
=== REMOVED Content ===
  - CONFIDENTIAL  ✓ (was under black box, now removed from PDF)
```

**Result:** ✓ Content removed from structure → Redaction worked correctly

---

### Example 2: Detect Failed Redaction

**Question:** Is text still visible under black boxes?

```bash
# Extract text after "redaction"
dotnet run extract-text badly_redacted.pdf

# Output:
CONFIDENTIAL
This is public
```

**Then check visually:**
```bash
xdg-open badly_redacted.pdf
# See black box, but text still extractable
```

**Result:** ✗ Text still in PDF structure → **Visual-only redaction (FAILED)**

---

### Example 3: Find What's Under Black Boxes

**Question:** What content is visually blocked right now?

```bash
# Use visual blocking detector
dotnet run detect-blocking suspicious.pdf

# Output:
⚠ Found 3 potentially blocked items:
  Text: "CONFIDENTIAL" at drawing order 12
  Text: "Secret data" at drawing order 25
```

**Result:** Shows what content is underneath black boxes

---

## Which Method to Use?

| Scenario | Best Method | Why |
|----------|-------------|-----|
| **Validate redaction worked** | Before/After Comparison | Most reliable |
| **Quick check for hidden text** | extract-text + visual inspection | Fast and simple |
| **Analyze specific PDF** | detect-blocking | Shows z-order |
| **Pixel-perfect analysis** | Render to image | Most accurate |
| **Automated testing** | Before/After in test suite | Repeatable |

## Complete Validation Workflow

### Step 1: Generate Test Case

```bash
# Create original PDF with sensitive data
# (or use existing document)

# Apply redaction (should remove content AND add black boxes)
./run-demo.sh
```

### Step 2: Visual Inspection

```bash
# Open both PDFs side by side
xdg-open RedactionDemo/04_text_only_original.pdf &
xdg-open RedactionDemo/04_text_only_redacted.pdf &

# Verify visually:
# ✓ Black boxes visible in redacted version
# ✓ Sensitive content NOT visible
```

### Step 3: Structure Validation

```bash
cd PdfEditor.Validator

# Extract text from redacted PDF
dotnet run extract-text RedactionDemo/04_text_only_redacted.pdf | grep "CONFIDENTIAL"

# Expected: No output
# If found: FAILED - content still in PDF
```

### Step 4: Blocking Detection

```bash
# Check what's under black boxes (if any)
dotnet run detect-blocking RedactionDemo/04_text_only_redacted.pdf

# Expected: No blocked content found
# If found: FAILED - content under black boxes wasn't removed
```

### Step 5: Pixel Comparison (Optional)

```bash
# For pixel-perfect validation
convert -density 150 RedactionDemo/04_text_only_original.pdf before.png
convert -density 150 RedactionDemo/04_text_only_redacted.pdf after.png

# Visual diff
compare before.png after.png diff.png
xdg-open diff.png

# Red regions show changes (black boxes added)
```

## Common Issues & Solutions

### Issue 1: "Text extracted but not visible"

**Symptom:** `pdftotext` finds text, but you can't see it in PDF viewer

**Possible causes:**
1. Text covered by black box (visual-only redaction) ✗
2. Text color set to white/background
3. Text outside page boundaries

**Solution:** Our redaction removes content from structure, not just covers it ✓

---

### Issue 2: "Visual blocking detector finds nothing"

**Symptom:** `detect-blocking` reports no blocked content

**Possible causes:**
1. Redaction worked correctly (content removed) ✓
2. Parser doesn't recognize black boxes (complex PDF)
3. Black boxes drawn before content (wrong z-order)

**Solution:** Use before/after comparison to confirm

---

### Issue 3: "Different tools give different results"

**Symptom:** `pdftotext` finds text, but OCR doesn't

**Explanation:** This is **expected** with visual-only redaction
- Text extraction: Reads PDF structure
- OCR: Reads rendered image

**Correct behavior:** Neither should find redacted content ✓

---

## Summary

### To Detect Visually Blocked Content:

**Method 1 (Best):** Compare before/after PDFs
```bash
dotnet run compare original.pdf redacted.pdf
```

**Method 2:** Use blocking detector
```bash
dotnet run detect-blocking document.pdf
```

**Method 3:** Render and compare images
```bash
convert original.pdf before.png
convert redacted.pdf after.png
compare before.png after.png diff.png
```

**Method 4:** OCR vs text extraction
```bash
tesseract redacted.png ocr.txt
pdftotext redacted.pdf text.txt
diff ocr.txt text.txt  # Differences = visually hidden
```

### Key Insight

**What you really want to know:**
> "After redaction, is sensitive content still in the PDF file?"

**Answer:**
```bash
pdftotext redacted.pdf - | grep "CONFIDENTIAL"

# No output = ✓ Content removed (redaction succeeded)
# Found = ✗ Content still there (redaction failed)
```

If content is removed from the PDF structure, it's **not** visually blocked - it's **gone** ✓

Our tests validate that redaction **removes** content, not just blocks it visually.
