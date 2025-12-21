# GitHub Issues - PDF Editor v1.3.0 Backlog

This file contains issues to be imported into GitHub. Each issue is separated by `---`.

---

## Issue 1: Test Recent Files menu functionality

**Labels:** `bug`, `component: file-management`, `priority: high`, `effort: small`

**Description:**

The Recent Files menu has been refactored to populate dynamically using `SubmenuOpened` event handler (commit 55378ad). Need to verify this fix works correctly.

**Steps to Test:**
1. Run the application: `dotnet run`
2. Open File → Recent Files menu
3. Verify menu shows recent file paths (should show 7 files)
4. Click on a recent file
5. Verify the file loads correctly

**Expected Behavior:**
- Menu shows list of recent files when opened
- Console shows: `>>> RecentFilesMenu_SubmenuOpened: RecentFiles.Count = 7`
- Console shows: `>>> Added menu item: /path/to/file.pdf` for each file
- Clicking a file shows: `>>> LoadRecentFileAsync CALLED with filePath: '/path/to/file.pdf'`
- Selected file opens in the application

**Acceptance Criteria:**
- [ ] Menu displays recent files
- [ ] Clicking a file loads it
- [ ] Console logging shows expected output

---

## Issue 2: Test pending redactions UI display

**Labels:** `bug`, `component: ui-framework`, `component: redaction-engine`, `priority: high`, `effort: small`

**Description:**

The pending redactions UI was refactored to use `ObservableCollection` instead of `ReadOnlyObservableCollection` (commit e8f6296). Backend logging shows redactions are being marked correctly, but UI display needs verification.

**Steps to Test:**
1. Run the application: `dotnet run`
2. Open a PDF file
3. Press 'R' to enter redaction mode
4. Draw a redaction box by click-and-drag on the PDF
5. Check right sidebar "Pending Redactions" panel

**Expected Behavior:**
- Right sidebar shows "Pending Redactions (1)"
- Panel displays: page number, preview text, redaction area info
- Drawing more boxes increases the count
- "×" button removes a pending redaction
- Backend logs show: `Redaction marked. Total pending: N`

**Acceptance Criteria:**
- [ ] Pending redactions appear in right sidebar
- [ ] Count updates correctly
- [ ] Preview text shows extracted content
- [ ] Remove button works

---

## Issue 3: Implement "Apply All Redactions" button and workflow

**Labels:** `enhancement`, `component: redaction-engine`, `component: ui-framework`, `priority: critical`, `effort: medium`

**Description:**

The mark-then-apply workflow for v1.3.0 is partially implemented. Users can mark redaction areas, but there's no way to actually apply them to the PDF.

