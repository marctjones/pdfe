# Manual Testing Checklist for v1.3.0 Release

This checklist covers critical workflows and features that must be manually verified before releasing v1.3.0.

**Version**: 1.3.0
**Release Focus**: Save workflow improvements, redaction safety, and original file protection

---

## Prerequisites

- [ ] Build passes: `dotnet build -c Release`
- [ ] All automated tests pass: `dotnet test`
- [ ] Build-time verification passes: `./scripts/verify-true-redaction.sh`

---

## 1. Original File Protection (Issue #43)

### Test 1.1: Original file never modified
**Steps**:
1. Open a PDF file (e.g., `test_document.pdf`)
2. Note the file's "Modified" timestamp in file explorer
3. Perform a redaction
4. Click "Save Redacted Version"
5. Save to a NEW filename (e.g., `test_document_REDACTED.pdf`)

**Expected Results**:
- [ ] Original file `test_document.pdf` timestamp is UNCHANGED
- [ ] New file `test_document_REDACTED.pdf` exists
- [ ] Both files exist on disk
- [ ] Opening original file shows unredacted content
- [ ] Opening redacted file shows redacted content

### Test 1.2: Window title shows file state
**Steps**:
1. Open a PDF file
2. Observe window title

**Expected Results**:
- [ ] Window title shows: `PdfEditor - {filename}`
- [ ] After redaction is applied, title shows: `PdfEditor - {filename}*` (asterisk indicates unsaved changes)

### Test 1.3: Sequential saves create new files
**Steps**:
1. Open `original.pdf`
2. Apply redaction, save as `version1_REDACTED.pdf`
3. Apply another redaction, save as `version2_REDACTED.pdf`
4. Apply another redaction, save as `version3_REDACTED.pdf`

**Expected Results**:
- [ ] `original.pdf` remains unchanged
- [ ] Three redacted files exist: `version1_REDACTED.pdf`, `version2_REDACTED.pdf`, `version3_REDACTED.pdf`
- [ ] Each file has progressively more redactions applied

---

## 2. Context-Aware Save Button (Issues #40, #41)

### Test 2.1: Save button text changes based on file state
**Steps**:
1. Open an original PDF
2. Observe Save button text
3. Apply a redaction
4. Observe Save button text
5. Save the redacted version
6. Observe Save button text

**Expected Results**:
- [ ] Before redaction: Save button shows "Save Redacted Version" (or is disabled if no redactions)
- [ ] After applying redaction: Save button shows "Save Redacted Version"
- [ ] After saving: If viewing redacted file, button shows "Save"

### Test 2.2: Save button suggests filename
**Steps**:
1. Open `document.pdf`
2. Apply redaction
3. Click "Save Redacted Version"
4. Observe suggested filename in save dialog

**Expected Results**:
- [ ] Suggested filename is `document_REDACTED.pdf`
- [ ] `_REDACTED` suffix is automatically added
- [ ] User can override the suggested filename

### Test 2.3: Subsequent saves on redacted file
**Steps**:
1. Open `document.pdf`
2. Apply redaction, save as `document_REDACTED.pdf`
3. Close and reopen `document_REDACTED.pdf`
4. Apply another redaction
5. Click Save button

**Expected Results**:
- [ ] Save button shows "Save" (not "Save Redacted Version")
- [ ] Suggested filename is `document_REDACTED_v2.pdf` or similar (NOT overwriting existing file)

---

## 3. Redaction Safety (Issues #48, #49, #50)

### Test 3.1: TRUE redaction logging (Issue #48)
**Steps**:
1. Run application from terminal: `dotnet run`
2. Open a PDF with text
3. Use text search to find a word
4. Click "Redact Results" button
5. Observe console output

**Expected Results**:
- [ ] Console shows: `[REDACTION-SECURITY] TRUE REDACTION: Removed X text operations, Y path operations, Z image operations`
- [ ] If no content found: `[REDACTION-ERROR] FAILED: No content found in redaction area`
- [ ] Logging CANNOT be silenced (uses `Console.WriteLine`, not just logger)

