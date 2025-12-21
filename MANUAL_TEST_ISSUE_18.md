# Manual Test for Issue #18: Pending Redactions UI Display

## Test Objective
Verify that the pending redactions panel displays correctly in the UI when marking redaction areas.

## Prerequisites
- App is running: `dotnet run` from `/home/marc/pdfe/PdfEditor`
- Have a test PDF file ready to open

## Test Steps

### 1. Verify Initial State
- [ ] Open the PDF Editor application
- [ ] Look at the right sidebar - should see "REDACTION" section
- [ ] **Expected**: Panel exists but shows "Pending (0)" or similar
- [ ] **Expected**: Panel is empty (no redaction items listed)

### 2. Open a PDF Document
- [ ] Click File → Open (or press Ctrl+O)
- [ ] Select any PDF file
- [ ] **Expected**: PDF loads successfully
- [ ] **Expected**: Pending redactions panel still shows (0) items

### 3. Enter Redaction Mode
- [ ] Click the "Redact Mode" button (or press 'R' if keyboard shortcut is implemented)
- [ ] **Expected**: Redaction mode activates (visual indicator should change)
- [ ] **Expected**: Pending redactions panel still shows (0) items

### 4. Mark First Redaction Area
- [ ] Click and drag on the PDF to select an area with text
- [ ] Release the mouse button
- [ ] **Expected**: Red dashed rectangle appears on PDF ✅
- [ ] **Expected**: Right sidebar updates to show "Pending (1)" ⭐ **CRITICAL**
- [ ] **Expected**: First redaction appears in list with:
  - Page number (e.g., "Page 1")
  - Preview text from that area
  - Timestamp or ID

### 5. Mark Additional Redaction Areas
- [ ] Draw another redaction box on the same page
- [ ] **Expected**: "Pending (2)" count updates ⭐
- [ ] **Expected**: Second item appears in list

- [ ] Navigate to page 2 (if PDF has multiple pages)
- [ ] Draw a redaction box on page 2
- [ ] **Expected**: "Pending (3)" count updates
- [ ] **Expected**: Third item shows "Page 2"

### 6. Verify UI Binding
- [ ] Check that the pending redactions list updates **immediately** when drawing boxes
- [ ] **Expected**: No delay, no need to click refresh
- [ ] **Expected**: ObservableCollection binding works automatically

### 7. Visual Verification
Check the right sidebar displays:
- [ ] Section header: "REDACTION" or similar
- [ ] Count label: "Pending (X)" where X matches number of boxes drawn
- [ ] List items showing:
  - [ ] Page numbers
  - [ ] Preview text (truncated if long)
  - [ ] Each item clearly separated

## Success Criteria

✅ **PASS if**:
1. Pending redactions panel is visible in right sidebar
2. Count updates immediately when drawing redaction boxes
3. Each redaction appears as a list item with page number and preview text
4. UI updates without manual refresh (ObservableCollection working)

❌ **FAIL if**:
1. Sidebar is blank/empty when redactions are marked
2. Count doesn't update or stays at (0)
3. No list items appear
4. Need to click something to refresh the list

## Backend Verification

The console logs should show (if running with `dotnet run`):
```
16:29:31.014 info: Loaded 7 recent files
```

And when marking redactions, the RedactionWorkflowManager should be adding items to the PendingRedactions collection (verified by our unit tests).

## If Test Fails

### Troubleshooting Steps

**If sidebar is completely blank:**
1. Check MainWindow.axaml - verify `<ListBox ItemsSource="{Binding RedactionWorkflow.PendingRedactions}">` exists
2. Check that Right sidebar Grid.Column="2" is visible and not collapsed

**If count is always (0):**
1. Check that redaction mode is calling `RedactionWorkflow.MarkArea()` not the old immediate-apply method
2. Verify MainWindowViewModel has `public RedactionWorkflowManager RedactionWorkflow { get; }` property
3. Check binding path is correct in XAML

**If list doesn't update automatically:**
1. Verify `PendingRedactions` is `ObservableCollection<PendingRedaction>` (not `List` or `ReadOnlyObservableCollection`)
2. Check that MarkArea() calls `_pending.Add()` which triggers collection changed event
3. Our unit tests verify this works - issue would be in XAML binding

## Related Files

- `/home/marc/pdfe/PdfEditor/Views/MainWindow.axaml` - UI definition for sidebar
- `/home/marc/pdfe/PdfEditor/ViewModels/RedactionWorkflowManager.cs` - Backend logic
- `/home/marc/pdfe/PdfEditor/ViewModels/MainWindowViewModel.cs` - Wire-up
- `/home/marc/pdfe/PdfEditor/Models/PendingRedaction.cs` - Data model
- `/home/marc/pdfe/PdfEditor.Tests/Unit/MainWindowViewModelTests.cs` - Unit tests (passing)

## Automated Test Coverage

✅ Unit tests verify:
- RedactionWorkflow is initialized
- PendingRedactions collection is accessible
- Adding items increases count
- ObservableCollection notifies on changes
- Multiple redactions all stored
- Collection implements INotifyCollectionChanged

⚠️ **Still need**: UI integration test to verify actual ListBox binding (would require headless UI test with Avalonia)

## Test Date

Date: _____________
Tester: _____________
Result: ⬜ PASS  ⬜ FAIL
Notes: ___________________________________________________
