# Redaction UX Issues and Solutions

## Current Issues (v1.2.0)

### Issue #1: Redacted Text Still Selectable Until Save

**Observed Behavior:**
1. User marks area for redaction and clicks "Apply Redaction"
2. Black box appears (visual confirmation) ✓
3. User can still select and extract text from the redacted area ❌
4. After saving the file, text is no longer extractable ✓

**Root Cause:**
```
ApplyRedactionAsync():
  1. Modifies in-memory PdfSharp document
  2. Re-renders page from in-memory document (shows black box)
  3. But text extraction ALWAYS reads from file on disk!

Text Extraction Flow:
  ExtractTextFromArea(pdfPath, ...)
    → PdfDocument.Open(pdfPath)  ← Opens FILE, not in-memory document
    → Extracts text from ORIGINAL file
```

**Why This Happens:**
- We have TWO representations of the PDF:
  - **PdfSharp Document** (in-memory, modified by redaction)
  - **File on Disk** (original, used by text extraction)
- Text extraction service (PdfPig) reads from disk, not from memory
- Until we save, the disk file still has the original text

**Code Locations:**
- `/PdfEditor/ViewModels/MainWindowViewModel.cs:744` - Uses `_currentFilePath` for extraction
- `/PdfEditor/Services/PdfTextExtractionService.cs:74` - Opens file from path

**Security Impact:**
- **Visual redaction is secure** (black box prevents viewing)
- **Content redaction is secure** (text removed from PDF structure)
- **Issue is UX confusion**: User can still extract text they think is redacted
- **After save**: Everything works correctly

**Workflow from User Testing:**
```
Time 11:35:09 - Apply redaction → Text removed from memory ✓
Time 11:35:17 - Select redacted area → Still extracts text ❌ "TOWN/CITY..."
Time 11:35:30 - Save file → Writes to disk ✓
Time 11:36:42 - Select same area → "Found 0 letters" ✓ Fixed after save
```

### Issue #2: Clipboard History Confusion

**Observed Behavior:**
1. User copies text: "FATHER'SFULLNAME"
2. User redacts area, clipboard shows: "TOWN/CITY..."
3. User can still extract text from redacted area
4. Clipboard history shows everything forever
5. User doesn't know what's actually protected

**Root Cause:**
- Clipboard history never clears
- Shows mix of:
  - Text copied before redaction (still in PDF)
  - Text that was redacted (should be gone)
  - Text extracted from "redacted" areas (because file not saved yet)
- No visual distinction between these states

**User Confusion:**
- "I see this text in clipboard history - is it still in the PDF?"
- "I redacted it but can still select it - did redaction work?"
- "The clipboard shows text I removed - why?"

## Solutions

### Quick Fixes (v1.2.0)

#### Fix 1: Save-to-Complete Workflow
Add clear message after redaction:
```
✓ Redaction applied (in memory)
⚠ Save the file to complete redaction
  Text selection will be disabled until save
```

Disable text selection mode until file is saved after redaction.

#### Fix 2: Clipboard History Labels
Add visual indicators:
- 📋 Normal copy (text still in PDF)
- 🔴 Redacted (removed from memory, save to persist)
- ✓ Saved (text permanently removed)

#### Fix 3: Clear Clipboard History on Save (Optional)
Add preference: "Clear clipboard history after saving redacted document"

### Long-term Solutions (v1.3.0 - UX Redesign)

#### Solution A: Mark-Then-Apply Workflow (RECOMMENDED)

**UX Flow:**
```
1. MARK phase:
   - User draws redaction areas (red dashed border)
   - Multiple areas can be marked
   - Areas shown in "Pending Redactions" panel
   - Nothing is modified yet - this is just visual

2. REVIEW phase:
   - User can see all pending redactions
   - Can remove/modify marked areas
   - Can extract text to see what will be removed
   - Text extraction still works (nothing redacted yet)

3. APPLY phase:
   - User clicks "Apply All Redactions"
   - Dialog: "This will permanently remove text. Continue?"
   - All redactions applied to in-memory document
   - Areas turn solid black
   - AUTOMATIC SAVE to disk
   - Text extraction reloads from saved file
   - No in-between state!
```

**Benefits:**
- Clear state transitions (Marked → Applied → Saved)
- No confusing "redacted but still extractable" state
- User reviews before committing
- Automatic save ensures consistency
- Clipboard history can distinguish marked vs applied

**Implementation:**
```csharp
// Pending redaction areas (visual only)
List<PendingRedaction> PendingRedactions;

MarkAreaForRedaction(Rect area)
{
    PendingRedactions.Add(new PendingRedaction
    {
        Area = area,
        PageIndex = CurrentPageIndex,
        PreviewText = ExtractText(area)  // Still works!
    });
    // No PDF modification yet
}

async Task ApplyAllRedactions()
{
    if (!await ConfirmDialog("Remove all marked text permanently?"))
        return;

    foreach (var pending in PendingRedactions)
    {
        _redactionService.RedactArea(pending.Area);
    }

    // CRITICAL: Save immediately to avoid state confusion
    await SaveFileAsync();

    // Reload document to sync memory and disk
    await ReloadCurrentDocument();

    PendingRedactions.Clear();
}
```