### Test 3.2: Runtime verification (Issue #50)
**Steps**:
1. Run application in DEBUG mode
2. Apply redaction and save
3. Observe console output after save

**Expected Results**:
- [ ] Console shows: `[REDACTION-VERIFICATION] ✓ PASSED - No text leaks detected`
- [ ] If text leaks detected: `[REDACTION-VERIFICATION] ✗ FAILED - N text leak(s) detected!`
- [ ] Leak details show page number, text, and coordinates

### Test 3.3: Visual-only fallback is DISABLED
**Steps**:
1. Open a PDF
2. Draw a redaction box in an area with NO text or graphics (empty white space)
3. Try to apply redaction

**Expected Results**:
- [ ] Application shows error: "No content found in redaction area"
- [ ] NO black rectangle is drawn
- [ ] Console shows: `[REDACTION-ERROR] FAILED: No content found in redaction area`
- [ ] Application does NOT silently draw a black box

### Test 3.4: Build-time verification
**Steps**:
1. Run: `./scripts/verify-true-redaction.sh`

**Expected Results**:
- [ ] All 8 checks PASS:
  - ✓ RedactionResult model exists
  - ✓ RedactionMode enum exists
  - ✓ MANDATORY logging exists
  - ✓ RemoveContentInArea signature correct
  - ✓ ContentStreamParser usage present
  - ✓ ContentStreamBuilder usage present
  - ✓ No suspicious simplification comments
  - ✓ ReplacePageContent usage present
- [ ] Script exits with code 0 (success)

---

## 4. Text Extraction Verification (Issue #42)

### Test 4.1: Redacted text is NOT extractable
**Steps**:
1. Open a PDF with text
2. Search for a word (e.g., "CONFIDENTIAL")
3. Click "Redact Results"
4. Save redacted version
5. Use external tool to extract text: `pdftotext redacted.pdf -`

**Expected Results**:
- [ ] `pdftotext` output does NOT contain "CONFIDENTIAL"
- [ ] Black rectangle is visible in the PDF at the redacted location
- [ ] Text is REMOVED from PDF structure, not just hidden

### Test 4.2: Non-redacted text is still extractable
**Steps**:
1. Open a PDF with multiple words
2. Redact only ONE word
3. Save redacted version
4. Use `pdftotext` to extract text

**Expected Results**:
- [ ] Non-redacted words are still extractable
- [ ] Only the redacted word is missing from extracted text
- [ ] PDF structure remains valid

---

## 5. Visual Distinction (Issue #38)