**Current State:**
- ✅ Users can mark redaction areas
- ✅ Pending redactions are tracked in `RedactionWorkflowManager`
- ✅ Preview text is extracted
- ✅ Pending list displays in UI (needs verification per Issue #2)
- ❌ No "Apply All" button
- ❌ No way to save redacted PDF

**Implementation Tasks:**
1. Add "Apply All Redactions" button to UI
   - Location: Bottom of pending redactions panel
   - Enabled only when pending count > 0
2. Create `ApplyAllRedactionsCommand` in `MainWindowViewModel`
3. Implement `ApplyAllRedactionsAsync()` method:
   - Iterate through `RedactionWorkflow.PendingRedactions`
   - Call `RedactionService.RedactArea()` for each
   - Handle errors gracefully (continue on error, log failures)
   - Move successful redactions to applied list
   - Re-render affected pages
4. Add confirmation dialog before applying (optional but recommended)
5. Suggest filename using `FilenameSuggestionService`
6. Clear current redaction area after apply

**Acceptance Criteria:**
- [ ] "Apply All" button appears in pending redactions panel
- [ ] Button disabled when no pending redactions
- [ ] Clicking button applies all redactions to PDF
- [ ] Applied redactions move to "Applied" list
- [ ] PDF re-renders with redacted content
- [ ] Errors are logged and handled gracefully
- [ ] User can save redacted PDF

**Related Files:**
- `PdfEditor/ViewModels/MainWindowViewModel.cs`
- `PdfEditor/Views/MainWindow.axaml`
- `PdfEditor/Services/RedactionService.cs`
- `PdfEditor/ViewModels/RedactionWorkflowManager.cs`

---

## Issue 4: Remove debug logging after Recent Files verification

**Labels:** `technical-debt`, `component: file-management`, `priority: medium`, `effort: small`

**Description:**

Debug logging was added extensively to diagnose Recent Files menu issues (commits e3760dc, 55378ad). Once functionality is verified, this logging should be removed or reduced.

**Debug Logging Locations:**
1. `MainWindowViewModel.cs`:
   - `RecentFileMenuItems` getter (lines 328-344)
   - `LoadRecentFileAsync` method (line 1562)
   - `LoadRecentFileCommand` initialization (line 139)
2. `MainWindow.axaml.cs`:
   - `RecentFilesMenu_SubmenuOpened` event handler (lines 396, 409, 423)

**Tasks:**
- [ ] Verify Recent Files menu works (Issue #1)
- [ ] Remove console logging from `RecentFilesMenu_SubmenuOpened`
- [ ] Remove debug logging from `RecentFileMenuItems` getter
- [ ] Keep only the initial log in `LoadRecentFileAsync` at INFO level
- [ ] Remove command initialization debug log

**Acceptance Criteria:**
- Recent Files functionality works without verbose debug output
- Only essential INFO-level logs remain

---

## Issue 5: Restore logging level to INFO from DEBUG

**Labels:** `technical-debt`, `priority: medium`, `effort: small`

**Description:**

Logging level was changed to DEBUG (commit e3760dc) to diagnose Recent Files menu issues. This produces excessive console output and should be restored to INFO after debugging is complete.

**Changes Needed:**
- `PdfEditor/App.axaml.cs` line 64: Change `LogLevel.Debug` back to `LogLevel.Information`
- Update log message on line 99: "Logging level set to: INFO"

**Depends On:** Issues #1, #4

**Acceptance Criteria:**
- [ ] Logging level set to INFO
- [ ] Console output is clean and readable
- [ ] Essential operations still logged

---

## Issue 6: Improve Recent Files menu display - show basenames instead of full paths

**Labels:** `enhancement`, `component: file-management`, `component: ui-framework`, `priority: low`, `effort: small`

**Description:**

Current Recent Files menu shows full file paths, which are very long and hard to read:
```
/home/marc/Downloads/Birth Certificate Request (PDF)2.pdf
/home/marc/Downloads/NIST-2024-0002-0062_attachment_1.pdf
```

**Proposed Enhancement:**

Show just the filename with tooltip showing full path:
```
Menu Item: Birth Certificate Request (PDF)2.pdf
Tooltip: /home/marc/Downloads/Birth Certificate Request (PDF)2.pdf
```

**Implementation:**
- Modify `RecentFilesMenu_SubmenuOpened` in `MainWindow.axaml.cs`
- Set `Header = Path.GetFileName(filePath)`
- Set `ToolTip.Tip = filePath`
- Keep `CommandParameter = filePath` (full path for loading)

**Acceptance Criteria:**
- [ ] Menu shows only filename
- [ ] Hovering shows full path in tooltip
- [ ] Clicking still loads the correct file

---

## Issue 7: Save and restore window position and size

**Labels:** `enhancement`, `component: ui-framework`, `component: file-management`, `priority: medium`, `effort: medium`

**Description:**

The application currently doesn't save window position, size, or state (maximized/normal). Users have to resize and reposition the window every time they launch the app.

**Current Behavior:**
- Window opens at default size/position
- No persistence of window state

**Desired Behavior:**
- Save window position, size, and state on close
- Restore on next launch
- Use platform-standard config location (already using `Environment.SpecialFolder.ApplicationData/PdfEditor/`)

**Implementation Approach:**
1. Create `WindowSettings.json` in app data folder
2. Save settings on window close:
   ```json
   {
     "X": 100,
     "Y": 100,
     "Width": 1200,
     "Height": 800,
     "WindowState": "Normal"
   }
   ```
3. Load and apply settings in `MainWindow.axaml.cs` constructor
4. Handle multi-monitor scenarios (window off-screen)

**Files to Modify:**
- `PdfEditor/Views/MainWindow.axaml.cs` - Add save/restore logic
- `PdfEditor/Models/WindowSettings.cs` - New model class

**Acceptance Criteria:**
- [ ] Window size/position saved on close
- [ ] Window restored to saved position on launch
- [ ] Maximized state is preserved
- [ ] Handles off-screen scenarios gracefully

---

## Issue 8: Add keyboard shortcut documentation to README

**Labels:** `documentation`, `priority: low`, `effort: small`

**Description:**

The application has extensive keyboard shortcuts (implemented in `MainWindow.axaml.cs`), and there's a keyboard shortcuts dialog (F1), but they're not documented in the README.

**Shortcuts to Document:**
- File: Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+W, Ctrl+P
- Edit: Ctrl+F, F3, Shift+F3, T (text mode), R (redaction mode)
- View: Ctrl++, Ctrl+-, Ctrl+0, Ctrl+1, Ctrl+2
- Navigation: PgUp, PgDn, Home, End, Arrow keys
- Help: F1

**Tasks:**
- [ ] Add "Keyboard Shortcuts" section to README.md
- [ ] Format as a table for readability
- [ ] Include brief description of each shortcut

---

## Issue 9: Handle deleted files in Recent Files list

**Labels:** `bug`, `component: file-management`, `priority: low`, `effort: small`

**Description:**

If a file in the Recent Files list is deleted, it still appears in the menu. Clicking it shows a warning in the console but doesn't remove it from the list or show a user-facing message.

**Current Behavior:**
- Deleted files remain in Recent Files menu
- Clicking deleted file logs: "Recent file not found: /path/to/file.pdf"
- No feedback to user

**Desired Behavior:**
- Option 1: Remove deleted files from list on click + show message dialog
- Option 2: Filter out non-existent files when loading recent files
- Option 3: Show disabled menu items for deleted files with strikethrough

**Implementation Notes:**
- File existence check already exists in `LoadRecentFileAsync` (line 1570)
- TODO comment on line 1573: "Could show a message box and remove from recent files"

**Acceptance Criteria:**
- [ ] User gets clear feedback when clicking deleted file
- [ ] Deleted file is removed from Recent Files list
- [ ] Recent files list persists the removal

---

## Issue 10: Optimize PDF rendering cache for memory usage

**Labels:** `enhancement`, `component: pdf-rendering`, `priority: low`, `effort: medium`

**Description:**

`PdfRenderService` caches rendered pages as PNG bytes in memory. Current implementation:
- Default max cache: 20 entries
- No memory limit, only entry count limit
- No cache size monitoring

**Potential Issues:**
- Large PDFs at high DPI could consume significant memory
- No visibility into cache memory usage
- No automatic eviction based on memory pressure

**Enhancement Ideas:**
1. Add memory limit in addition to entry count limit
2. Log cache statistics (hits, misses, memory usage)
3. Configurable cache size via preferences
4. Consider compression for cached PNG data

**Files:**
- `PdfEditor/Services/PdfRenderService.cs`

**Acceptance Criteria:**
- [ ] Cache has configurable memory limit
- [ ] Cache statistics are logged
- [ ] Memory usage is monitored and reported

---

## Issue 11: Add unit tests for RedactionWorkflowManager

**Labels:** `enhancement`, `component: redaction-engine`, `priority: medium`, `effort: medium`

**Description:**

`RedactionWorkflowManager` is a new class (v1.3.0) that manages mark-then-apply workflow state. It has no unit tests yet.

**Test Coverage Needed:**
1. `MarkArea()` - adds to pending list, raises property changes
2. `RemovePending()` - removes by ID, handles not found
3. `ClearPending()` - clears all pending
4. `MoveToApplied()` - moves all pending to applied
5. `GetPendingForPage()` - filters by page number
6. `GetAppliedForPage()` - filters by page number
7. `Reset()` - clears all state
8. Property change notifications

**Files:**
- `PdfEditor.Tests/Unit/RedactionWorkflowManagerTests.cs` (new file)

**Acceptance Criteria:**
- [ ] All public methods have unit tests
- [ ] Property change notifications are tested
- [ ] Edge cases are covered (empty lists, not found, etc.)

---

## Issue 12: Add integration test for complete mark-then-apply workflow

**Labels:** `enhancement`, `component: redaction-engine`, `priority: high`, `effort: large`

**Description:**

v1.3.0 introduces mark-then-apply redaction workflow. Need end-to-end integration test that simulates the complete user flow.

**Test Scenario:**
1. Open PDF
2. Mark 3 redaction areas on different pages
3. Verify pending count = 3
4. Apply all redactions
5. Verify applied count = 3, pending count = 0
6. Save PDF
7. Extract text from saved PDF
8. Verify redacted text is removed (TRUE redaction)

**Similar Existing Tests:**
- `ComprehensiveRedactionTests.cs` - has full redaction verification
- `GuiRedactionSimulationTests.cs` - simulates GUI workflow

**Files:**
- `PdfEditor.Tests/Integration/MarkThenApplyWorkflowTests.cs` (new file)

**Acceptance Criteria:**
- [ ] Test covers complete mark-then-apply flow
- [ ] Verifies TRUE glyph-level removal (not just visual)
- [ ] Tests multi-page scenarios
- [ ] Handles errors gracefully

---

## Issue 13: Investigate and fix build warning CS8618 systematically

**Labels:** `technical-debt`, `priority: medium`, `effort: small`

**Description:**

We've been fixing nullable warnings reactively as they appear. Need systematic approach to prevent them.

**Recent Occurrences:**
- `LoadRecentFileCommand` (fixed in commit 282bc61)
- Previous occurrences in other command properties

**Systematic Fix:**
1. Review all command properties in `MainWindowViewModel.cs`
2. Add `= null!;` to all get-only properties initialized in constructor
3. Document pattern in code comments or CLAUDE.md
4. Set up analyzer rules to catch these proactively

**Files:**
- `PdfEditor/ViewModels/MainWindowViewModel.cs`
- `PdfEditor/.editorconfig` (optional - add analyzer rules)

**Acceptance Criteria:**
- [ ] All command properties have null-forgiving operator
- [ ] Build produces 0 warnings
- [ ] Pattern is documented for future properties

---

## Issue 14: Verify PDF 1.7 and PDF 2.0 conformance for mark-then-apply workflow

**Labels:** `enhancement`, `component: verification`, `component: redaction-engine`, `priority: high`, `effort: medium`

**Description:**

Existing redaction engine has PDF conformance tests (`PdfConformanceTests.cs`, `VeraPdfConformanceTests.cs`), but they test immediate apply workflow. Need to verify mark-then-apply workflow maintains conformance.

**Test Scenarios:**
1. Mark multiple redactions, apply all at once
2. Mark redactions across multiple pages
3. Mark and remove some redactions before applying
4. Verify resulting PDF conforms to PDF 1.7 spec
5. Verify resulting PDF conforms to PDF 2.0 spec

**Existing Tests to Reference:**
- `PdfEditor.Tests/Integration/PdfConformanceTests.cs`
- `PdfEditor.Tests/Integration/VeraPdfConformanceTests.cs`

**Acceptance Criteria:**
- [ ] Mark-then-apply produces valid PDF 1.7 documents
- [ ] Mark-then-apply produces valid PDF 2.0 documents
- [ ] External validators (qpdf, mutool) pass
- [ ] VeraPDF validation passes

---

## Issue 15: Add "Clear All" button for pending redactions

**Labels:** `enhancement`, `component: ui-framework`, `component: redaction-engine`, `priority: low`, `effort: small`

**Description:**

Users can remove pending redactions one-by-one with the "×" button, but there's no way to clear all pending redactions at once.

**Proposed Enhancement:**

Add "Clear All" button next to "Apply All Redactions" button in pending redactions panel.

**Implementation:**
1. Add `ClearAllPendingCommand` to `MainWindowViewModel`
2. Wire to `RedactionWorkflow.ClearPending()` (already exists)
3. Add button to UI (disabled when count = 0)
4. Optional: Add confirmation dialog

**Acceptance Criteria:**
- [ ] "Clear All" button appears in pending redactions panel
- [ ] Button disabled when no pending redactions
- [ ] Clicking clears all pending redactions
- [ ] Pending count updates to 0

---

## Issue 16: Persist zoom level preference across sessions

**Labels:** `enhancement`, `component: file-management`, `component: pdf-rendering`, `priority: low`, `effort: small`

**Description:**

Zoom level resets to default every time a document is opened. Users have to re-adjust zoom each time.

**Desired Behavior:**
- Save last used zoom level
- Restore on next document open
- Could be global preference or per-document

**Implementation Options:**
1. Global preference: Save to `ApplicationData/PdfEditor/settings.json`
2. Per-document: Save to `ApplicationData/PdfEditor/document-state.json` with file path as key

**Related:** Issue #7 (window settings)

**Acceptance Criteria:**
- [ ] Zoom level is saved
- [ ] Zoom level is restored on document open
- [ ] Works across application restarts

---

## Issue 17: Add visual feedback when in redaction mode

**Labels:** `enhancement`, `component: ui-framework`, `priority: medium`, `effort: small`

**Description:**

Currently, redaction mode is indicated by:
- Status bar text: "🔴 Redaction Mode"
- Redaction mode button appears pressed

**Enhancement Ideas:**
1. Change cursor to crosshair when in redaction mode
2. Add red border around PDF canvas
3. Add colored overlay to make mode more obvious
4. Show tip: "Click and drag to mark areas for redaction"

**Files:**
- `PdfEditor/Views/MainWindow.axaml`
- `PdfEditor/Views/MainWindow.axaml.cs`

**Acceptance Criteria:**
- [ ] Visual indicator clearly shows redaction mode is active
- [ ] Cursor changes to indicate drawing mode
- [ ] Easy to distinguish from normal viewing mode

---

## Issue 18: Implement search results navigation (current/total)

**Labels:** `enhancement`, `component: text-extraction`, `component: ui-framework`, `priority: medium`, `effort: medium`

**Description:**

Search functionality highlights all matches, but doesn't show:
- How many total matches were found
- Which match is currently selected
- Navigation through matches

**Current Behavior:**
- F3 / Shift+F3 navigate through matches
- All matches are highlighted
- No indication of position (e.g., "3 of 15")

**Desired Enhancement:**

Add search status showing: "Match 3 of 15" and scroll to center current match.

**Implementation:**
1. Add `CurrentMatchIndex` and `TotalMatches` properties to `MainWindowViewModel`
2. Update search UI to show "Match X of Y"
3. Find Next/Previous should update current index
4. Scroll viewport to center current match
5. Highlight current match differently (e.g., orange vs yellow)

**Acceptance Criteria:**
- [ ] Search shows "Match X of Y"
- [ ] Current match is highlighted differently
- [ ] Viewport scrolls to show current match
- [ ] F3/Shift+F3 update the current index

---

## Issue 19: Add ability to export single page as image

**Labels:** `enhancement`, `component: pdf-rendering`, `priority: low`, `effort: small`

**Description:**

Current export functionality exports all pages or a range. Would be useful to quickly export just the current page.

**Proposed Feature:**
- Add "Export Current Page..." menu item
- Keyboard shortcut: Ctrl+E
- Save as PNG/JPEG at configurable DPI

**Similar To:** Existing `ExportPagesAsync` functionality

**Acceptance Criteria:**
- [ ] Menu item "Export Current Page..."
- [ ] Exports current page as image
- [ ] User can choose format (PNG/JPEG)
- [ ] User can choose DPI (72, 150, 300)

---

## Issue 20: Document coordinate system handling for future contributors

**Labels:** `documentation`, `component: coordinates`, `priority: medium`, `effort: medium`

**Description:**

The codebase has extensive inline documentation about coordinate systems (see `MainWindow.axaml.cs` lines 279-296, `TextBoundsCalculator.cs`), but this critical knowledge isn't centralized.

**Issues:**
- Coordinate system knowledge scattered across files
- Easy for contributors to make coordinate bugs
- ARCHITECTURE_DIAGRAM.md doesn't cover this

**Proposed Documentation:**

Create `docs/COORDINATE_SYSTEMS.md` covering:
1. PDF coordinate system (bottom-left origin, points)
2. Avalonia/Screen coordinate system (top-left origin, pixels)
3. Image coordinate system (150 DPI, top-left origin)
4. Zoom transformations via ScaleTransform
5. When conversions happen and where
6. Common pitfalls and debugging tips

**Consolidate Knowledge From:**
- `MainWindow.axaml.cs` coordinate comments
- `TextBoundsCalculator.cs` implementation
- `CoordinateConverter.cs` implementation
- `GuiRedactionSimulationTests.cs` comments

**Acceptance Criteria:**
- [ ] COORDINATE_SYSTEMS.md created
- [ ] Covers all 3 coordinate systems
- [ ] Includes diagrams
- [ ] Links from CLAUDE.md and ARCHITECTURE_DIAGRAM.md

---

## Summary

**Total Issues: 20**

**By Priority:**
- Critical: 2
- High: 4
- Medium: 8
- Low: 6

**By Type:**
- Bug: 3
- Enhancement: 14
- Documentation: 2
- Technical Debt: 4

**By Component:**
- redaction-engine: 6
- ui-framework: 8
- file-management: 6
- pdf-rendering: 4
- text-extraction: 2
- coordinates: 2
- verification: 2
- clipboard: 0

**Immediate Next Steps (Priority: Critical/High):**
1. Issue #1: Test Recent Files menu
2. Issue #2: Test pending redactions UI
3. Issue #3: Implement "Apply All Redactions" (blocking v1.3.0 completion)
4. Issue #12: Integration test for workflow
5. Issue #14: PDF conformance verification