#### Solution B: Stream-Based Text Extraction

**Technical Approach:**
Modify PdfTextExtractionService to accept a stream instead of file path:

```csharp
// Current (v1.2.0)
string ExtractTextFromArea(string pdfPath, ...)
{
    using var document = PdfDocument.Open(pdfPath);  // Always reads disk
    ...
}

// Improved (v1.3.0)
string ExtractTextFromArea(Stream pdfStream, ...)
{
    using var document = PdfDocument.Open(pdfStream);  // Reads from memory
    ...
}

// ViewModel calls:
var stream = _documentService.GetCurrentDocumentAsStream();
var text = _textExtractionService.ExtractTextFromArea(stream, ...);
```

**Benefits:**
- Text extraction uses current in-memory state
- Redacted text immediately non-extractable
- More complex: need to manage stream lifecycle

**Drawbacks:**
- PdfPig may cache document structure
- Need to ensure stream is positioned correctly
- More memory usage (multiple document representations)

#### Solution C: Redacted Area Tracking

**Approach:**
Track redacted rectangles and block text extraction from them:

```csharp
List<RedactedArea> RedactedAreas;  // { PageIndex, Rect, Timestamp }

string ExtractTextFromArea(Rect area)
{
    // Check if area overlaps any redacted regions
    if (RedactedAreas.Any(r => r.Intersects(area)))
    {
        return "[Redacted - save file to confirm removal]";
    }

    // Normal extraction from file
    return base.ExtractText(area);
}
```

**Benefits:**
- Simple to implement
- Provides immediate feedback
- Doesn't require save

**Drawbacks:**
- Can't prevent extraction of overlapping non-redacted text
- Tracking state between save/reload cycles
- Doesn't solve the root cause

## Recommendations

### For v1.2.0 Release
1. **Document the behavior clearly** in README and UI
2. Add warning message: "Save file to complete redaction"
3. Optionally disable text selection after redaction until save

### For v1.3.0 Release (UX Redesign)
1. **Implement Mark-Then-Apply workflow** (Solution A) - PRIMARY
2. **Add stream-based text extraction** (Solution B) - SECONDARY
3. **Clear state indicators** (Marked → Applied → Saved)
4. **Automatic save after apply** with user confirmation

### Clipboard History Improvements
1. Add visual indicators for entry type (📋 copy, 🔴 redacted, ✓ saved)
2. Add "Clear History" button
3. Add timestamps
4. Optionally auto-clear on save (user preference)

## Testing Scenarios

### Test 1: Current Behavior (v1.2.0)
```
✓ Apply redaction → Black box appears
❌ Select redacted area → Text still extractable
✓ Save file
✓ Select redacted area → No text found
```

### Test 2: Mark-Then-Apply (v1.3.0)
```
✓ Mark area → Red dashed border, no modification
✓ Select marked area → Text still extractable (expected!)
✓ Apply all → Black boxes, auto-save, reload
✓ Select applied area → No text found (immediate)
```

### Test 3: Clipboard History
```
✓ Copy text → Shows in history with 📋
✓ Mark for redaction → Shows in pending with 🔴
✓ Apply → Auto-save, history shows ✓
✓ Optional: Clear history on save
```

## User Documentation

### Current Workflow (v1.2.0)
```markdown
## How Redaction Works

1. Click "Redact Mode" button
2. Draw a rectangle over sensitive text
3. Click "Apply Redaction" - a black box appears
4. **IMPORTANT**: Save the file to complete redaction
5. Text is removed from PDF structure after save

**Note**: Text may still be selectable until you save the file.
This is visual only - the text WILL be removed when you save.
```

### Future Workflow (v1.3.0)
```markdown
## How Redaction Works

1. Click "Redact Mode" button
2. Mark areas to redact (red dashed boxes)
3. Review pending redactions in sidebar
4. Click "Apply All Redactions"
5. Confirm the action
6. File is automatically saved
7. All marked text is permanently removed

**States**:
- Red dashed border = Marked (pending, text still accessible)
- Solid black box = Applied (permanently removed from PDF)
```

## Implementation Priority

### v1.2.0 (Current Release)
- [x] Document behavior in README
- [ ] Add warning message after redaction
- [ ] Optionally disable text selection until save

### v1.3.0 (UX Redesign)
- [ ] Implement mark-then-apply workflow
- [ ] Add pending redactions panel
- [ ] Automatic save after apply
- [ ] Stream-based text extraction (optional)
- [ ] Enhanced clipboard history with state indicators
- [ ] User confirmation dialogs
- [ ] Comprehensive testing

## References

- Log excerpt showing the issue: User testing session 11:34-11:36
- UX Redesign Proposal: `/UX_REDESIGN_PROPOSAL.md`
- Code locations: `MainWindowViewModel.cs:714-846`, `PdfTextExtractionService.cs:66-140`
- Test coverage: `SelectiveInstanceRedactionTests.cs` (verifies text removal after save)
