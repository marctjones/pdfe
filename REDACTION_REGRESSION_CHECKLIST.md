# Redaction Regression Test Checklist

**Run this checklist BEFORE and AFTER any changes to redaction code.**

## Quick Verification Commands

```bash
# 1. Build the project
cd PdfEditor
dotnet build

# 2. Run ALL redaction tests (must all pass)
cd ../PdfEditor.Tests
dotnet test --filter "FullyQualifiedName~Redaction"

# 3. Run with verbose output to verify glyph removal logs
dotnet test --filter "FullyQualifiedName~Redaction" --logger "console;verbosity=detailed"
```

---

## Manual Verification Checklist

### ✅ Before Making Changes

- [ ] All redaction tests pass
- [ ] Note current test count: _____ tests passing

### ✅ After Making Changes

- [ ] All redaction tests still pass
- [ ] Same number of tests: _____ tests passing
- [ ] No tests were removed or weakened

---

## Critical Test Cases (Must ALWAYS Pass)

### Test 1: Text Extraction Fails After Redaction
**File:** `BlackBoxRedactionTests.cs`
**Test:** `GeneratePDF_ApplyBlackBox_VerifyContentRemoval`

```csharp
// This assertion MUST pass
textAfter.Should().NotContain("CONFIDENTIAL",
    "Text must be REMOVED from PDF structure");
```

- [ ] Redacted text cannot be extracted via PdfPig
- [ ] Redacted text cannot be found with text search
- [ ] Content stream was modified (not just black box added)

### Test 2: Selective Removal Works
**File:** `ComprehensiveRedactionTests.cs`
**Test:** `RedactMappedContent_ShouldRemoveOnlyTargetedItems`

```csharp
// Targeted content removed
textAfter.Should().NotContain("CONFIDENTIAL");
textAfter.Should().NotContain("SECRET");

// Non-targeted content preserved
textAfter.Should().Contain("PUBLIC");
textAfter.Should().Contain("PRIVATE");
```

- [ ] Only text under redaction box is removed
- [ ] Text outside redaction box is preserved
- [ ] Word count decreases after redaction

### Test 3: Complex Document Handling
**File:** `ComprehensiveRedactionTests.cs`
**Test:** `RedactComplexDocument_ShouldRemoveSensitiveDataOnly`

- [ ] Sensitive data (SSN, account numbers) is removed
- [ ] Public data (company names) is preserved
- [ ] PDF remains structurally valid

### Test 4: Random Area Redaction
**File:** `ComprehensiveRedactionTests.cs`
**Test:** `RedactRandomAreas_ShouldOnlyRemoveIntersectingContent`

- [ ] Content in random areas is removed
- [ ] Content outside areas is preserved
- [ ] Multiple redaction areas work correctly

---

## Log Verification

When running tests with verbose output, verify these log entries appear:

### Expected Logs for Glyph Removal
```
[Info] Parsed content stream in XXms. Found XX operations
[Info] REMOVING Text: "CONFIDENTIAL" at (X,Y,W,H)
[Info] Content filtering complete. Removed: X, Kept: Y
[Info] Removed operations by type: TextOperation=X
[Debug] Rebuilding content stream with filtered operations
[Info] Successfully replaced page content stream
```

### WARNING: These Logs Indicate Problems
```
[Warning] Could not remove content in area
[Error] Failed to parse content stream
```

If you see warnings/errors, glyph removal may have failed.

---

## Code Verification Checklist

### Critical Code Must Be Present

- [ ] `RemoveContentInArea()` method exists and is called
- [ ] `_parser.ParseContentStream(page)` parses content stream
- [ ] Operations are filtered: `operation.IntersectsWith(area)`
- [ ] Content stream rebuilt: `_builder.BuildContentStream(filteredOperations)`
- [ ] Page content replaced: `ReplacePageContent(page, newContentBytes)`
- [ ] Black rectangle drawn: `DrawBlackRectangle(page, area)`

### Order Must Be Correct
```csharp
// 1. Parse
var operations = _parser.ParseContentStream(page);

// 2. Filter
foreach (var operation in operations)
{
    if (!operation.IntersectsWith(area))
        filteredOperations.Add(operation);
}

// 3. Rebuild
var newContentBytes = _builder.BuildContentStream(filteredOperations);

// 4. Replace
ReplacePageContent(page, newContentBytes);

// 5. Draw visual
DrawBlackRectangle(page, area);
```

---

## Independent Verification

### Using pdftotext (if available)
```bash
# Create test PDF with text
# Apply redaction
# Extract text
pdftotext redacted.pdf output.txt

# Verify redacted text is absent
grep "REDACTED_TEXT" output.txt
# Should return nothing
```

### Using Adobe Acrobat/PDF Reader
1. Open redacted PDF
2. Try to select text under black box
   - [ ] Nothing can be selected
3. Use Find (Ctrl+F) for redacted text
   - [ ] Text not found
4. Try copy-paste from redacted area
   - [ ] No text copied

---

## Regression Indicators

### 🚨 CRITICAL: Redaction is BROKEN if:

1. ❌ Text can be extracted after redaction
2. ❌ Find (Ctrl+F) locates redacted text
3. ❌ Copy-paste retrieves redacted text
4. ❌ Content stream is unchanged after redaction
5. ❌ `RemoveContentInArea()` is not called
6. ❌ Tests pass without actually removing content

### ✅ Redaction is WORKING if:

1. ✅ Text extraction returns empty/missing for redacted areas
2. ✅ Find (Ctrl+F) cannot locate redacted text
3. ✅ Copy-paste from redacted area yields nothing
4. ✅ Content stream is modified (different bytes)
5. ✅ Logs show "REMOVING Text: ..." entries
6. ✅ All tests pass with assertions on text absence

---

## Quick Reference: Test Commands

```bash
# Run specific test
dotnet test --filter "GeneratePDF_ApplyBlackBox_VerifyContentRemoval"

# Run all black box tests
dotnet test --filter "BlackBoxRedactionTests"

# Run all comprehensive tests
dotnet test --filter "ComprehensiveRedactionTests"

# Run all redaction tests with output
dotnet test --filter "Redaction" --logger "console;verbosity=detailed"
```

---

## Summary

**Before committing ANY changes to redaction code:**

1. [ ] Run all redaction tests
2. [ ] Verify glyph removal logs appear
3. [ ] Confirm text extraction fails for redacted content
4. [ ] Confirm non-redacted content is preserved
5. [ ] Verify no tests were removed or weakened

**If any check fails, DO NOT commit the changes.**
