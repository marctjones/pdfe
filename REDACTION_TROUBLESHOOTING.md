# Redaction Troubleshooting Guide

This guide explains how the glyph-level redaction works, how to diagnose failures, and how to fix common issues.

## How Redaction Should Work

### The Correct Flow

```
1. PARSE:    ContentStreamParser.ParseContentStream()
             → Returns List<PdfOperation> with bounding boxes

2. FILTER:   Remove operations intersecting redaction area
             → foreach op: if op.IntersectsWith(area) skip it

3. REBUILD:  ContentStreamBuilder.BuildContentStream()
             → Serialize remaining operations to PDF syntax

4. REPLACE:  ReplacePageContent()
             → Clear old content, write new filtered content

5. DRAW:     DrawBlackRectangle()
             → Visual confirmation (secondary)
```

### What This Achieves

- **Text glyphs are REMOVED** from PDF content stream
- Text extraction tools return **empty/missing** for redacted areas
- Black box provides **visual confirmation** only

---

## Quick Diagnostic

### Run the Diagnostic Test

```bash
cd PdfEditor.Tests
dotnet test --filter "Diagnostic_DetailedRedactionAnalysis" --logger "console;verbosity=detailed"
```

This test outputs detailed information about:
- Content before/after redaction
- Content stream sizes
- Words removed/preserved
- Overall verdict

### What to Look For

**✅ Working correctly:**
```
Glyph removed from text extraction: ✓ YES
Content stream modified: ✓ YES
PDF remains valid: ✓ YES
```

**❌ Broken:**
```
Glyph removed from text extraction: ✗ NO  ← Text still extractable
Content stream modified: ✗ NO             ← Only black box added
```

---

## Common Failures and Fixes

### Failure 1: Text Still Extractable After Redaction

**Symptoms:**
- Test fails: "Text must be REMOVED from PDF structure"
- `pdftotext` still shows redacted text
- Copy-paste retrieves text from under black box

**Cause:** Content stream not being rebuilt/replaced

**How to Debug:**
```csharp
// In RedactionService.RemoveContentInArea()
_logger.LogInformation("Parsed {Count} operations", operations.Count);
_logger.LogInformation("Filtered to {Count} operations", filteredOperations.Count);
_logger.LogInformation("Rebuilding content stream...");
```

Check logs for:
- Operations being parsed
- Operations being filtered out
- Content stream being rebuilt

**Fix:** Ensure the full flow executes:
```csharp
// 1. Parse - must happen
var operations = _parser.ParseContentStream(page);

// 2. Filter - must remove intersecting ops
var filtered = operations.Where(op => !op.IntersectsWith(area)).ToList();

// 3. Rebuild - must happen
var newContent = _builder.BuildContentStream(filtered);

// 4. Replace - must happen
ReplacePageContent(page, newContent);
```

### Failure 2: Content Stream Unchanged

**Symptoms:**
- Content stream size identical before/after
- Test fails: "Content stream must be modified"

**Cause:** ReplacePageContent not called or failing silently

**How to Debug:**
```csharp
// Add logging in ReplacePageContent()
_logger.LogDebug("Clearing {Count} content elements", page.Contents.Elements.Count);
page.Contents.Elements.Clear();
_logger.LogDebug("Creating new content stream with {Bytes} bytes", newContent.Length);
```

**Fix:** Verify ReplacePageContent executes:
```csharp
private void ReplacePageContent(PdfPage page, byte[] newContent)
{
    page.Contents.Elements.Clear();  // Must clear old content
    var stream = page.Contents.CreateSingleContent();
    stream.CreateStream(newContent); // Must create new stream
}
```

### Failure 3: No Operations Removed

**Symptoms:**
- Logs show "Removed: 0, Kept: N"
- Text not being filtered

**Cause:** Intersection detection failing

**How to Debug:**
```csharp
// In RemoveContentInArea filtering loop
foreach (var operation in operations)
{
    if (operation is TextOperation textOp)
    {
        _logger.LogDebug("Text '{Text}' at {Box}, intersects: {Intersects}",
            textOp.Text, textOp.BoundingBox, operation.IntersectsWith(area));
    }
}
```

**Possible Causes:**
1. Bounding box calculation wrong
2. Coordinate system mismatch (PDF vs Avalonia)
3. DPI scaling incorrect

**Fix - Check Coordinates:**
```csharp
// Redaction area should be in same coordinate system as text bounds
// Both should use Avalonia coordinates (top-left origin)
_logger.LogInformation("Redaction area: {Area}", area);
_logger.LogInformation("Text bounds: {Bounds}", textOp.BoundingBox);
```

### Failure 4: Parser Returns Empty Operations

**Symptoms:**
- Logs show "Parsed 0 operations"
- No text operations found

**Cause:** Content stream parsing failing

**How to Debug:**
```csharp
// In ContentStreamParser
_logger.LogDebug("Raw content stream length: {Length} bytes", contentBytes.Length);
_logger.LogDebug("First 100 chars: {Content}",
    Encoding.UTF8.GetString(contentBytes).Substring(0, Math.Min(100, contentBytes.Length)));
```

**Possible Causes:**
1. Content stream is compressed (check for FlateDecode)
2. Content stream format not recognized
3. Parser exception swallowed

**Fix:** Check for parsing errors:
```csharp
try
{
    var operations = _parser.ParseContentStream(page);
    if (operations.Count == 0)
    {
        _logger.LogWarning("No operations parsed - check content stream format");
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "Parser failed");
    throw; // Don't swallow
}
```

### Failure 5: PDF Corrupted After Redaction

**Symptoms:**
- PDF won't open
- Test fails: "PDF must remain valid"

**Cause:** Content stream malformed after rebuild