### Test 5.1: Pending vs applied redactions
**Steps**:
1. Open a PDF
2. Draw a redaction box (don't apply yet)
3. Observe the box color/style
4. Click "Apply Redaction"
5. Observe the box color/style

**Expected Results**:
- [ ] Pending redaction: Box is outlined/translucent (can see text behind)
- [ ] Applied redaction: Box is solid black (text obscured)
- [ ] Clear visual difference between pending and applied states

---

## 6. Status Bar Updates (Issue #44)

### Test 6.1: Status bar shows operations
**Steps**:
1. Open a PDF
2. Observe status bar messages during these operations:
   - Opening file
   - Applying redaction
   - Saving file
   - Searching text

**Expected Results**:
- [ ] Status bar shows: "Opening {filename}..."
- [ ] Status bar shows: "Applying redaction..."
- [ ] Status bar shows: "Saving to {filename}..."
- [ ] Status bar shows: "Searching for '{query}'..."
- [ ] Status bar returns to ready state after each operation

---

## 7. Regression Testing (Existing Features)

### Test 7.1: Basic PDF operations
**Steps**:
1. Open a multi-page PDF
2. Navigate between pages
3. Zoom in/out
4. Pan the view

**Expected Results**:
- [ ] All pages render correctly
- [ ] Zoom works smoothly (100%, 150%, 200%, etc.)
- [ ] Pan works with mouse drag
- [ ] Page thumbnails update correctly

### Test 7.2: Text search and redaction
**Steps**:
1. Open a PDF with searchable text
2. Search for a word that appears multiple times
3. Observe search results count
4. Click "Redact Results"
5. Verify all instances are redacted

**Expected Results**:
- [ ] Search finds all instances
- [ ] Results count is accurate
- [ ] All instances highlighted in yellow
- [ ] "Redact Results" button appears
- [ ] All instances are redacted when button is clicked

### Test 7.3: Page manipulation
**Steps**:
1. Open a multi-page PDF
2. Add a blank page
3. Remove a page
4. Rotate a page
5. Save the document

**Expected Results**:
- [ ] Blank page is added correctly
- [ ] Page removal works
- [ ] Page rotation persists after save
- [ ] Page thumbnails update to reflect changes

### Test 7.4: Clipboard history
**Steps**:
1. Open a PDF
2. Redact several pieces of text
3. Observe clipboard history panel

**Expected Results**:
- [ ] Clipboard panel shows all redacted text
- [ ] Each entry shows the redacted text content
- [ ] Entries are timestamped or ordered

---

## 8. Cross-Platform Testing

### Test 8.1: Linux
**Steps**:
1. Build and run on Linux: `dotnet run`
2. Perform basic operations (open, redact, save)

**Expected Results**:
- [ ] Application launches without errors
- [ ] All features work as expected
- [ ] File dialogs work correctly

### Test 8.2: Windows
**Steps**:
1. Build and run on Windows: `dotnet run`
2. Perform basic operations

**Expected Results**:
- [ ] Application launches without errors
- [ ] All features work as expected
- [ ] File dialogs use native Windows UI

### Test 8.3: macOS (if available)
**Steps**:
1. Build and run on macOS: `dotnet run`
2. Perform basic operations

**Expected Results**:
- [ ] Application launches without errors
- [ ] All features work as expected
- [ ] File dialogs use native macOS UI

---

## 9. Error Handling

### Test 9.1: Invalid PDF
**Steps**:
1. Try to open a corrupted or non-PDF file
2. Rename a .txt file to .pdf and try to open it

**Expected Results**:
- [ ] Application shows error message
- [ ] Application does NOT crash
- [ ] Error is logged to console

### Test 9.2: Read-only file
**Steps**:
1. Open a PDF
2. Apply redaction
3. Make the original file read-only in file system
4. Try to save

**Expected Results**:
- [ ] Save dialog appears (suggesting new filename)
- [ ] Can save to a different filename
- [ ] Error message if trying to overwrite read-only file

### Test 9.3: Disk full (optional)
**Steps**:
1. Create a scenario with limited disk space
2. Try to save a large redacted PDF

**Expected Results**:
- [ ] Application shows error message
- [ ] Application does NOT crash
- [ ] Original file remains unchanged

---

## 10. Performance

### Test 10.1: Large PDF
**Steps**:
1. Open a PDF with 100+ pages
2. Apply redactions to several pages
3. Save the document

**Expected Results**:
- [ ] Application remains responsive
- [ ] Redaction completes in reasonable time (<5 seconds per page)
- [ ] Memory usage is reasonable (check with `top` or Task Manager)

### Test 10.2: Many redactions
**Steps**:
1. Open a PDF with lots of text
2. Search for a common word (e.g., "the")
3. Redact all results (50+ instances)

**Expected Results**:
- [ ] Application remains responsive
- [ ] All instances are redacted
- [ ] No memory leaks (check memory usage before and after)

---

## Sign-Off

**Tester Name**: ___________________
**Date**: ___________________
**Platform Tested**: [ ] Linux [ ] Windows [ ] macOS
**Build Version**: ___________________

**Overall Assessment**:
- [ ] All critical tests passed
- [ ] No blocking issues found
- [ ] Ready for release

**Issues Found** (if any):

---

**Notes**:
- Any test failures should be documented as GitHub issues
- Critical failures (data loss, security issues) block the release
- Minor UI issues may be deferred to next release