**How to Debug:**
```csharp
// Check rebuilt content stream
var newContent = _builder.BuildContentStream(filteredOperations);
_logger.LogDebug("Rebuilt content: {Content}",
    Encoding.UTF8.GetString(newContent).Substring(0, Math.Min(500, newContent.Length)));
```

**Possible Causes:**
1. ContentStreamBuilder not serializing correctly
2. State stack imbalance (mismatched q/Q)
3. Invalid PDF operator syntax

**Fix:** Verify builder output is valid PDF syntax

---

## Key Code Locations

### Where to Add Logging

**RedactionService.cs (main orchestration):**
```csharp
// Line ~70: After scaling
_logger.LogDebug("Scaled redaction area: {Area}", scaledArea);

// Line ~135: In RemoveContentInArea
_logger.LogInformation("Parsing content stream...");
_logger.LogInformation("Filtering {Total} operations...", operations.Count);
_logger.LogInformation("Removed {Removed}, kept {Kept}", removedCount, filteredOperations.Count);
```

**ContentStreamParser.cs (parsing):**
```csharp
// After parsing each operator
_logger.LogDebug("Parsed {Op} -> {Type}", operatorName, operation.GetType().Name);

// After calculating bounds
_logger.LogDebug("Text '{Text}' bounds: {Bounds}", text, bounds);
```

**ContentStreamBuilder.cs (rebuilding):**
```csharp
// When serializing each operation
_logger.LogDebug("Serializing {Type}: {Content}", op.GetType().Name, serialized);
```

### Critical Methods to Check

| File | Method | Purpose |
|------|--------|---------|
| `RedactionService.cs` | `RemoveContentInArea()` | Orchestrates glyph removal |
| `ContentStreamParser.cs` | `ParseContentStream()` | Parses PDF operators to objects |
| `ContentStreamBuilder.cs` | `BuildContentStream()` | Serializes objects to PDF syntax |
| `TextBoundsCalculator.cs` | `CalculateBounds()` | Calculates text bounding boxes |
| `PdfOperation.cs` | `IntersectsWith()` | Determines if operation should be removed |

---

## Verification Steps

### After Any Fix

1. **Run all glyph removal tests:**
   ```bash
   dotnet test --filter "GlyphRemoval"
   ```

2. **Run diagnostic test:**
   ```bash
   dotnet test --filter "Diagnostic_DetailedRedactionAnalysis" --logger "console;verbosity=detailed"
   ```

3. **Manual verification:**
   ```bash
   # Create test PDF, apply redaction, then:
   pdftotext redacted.pdf output.txt
   grep "REDACTED_TEXT" output.txt  # Should return nothing
   ```

4. **Check the three criteria:**
   - [ ] Text extraction returns empty for redacted text
   - [ ] Content stream bytes are different
   - [ ] PDF opens and is valid

---

## Test Commands Reference

```bash
# Run all redaction tests
dotnet test --filter "FullyQualifiedName~Redaction"

# Run glyph removal tests only
dotnet test --filter "GlyphRemoval"

# Run with detailed logging
dotnet test --filter "GlyphRemoval" --logger "console;verbosity=detailed"

# Run diagnostic test
dotnet test --filter "Diagnostic_DetailedRedactionAnalysis"

# Run specific critical test
dotnet test --filter "GlyphRemoval_TextMustBeAbsentAfterRedaction"
```

---

## Understanding the Code Flow

### Step-by-Step Execution

1. **User draws redaction box** → ViewModel calls `RedactionService.RedactArea()`

2. **Scale coordinates** (lines 48-63 in RedactionService.cs)
   - Convert from render DPI to PDF points (72 DPI)
   - Keep in Avalonia coordinates (top-left origin)

3. **Remove content** (lines 127-244 in RedactionService.cs)
   - `_parser.ParseContentStream()` → Get all PDF operations
   - Filter: `operation.IntersectsWith(area)` → Skip if true
   - `_builder.BuildContentStream()` → Rebuild without removed ops
   - `ReplacePageContent()` → Write new content to page

4. **Draw visual box** (lines 109-121 in RedactionService.cs)
   - Uses XGraphics to draw black rectangle
   - This is just visual confirmation

### Key Data Structures

**PdfOperation** (base class):
```csharp
public Rect BoundingBox { get; }
public virtual bool IntersectsWith(Rect area) => BoundingBox.IntersectsWith(area);
```

**TextOperation** (text glyphs):
```csharp
public string Text { get; }
public string FontName { get; }
public double FontSize { get; }
public Rect BoundingBox { get; }  // Calculated by TextBoundsCalculator
```

### Coordinate System

**Important:** Both redaction area and text bounds must use same coordinate system.

```
PDF coordinates:      Avalonia coordinates:
+Y                    (0,0) +X
^                     +-------->
|                     |
|                     | +Y
+-----> +X            v
(0,0)
```

TextBoundsCalculator converts PDF to Avalonia:
```csharp
var avaloniaY = pageHeight - pdfY - height;
```

---

## When to Escalate

If you've tried the fixes above and tests still fail:

1. **Check parser comprehensiveness** - Does it handle all PDF operators in the document?
2. **Check coordinate transformations** - Are text matrices being applied correctly?
3. **Check font metrics** - Are bounding boxes accurate for the fonts used?

These are complex areas that may require deeper investigation into:
- `ContentStreamParser.cs` (500+ lines)
- `TextBoundsCalculator.cs` (150+ lines)
- PDF specification for operator handling

---

## Summary

**The three things that MUST happen for glyph removal:**

1. ✅ Content stream is PARSED into operations
2. ✅ Operations are FILTERED (removed if intersecting)
3. ✅ Content stream is REBUILT and REPLACED

**If text is still extractable, one of these failed.**

Run the diagnostic test and check the logs to identify which step is failing.
