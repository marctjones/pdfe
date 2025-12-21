# Implementation Plan for v1.3.0 - UX Redesign

**Version:** 1.3.0
**Target Date:** TBD
**Current Version:** 1.2.0
**Goal:** Implement simplified file operations and mark-then-apply redaction workflow

---

## Overview

This implementation plan breaks down the v1.3.0 UX redesign into small, testable increments. Each step includes:
- Implementation tasks
- New tests to write
- Tests to run for verification
- Git commit checkpoint

**Strategy:**
- Small commits with working code
- Test after each change
- Never break existing functionality
- Feature flags for in-progress work

**IMPORTANT:** This plan has been updated to include critical refactoring steps.
See [`REFACTORING_PLAN_V1.3.0.md`](REFACTORING_PLAN_V1.3.0.md) for detailed Pre-Phase 0 steps.

---

## Pre-Phase 0: Critical Refactoring (7 hours)

**⚠️ MUST DO FIRST - Prepares codebase and fixes root causes**

Before implementing new features, we need to refactor existing code to:
1. Reduce complexity of MainWindowViewModel
2. Fix root cause of "text still selectable after redaction" issue
3. Create clean foundation for mark-then-apply workflow

### Why Refactor First?

- **Fixes Root Cause:** Stream-based text extraction solves the extraction bug
- **Reduces Risk:** Smaller, focused changes in subsequent phases
- **Better Architecture:** Separated concerns, easier to maintain
- **Net Positive:** +7 hours now, saves 10+ hours in debugging later

### Refactoring Steps

**Step 0.1: Extract DocumentStateManager** (2 hours, 12 tests)
- Separates file state tracking from MainWindowViewModel
- Tracks original vs current file paths
- Detects redacted versions
- Manages unsaved changes count
- **Detailed spec:** See REFACTORING_PLAN_V1.3.0.md, Step 0.1

**Step 0.2: Add Stream-Based Text Extraction** (3 hours, 7 tests) 🔥 **CRITICAL**
- **Fixes root cause** of "text still selectable" issue
- Adds `ExtractTextFromArea(Stream)` overload
- Enables reading from in-memory modified documents
- Backwards compatible (existing code unchanged)
- **Detailed spec:** See REFACTORING_PLAN_V1.3.0.md, Step 0.2

**Step 0.3: Extract RedactionWorkflowManager** (2 hours, 10 tests)
- Separates redaction workflow from MainWindowViewModel
- Tracks pending and applied redactions
- Foundation for mark-then-apply workflow
- **Detailed spec:** See REFACTORING_PLAN_V1.3.0.md, Step 0.3

### Refactoring Results

After Pre-Phase 0:
- ✅ 29 new unit tests (all passing)
- ✅ Root cause of text extraction issue fixed
- ✅ MainWindowViewModel simplified
- ✅ Clean foundation for Phases 1-7
- ✅ All existing tests still pass

**Git Commits:** 3 commits (one per refactoring step)
**Time Investment:** 7 hours
**Risk Reduction:** Significant (subsequent phases now simpler)

---

## Phase 1: Foundation & File State Tracking (SIMPLIFIED)

**Note:** Steps 1.1 and 1.3 are now MUCH simpler since DocumentStateManager and RedactionWorkflowManager were created in Pre-Phase 0.

### Step 1.1: Wire Up DocumentStateManager (SIMPLIFIED)

**Implementation:**

Since DocumentStateManager was created in Step 0.1, we just need to wire it up:

```csharp
// MainWindowViewModel.cs - Already done in refactoring!
public DocumentStateManager FileState { get; }  // Already added in Step 0.1
```

**Changes:**
- ✅ Already done in Pre-Phase 0, Step 0.1
- Just verify it's wired up correctly

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~DocumentStateManagerTests"
# Should see: 12 tests passed (from refactoring)
```

**Time:** 30 minutes (just verification)

**Git Checkpoint:**
- No commit needed (already committed in Step 0.1)

---

### Step 1.2: Add Filename Suggestion Service

**Implementation:**
```csharp
// PdfEditor/Services/FilenameSuggestionService.cs (NEW FILE)
public class FilenameSuggestionService
{
    public string SuggestRedactedFilename(string originalPath)
    {
        // "contract.pdf" → "contract_REDACTED.pdf"
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        var dir = Path.GetDirectoryName(originalPath);

        return Path.Combine(dir, $"{name}_REDACTED{ext}");
    }

    public string SuggestPageSubsetFilename(string originalPath, string pageRange)
    {
        // "contract.pdf" + "1-5" → "contract_pages_1-5.pdf"
    }

    public string SuggestWithAutoIncrement(string path)
    {
        // If exists, return "contract_REDACTED_2.pdf"
    }
}
```

**Changes:**
- Create `PdfEditor/Services/FilenameSuggestionService.cs`
- Register in DI container (`App.axaml.cs`)
- Inject into `MainWindowViewModel`

**New Tests:**
```csharp
// PdfEditor.Tests/Unit/FilenameSuggestionServiceTests.cs (NEW FILE)
- SuggestRedactedFilename_AppendsRedacted()
- SuggestRedactedFilename_PreservesDirectory()
- SuggestRedactedFilename_PreservesExtension()
- SuggestWithAutoIncrement_WhenExists_AddsNumber()
- SuggestWithAutoIncrement_WhenNotExists_ReturnsOriginal()
- SuggestPageSubsetFilename_AddsPageRange()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~FilenameSuggestionServiceTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/Services/FilenameSuggestionService.cs PdfEditor.Tests/Unit/FilenameSuggestionServiceTests.cs PdfEditor/App.axaml.cs
git commit -m "feat: Add FilenameSuggestionService for smart filename generation"
```

---

### Step 1.3: Wire Up RedactionWorkflowManager (SIMPLIFIED)

**Implementation:**

Since RedactionWorkflowManager was created in Step 0.3, we just need to wire it up:

```csharp
// MainWindowViewModel.cs
public RedactionWorkflowManager RedactionWorkflow { get; }

public MainWindowViewModel(/* params */)
{
    // ... existing code ...

    // ADD: Initialize workflow manager
    RedactionWorkflow = new RedactionWorkflowManager();

    // ... rest of constructor ...
}
```

**Changes:**
- ✅ RedactionWorkflowManager already created in Pre-Phase 0, Step 0.3
- Add property to MainWindowViewModel
- Initialize in constructor
- Wire up property change notifications

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~RedactionWorkflowManagerTests"
# Should see: 10 tests passed (from refactoring)
```

**Time:** 30 minutes (just wiring)

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs
git commit -m "feat: Wire up RedactionWorkflowManager to MainWindowViewModel"
```

---

## Phase 2: Pending Redactions Panel

### Step 2.1: Create PendingRedaction Model

**Implementation:**
```csharp
// PdfEditor/Models/PendingRedaction.cs (NEW FILE)
public class PendingRedaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PageNumber { get; set; }
    public Rect Area { get; set; }
    public string PreviewText { get; set; }  // Text that will be removed
    public DateTime MarkedTime { get; set; } = DateTime.Now;
}
```

**Changes:**
- Create `PdfEditor/Models/PendingRedaction.cs`
- Add `ObservableCollection<PendingRedaction> PendingRedactions` to `MainWindowViewModel`

**New Tests:**
```csharp
// PdfEditor.Tests/Unit/PendingRedactionTests.cs (NEW FILE)
- Create_GeneratesUniqueId()
- Create_SetsCurrentTimestamp()
- Properties_GetSet_WorkCorrectly()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~PendingRedactionTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/Models/PendingRedaction.cs PdfEditor.Tests/Unit/PendingRedactionTests.cs PdfEditor/ViewModels/MainWindowViewModel.cs
git commit -m "feat: Add PendingRedaction model for mark-then-apply workflow"
```

---

### Step 2.2: Modify Redaction Mode to Mark Instead of Apply

**Implementation:**
- Change `ApplyRedactionCommand` behavior in redaction mode
- Instead of immediately redacting, add to `PendingRedactions` list:
  ```csharp
  private void MarkRedactionArea()
  {
      var pending = new PendingRedaction
      {
          PageNumber = CurrentPageIndex + 1,
          Area = CurrentRedactionArea,
          PreviewText = ExtractTextFromArea(CurrentRedactionArea)
      };

      PendingRedactions.Add(pending);
      FileState.PendingRedactionsCount = PendingRedactions.Count;
  }
  ```

**Changes:**
- Modify `MainWindowViewModel.cs` - change redaction command logic
- Add `MarkRedactionArea()` method
- Keep old `ApplyRedactionAsync()` for later use by "Apply All"

**New Tests:**
```csharp
// PdfEditor.Tests/Unit/MainWindowViewModelTests.cs (ADD TO EXISTING)
- MarkRedaction_AddsToCollection()
- MarkRedaction_ExtractsPreviewText()
- MarkRedaction_UpdatesFileState()
- MarkMultipleRedactions_AllStored()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~MainWindowViewModelTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor.Tests/Unit/MainWindowViewModelTests.cs
git commit -m "feat: Change redaction mode to mark areas instead of immediate apply"
```

---

### Step 2.3: Add Pending Redactions UI Panel

**Implementation:**
```xml
<!-- PdfEditor/Views/MainWindow.axaml (MODIFY) -->
<!-- Right sidebar - context-sensitive panel -->
<StackPanel Grid.Column="2" Width="250">
    <TextBlock Text="REDACTION" FontWeight="Bold" />

    <!-- Pending redactions section -->
    <TextBlock Text="{Binding PendingRedactions.Count, StringFormat='Pending ({0})'}" />
    <ListBox ItemsSource="{Binding PendingRedactions}" Height="200">
        <ListBox.ItemTemplate>
            <DataTemplate>
                <StackPanel>
                    <TextBlock Text="{Binding PageNumber, StringFormat='Page {0}'}" />
                    <TextBlock Text="{Binding PreviewText}"
                               MaxLines="2" TextTrimming="CharacterEllipsis" />
                </StackPanel>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>

    <!-- Action buttons -->
    <Button Content="Review All" Command="{Binding ReviewRedactionsCommand}" />
    <Button Content="Apply All" Command="{Binding ApplyAllRedactionsCommand}" />
</StackPanel>
```

**Changes:**
- Modify `PdfEditor/Views/MainWindow.axaml`
- Update layout to show pending redactions list
- Add buttons for Review and Apply All

**Tests to Run:**
```bash
# Manual UI testing
dotnet run

# Automated UI tests
dotnet test --filter "FullyQualifiedName~HeadlessUITests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/Views/MainWindow.axaml
git commit -m "feat: Add pending redactions panel to right sidebar"
```

---

### Step 2.4: Implement Visual Distinction (Pending vs Applied)

**Implementation:**
- Draw red dashed border for pending redactions
- Draw solid black box for applied redactions
- Store applied redactions separately from pending

**Changes:**
```csharp
// MainWindowViewModel.cs
private ObservableCollection<PendingRedaction> _appliedRedactions = new();

// During rendering, overlay both:
// - Pending: Red dashed Rectangle
// - Applied: Black solid Rectangle
```

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/VisualRedactionIndicatorsTests.cs (NEW FILE)
- PendingRedaction_ShowsRedDashedBorder()
- AppliedRedaction_ShowsBlackSolidBox()
- BothTypes_BothVisible()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~VisualRedactionIndicatorsTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor.Tests/Integration/VisualRedactionIndicatorsTests.cs
git commit -m "feat: Add visual distinction between pending and applied redactions"
```

---

## Phase 3: Apply All Redactions with Auto-Save

### Step 3.1: Implement Apply All Redactions Command

**Implementation:**
```csharp
private async Task ApplyAllRedactionsAsync()
{
    if (PendingRedactions.Count == 0)
        return;

    // Determine target filename
    string targetPath;
    if (FileState.IsOriginalFile)
    {
        // Show Save As dialog
        targetPath = await ShowSaveRedactedVersionDialog();
        if (targetPath == null)
            return; // User cancelled
    }
    else
    {
        // Already a redacted version, confirm update
        targetPath = FileState.CurrentFilePath;
    }

    // Apply all pending redactions to in-memory document
    var document = _documentService.GetCurrentDocument();
    foreach (var pending in PendingRedactions)
    {
        var page = document.Pages[pending.PageNumber - 1];
        _redactionService.RedactArea(page, pending.Area, renderDpi: 150);
    }

    // Save to target path
    document.Save(targetPath);

    // Update state
    FileState.CurrentFilePath = targetPath;
    _appliedRedactions.AddRange(PendingRedactions);
    PendingRedactions.Clear();
    FileState.PendingRedactionsCount = 0;

    // Reload from saved file (fixes text extraction issue!)
    await ReloadCurrentDocument();
}
```

**Changes:**
- Add `ApplyAllRedactionsCommand` to `MainWindowViewModel`
- Implement `ShowSaveRedactedVersionDialog()` method
- Implement `ReloadCurrentDocument()` method

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/ApplyAllRedactionsTests.cs (NEW FILE)
- ApplyAll_RedactsAllPendingAreas()
- ApplyAll_SavesWithSuggestedFilename()
- ApplyAll_PreservesOriginalFile()
- ApplyAll_MovesFromPendingToApplied()
- ApplyAll_ReloadsDocument()
- ApplyAll_TextNoLongerExtractable()  // CRITICAL TEST
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~ApplyAllRedactionsTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor.Tests/Integration/ApplyAllRedactionsTests.cs
git commit -m "feat: Implement Apply All Redactions with auto-save and reload"
```

---

### Step 3.2: Add Simple Save Redacted Version Dialog

**Implementation:**
```csharp
// PdfEditor/Views/SaveRedactedVersionDialog.axaml (NEW FILE)
<Window>
    <StackPanel Margin="20">
        <TextBlock Text="Save Redacted Version" FontSize="16" FontWeight="Bold" />

        <TextBlock Text="Save as:" Margin="0,20,0,5" />
        <Grid ColumnDefinitions="*,Auto">
            <TextBox Grid.Column="0" Text="{Binding SuggestedFilename}" />
            <Button Grid.Column="1" Content="Browse..." Click="BrowseButton_Click" />
        </Grid>

        <TextBlock Margin="0,20,0,0">
            <Run Text="{Binding PendingCount}" />
            <Run Text=" areas will be redacted" />
        </TextBlock>

        <TextBlock Text="Original file will be preserved ✓"
                   Foreground="Green" Margin="0,5,0,0" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
            <Button Content="Cancel" Click="CancelButton_Click" Margin="0,0,10,0" />
            <Button Content="Save" Click="SaveButton_Click" />
        </StackPanel>
    </StackPanel>
</Window>
```

**Changes:**
- Create `PdfEditor/Views/SaveRedactedVersionDialog.axaml`
- Create `PdfEditor/ViewModels/SaveRedactedVersionDialogViewModel.cs`
- Simple dialog with filename textbox and Save/Cancel buttons
- No checkboxes, no complexity

**New Tests:**
```csharp
// PdfEditor.Tests/UI/SaveRedactedVersionDialogTests.cs (NEW FILE)
- Dialog_ShowsSuggestedFilename()
- Dialog_ShowsPendingCount()
- Dialog_BrowseButton_OpensFilePicker()
- Dialog_SaveButton_ReturnsPath()
- Dialog_CancelButton_ReturnsNull()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~SaveRedactedVersionDialogTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/Views/SaveRedactedVersionDialog.axaml PdfEditor/ViewModels/SaveRedactedVersionDialogViewModel.cs PdfEditor.Tests/UI/SaveRedactedVersionDialogTests.cs
git commit -m "feat: Add simple Save Redacted Version dialog"
```

---

## Phase 4: Context-Aware Save Button

### Step 4.1: Add Dynamic Save Button Text

**Implementation:**
```csharp
// MainWindowViewModel.cs
public string SaveButtonText
{
    get
    {
        if (FileState.IsOriginalFile && FileState.HasUnsavedChanges)
            return "Save Redacted Version";
        if (FileState.IsRedactedVersion && FileState.HasUnsavedChanges)
            return "Save";
        return "Save"; // Will be grayed out
    }
}

public bool CanSave => FileState.HasUnsavedChanges;
```

**Changes:**
- Add `SaveButtonText` property to `MainWindowViewModel`
- Add `CanSave` property
- Update property when `FileState` changes
- Bind button text in XAML

**XAML Changes:**
```xml
<Button Content="{Binding SaveButtonText}"
        Command="{Binding SaveFileCommand}"
        IsEnabled="{Binding CanSave}" />
```

**New Tests:**
```csharp
// PdfEditor.Tests/Unit/MainWindowViewModelTests.cs (ADD TO EXISTING)
- SaveButtonText_OriginalWithChanges_ShowsSaveRedactedVersion()
- SaveButtonText_RedactedWithChanges_ShowsSave()
- SaveButtonText_NoChanges_ShowsSave()
- CanSave_NoChanges_ReturnsFalse()
- CanSave_WithChanges_ReturnsTrue()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~MainWindowViewModelTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor/Views/MainWindow.axaml PdfEditor.Tests/Unit/MainWindowViewModelTests.cs
git commit -m "feat: Add context-aware Save button text and enable state"
```

---

### Step 4.2: Implement Context-Aware Save Command

**Implementation:**
```csharp
private async Task SaveFileAsync()
{
    if (!CanSave)
        return;

    if (FileState.IsOriginalFile && FileState.HasUnsavedChanges)
    {
        // FORCE Save As dialog (prevent original overwrite)
        await ApplyAllRedactionsAsync(); // This shows the dialog
    }
    else if (FileState.IsRedactedVersion && FileState.HasUnsavedChanges)
    {
        // Safe to save directly
        await ApplyAllRedactionsAsync(); // No dialog, just confirm
    }
}
```

**Changes:**
- Modify `SaveFileAsync()` in `MainWindowViewModel`
- Add logic to prevent original overwrite
- Show appropriate dialog based on state

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/ContextAwareSaveTests.cs (NEW FILE)
- SaveOriginal_ShowsSaveAsDialog()
- SaveOriginal_CannotOverwrite()
- SaveRedactedVersion_UpdatesDirectly()
- SaveWithNoChanges_DoesNothing()
- CtrlS_OnOriginal_ShowsDialog()
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~ContextAwareSaveTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor.Tests/Integration/ContextAwareSaveTests.cs
git commit -m "feat: Implement context-aware Save command to prevent original overwrite"
```

---

## Phase 5: Integration Testing

### Step 5.1: End-to-End Workflow Tests

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/EndToEndWorkflowTests.cs (NEW FILE)
- FullWorkflow_OpenMarkApplySave_Success()
- FullWorkflow_OriginalPreserved_RedactedCreated()
- FullWorkflow_TextNotExtractableAfterSave()
- FullWorkflow_ContinueWorkingOnRedactedVersion()
- FullWorkflow_MultipleRedactionSessions()
```

**Example Test:**
```csharp
[Fact]
public async Task FullWorkflow_OpenMarkApplySave_Success()
{
    // Arrange: Create test PDF
    var originalPath = CreateTestPdf("test.pdf", "SECRET text here");

    // Act: Open, mark, apply, save
    await _viewModel.OpenFileAsync(originalPath);
    _viewModel.CurrentRedactionArea = new Rect(10, 10, 100, 20);
    _viewModel.MarkRedactionArea();

    Assert.Equal(1, _viewModel.PendingRedactions.Count);

    await _viewModel.ApplyAllRedactionsAsync();

    // Assert: Two files exist
    Assert.True(File.Exists(originalPath));
    Assert.True(File.Exists("test_REDACTED.pdf"));

    // Assert: Original unchanged
    var originalText = PdfTestHelpers.ExtractAllText(originalPath);
    Assert.Contains("SECRET", originalText);

    // Assert: Redacted version has no text
    var redactedText = PdfTestHelpers.ExtractAllText("test_REDACTED.pdf");
    Assert.DoesNotContain("SECRET", redactedText);
}
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~EndToEndWorkflowTests"
dotnet test  # Run ALL tests
```

**Git Checkpoint:**
```bash
git add PdfEditor.Tests/Integration/EndToEndWorkflowTests.cs
git commit -m "test: Add end-to-end workflow tests for mark-then-apply redaction"
```

---

### Step 5.2: Text Extraction After Redaction Tests

**Purpose:** Verify the fix for "redacted text still selectable until save" issue

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/TextExtractionAfterRedactionTests.cs (NEW FILE)
- TextExtraction_AfterMarkBefore Apply_TextStillExtractable()
- TextExtraction_AfterApply_TextNotExtractable()  // CRITICAL
- TextExtraction_FromRedactedFile_NoLeaks()
- TextExtraction_MultipleRedactions_AllTextRemoved()
```

**Critical Test:**
```csharp
[Fact]
public async Task TextExtraction_AfterApply_TextNotExtractable()
{
    // This test verifies the fix for Issue #1 from REDACTION_UX_ISSUES.md

    // Arrange
    var pdfPath = CreateTestPdf("test.pdf", "CONFIDENTIAL information");
    await _viewModel.OpenFileAsync(pdfPath);

    // Mark area containing "CONFIDENTIAL"
    _viewModel.CurrentRedactionArea = GetAreaContaining("CONFIDENTIAL");
    _viewModel.MarkRedactionArea();

    // Apply all (which auto-saves)
    await _viewModel.ApplyAllRedactionsAsync();

    // Act: Try to extract text from same area
    var extractedText = _textExtractionService.ExtractTextFromArea(
        _viewModel.FileState.CurrentFilePath,
        0,
        GetAreaContaining("CONFIDENTIAL"));

    // Assert: Text is NOT extractable (redaction worked!)
    Assert.DoesNotContain("CONFIDENTIAL", extractedText);
    Assert.Empty(extractedText); // Should be completely empty
}
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~TextExtractionAfterRedactionTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor.Tests/Integration/TextExtractionAfterRedactionTests.cs
git commit -m "test: Verify text extraction fix - redacted text not extractable after apply"
```

---

### Step 5.3: Original File Protection Tests

**New Tests:**
```csharp
// PdfEditor.Tests/Integration/OriginalFileProtectionTests.cs (NEW FILE)
- CannotOverwriteOriginal_ThroughNormalSave()
- SaveButton_OnOriginal_Disabled()
- CtrlS_OnOriginal_ShowsDialog()
- FileExists_OriginalUnchanged_AfterRedaction()
- FileTimestamp_OriginalNotModified()
```

**Example Test:**
```csharp
[Fact]
public void CannotOverwriteOriginal_ThroughNormalSave()
{
    // Arrange
    var originalPath = CreateTestPdf("important.pdf", "Data");
    var originalTimestamp = File.GetLastWriteTime(originalPath);

    _viewModel.OpenFileAsync(originalPath).Wait();
    _viewModel.CurrentRedactionArea = new Rect(10, 10, 100, 20);
    _viewModel.MarkRedactionArea();

    // Act: Try to save (should show dialog, not overwrite)
    _viewModel.SaveFileCommand.Execute(null);

    // Assert: Original file timestamp unchanged
    var newTimestamp = File.GetLastWriteTime(originalPath);
    Assert.Equal(originalTimestamp, newTimestamp);
}
```

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~OriginalFileProtectionTests"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor.Tests/Integration/OriginalFileProtectionTests.cs
git commit -m "test: Add original file protection tests to prevent accidental overwrites"
```

---

## Phase 6: Polish & User Experience

### Step 6.1: Add Status Bar Updates

**Implementation:**
```csharp
public string StatusBarText
{
    get
    {
        if (FileState.PendingRedactionsCount > 0)
            return $"{FileState.PendingRedactionsCount} areas marked";
        if (FileState.IsOriginalFile)
            return "Ready";
        if (FileState.IsRedactedVersion)
            return "Redacted version";
        return "Ready";
    }
}
```

**Changes:**
- Update status bar to show current state
- Show pending count
- Show file type (Original vs Redacted)

**Tests to Run:**
```bash
dotnet run  # Manual testing
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor/Views/MainWindow.axaml
git commit -m "feat: Update status bar to show pending redactions and file type"
```

---

### Step 6.2: Add Keyboard Shortcuts

**Implementation:**
- `Ctrl+S`: Context-aware save
- `Ctrl+Enter`: Apply All Redactions
- `Escape`: Clear current selection
- `Delete`: Remove selected pending redaction

**Changes:**
- Add key bindings to `MainWindow.axaml`
- Wire up to commands

**Tests to Run:**
```bash
dotnet run  # Manual testing
dotnet test --filter "FullyQualifiedName~KeyboardShortcutTests"
```

**Git Checkpoint:**
```bash
git add PdfEditor/Views/MainWindow.axaml PdfEditor.Tests/UI/KeyboardShortcutTests.cs
git commit -m "feat: Add keyboard shortcuts for redaction workflow"
```

---

### Step 6.3: Add Remove Pending Redaction

**Implementation:**
- Allow user to click on pending redaction in list to select
- Add "Remove" button or Delete key to remove from list
- Update pending count

**Changes:**
- Add `SelectedPendingRedaction` property
- Add `RemovePendingRedactionCommand`
- Bind Delete key to remove

**Tests to Run:**
```bash
dotnet test --filter "FullyQualifiedName~PendingRedactionManagement"
dotnet test
```

**Git Checkpoint:**
```bash
git add PdfEditor/ViewModels/MainWindowViewModel.cs PdfEditor/Views/MainWindow.axaml PdfEditor.Tests/Unit/PendingRedactionManagementTests.cs
git commit -m "feat: Add ability to remove pending redactions before applying"
```

---

## Phase 7: Final Integration & Release

### Step 7.1: Run Full Test Suite

**All Tests:**
```bash
# Run all 700+ tests
dotnet test

# Run only new v1.3.0 tests
dotnet test --filter "FullyQualifiedName~(DocumentFileState|FilenameSuggestion|ApplyAll|ContextAwareSave|EndToEnd|TextExtractionAfter|OriginalFileProtection)"

# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverageReportsFormat=html
```

**Success Criteria:**
- All existing tests still pass ✓
- All new tests pass ✓
- No regressions in redaction functionality ✓
- Text extraction issue fixed ✓
- Original file protection works ✓

---

### Step 7.2: Manual Testing Checklist

**Test Scenarios:**

1. **Simple Redaction (90% case)**
   - [ ] Open PDF
   - [ ] Mark 3 areas for redaction
   - [ ] Click "Apply All Redactions"
   - [ ] Dialog shows suggested filename: `filename_REDACTED.pdf`
   - [ ] Click Save
   - [ ] Verify: Original exists and unchanged
   - [ ] Verify: Redacted version exists with black boxes
   - [ ] Verify: Cannot extract text from redacted areas
   - [ ] **Time to complete: < 2 minutes**

2. **Continue Working on Redacted Version**
   - [ ] Open `filename_REDACTED.pdf`
   - [ ] Mark 2 more areas
   - [ ] Click Save (regular save, no dialog)
   - [ ] Verify: File updated
   - [ ] Verify: Original still unchanged

3. **Cannot Overwrite Original**
   - [ ] Open `original.pdf`
   - [ ] Mark area for redaction
   - [ ] Press Ctrl+S
   - [ ] Verify: Save As dialog appears (NOT simple save)
   - [ ] Verify: Cannot overwrite original through normal workflow

4. **Text Extraction Fix**
   - [ ] Open PDF
   - [ ] Mark area with "SECRET" text
   - [ ] Click "Apply All"
   - [ ] Save as `test_REDACTED.pdf`
   - [ ] Try to select/extract text from redacted area
   - [ ] Verify: No text extracted (FIXED!)

5. **Multiple Redaction Sessions**
   - [ ] Open `test.pdf`
   - [ ] Mark 2 areas, apply, save as `test_REDACTED.pdf`
   - [ ] Close and reopen `test_REDACTED.pdf`
   - [ ] Mark 1 more area, apply, save
   - [ ] Verify: All 3 redactions visible
   - [ ] Verify: Original `test.pdf` still unchanged

---

### Step 7.3: Update Documentation

**Files to Update:**

1. **README.md**
   - Add v1.3.0 features section
   - Update workflow screenshots/descriptions
   - Add "How to Save Redacted PDFs" section

2. **CHANGELOG.md** (Create if doesn't exist)
   ```markdown
   ## [1.3.0] - 2025-XX-XX

   ### Added
   - Mark-then-apply redaction workflow (batch redactions)
   - Context-aware Save button (prevents original overwrite)
   - Automatic filename suggestions (`_REDACTED.pdf`)
   - Pending redactions panel with preview
   - Visual distinction: pending (red dashed) vs applied (black solid)
   - Auto-save and reload after applying redactions

   ### Fixed
   - Redacted text no longer extractable before save (auto-save fixes this)
   - Cannot accidentally overwrite original file
   - Clipboard history confusion (shows pending vs applied state)

   ### Changed
   - Redaction workflow: mark multiple areas → review → apply all
   - Save behavior based on file state (original vs redacted version)
   ```

3. **UX_REDESIGN_PROPOSAL.md**
   - Update implementation status
   - Mark Phase 1 as "Completed"

**Git Checkpoint:**
```bash
git add README.md CHANGELOG.md UX_REDESIGN_PROPOSAL.md
git commit -m "docs: Update documentation for v1.3.0 release"
```

---

### Step 7.4: Create Release

**Release Checklist:**

1. **Version Bump**
   - Update version in `.csproj` files to `1.3.0`
   - Update `AssemblyVersion` and `FileVersion`

2. **Build Release Binaries**
   ```bash
   # Linux
   dotnet publish -c Release -r linux-x64 --self-contained true

   # Windows
   dotnet publish -c Release -r win-x64 --self-contained true

   # macOS
   dotnet publish -c Release -r osx-x64 --self-contained true
   dotnet publish -c Release -r osx-arm64 --self-contained true
   ```

3. **Run Final Tests on Release Builds**
   ```bash
   dotnet test -c Release
   ```

4. **Create Git Tag**
   ```bash
   git tag -a v1.3.0 -m "$(cat <<'EOF'
   Release v1.3.0 - Simplified File Operations and Mark-Then-Apply Workflow

   This release implements the UX redesign with focus on simplicity and safety.

   ## Highlights

   ✅ **Mark-Then-Apply Workflow**
   - Mark multiple areas for redaction
   - Review all pending redactions in sidebar
   - Apply all at once with automatic save
   - Visual distinction: red dashed (pending) vs black solid (applied)

   ✅ **Safe File Operations**
   - Context-aware Save button prevents original overwrite
   - Auto-suggests filename_REDACTED.pdf
   - Cannot accidentally destroy original file
   - Original always preserved

   ✅ **Critical Fixes**
   - Redacted text immediately non-extractable (auto-save after apply)
   - No more "redacted but still selectable" confusion
   - Text extraction from redacted areas returns empty

   ## User Experience

   - Simple workflow: < 2 minutes to first redaction
   - Zero user questions needed ("which save option?")
   - Progressive disclosure: simple for 90%, powerful for 10%
   - One obvious button: "Save Redacted Version"

   ## Testing

   - 700+ total tests (all passing)
   - New end-to-end workflow tests
   - Text extraction verification tests
   - Original file protection tests

   ## What's Next

   v1.4.0 will add:
   - Advanced Save menu for power users
   - Collapsible sidebars
   - Dark theme support
   - Keyboard shortcut overlay

   🤖 Generated with [Claude Code](https://claude.com/claude-code)

   Co-Authored-By: Claude <noreply@anthropic.com>
   EOF
   )"
   ```

5. **Push Tag**
   ```bash
   git push && git push --tags
   ```

6. **Create GitHub Release**
   ```bash
   gh release create v1.3.0 \
     --title "v1.3.0 - Simplified File Operations" \
     --notes-file RELEASE_NOTES.md \
     ./releases/PdfEditor-linux-x64.tar.gz \
     ./releases/PdfEditor-windows-x64.zip \
     ./releases/PdfEditor-macos-x64.tar.gz \
     ./releases/PdfEditor-macos-arm64.tar.gz
   ```

---

## Summary of New Tests

### Unit Tests (Est. 30 tests)
- `DocumentFileStateTests.cs` - 4 tests
- `FilenameSuggestionServiceTests.cs` - 6 tests
- `MainWindowViewModelTests.cs` - 10 new tests (added to existing)
- `PendingRedactionTests.cs` - 3 tests
- `PendingRedactionManagementTests.cs` - 5 tests
- `KeyboardShortcutTests.cs` - 2 tests

### Integration Tests (Est. 40 tests)
- `ApplyAllRedactionsTests.cs` - 6 tests
- `ContextAwareSaveTests.cs` - 5 tests
- `EndToEndWorkflowTests.cs` - 5 tests
- `TextExtractionAfterRedactionTests.cs` - 4 tests (CRITICAL)
- `OriginalFileProtectionTests.cs` - 5 tests
- `VisualRedactionIndicatorsTests.cs` - 3 tests

### UI Tests (Est. 10 tests)
- `SaveRedactedVersionDialogTests.cs` - 5 tests
- Additional HeadlessUITests - 5 tests

**Total New Tests: ~80 tests**
**Total Tests After v1.3.0: ~744 tests**

---

## Success Metrics

### Quantitative
- ✅ All 744 tests passing
- ✅ Zero regressions in existing functionality
- ✅ < 2 minutes for first successful redaction (timed test)
- ✅ Zero accidental original overwrites (impossible through UI)

### Qualitative
- ✅ User can complete workflow without reading documentation
- ✅ No confusion about "which save option"
- ✅ No questions about why text is still selectable (auto-save fixes it)
- ✅ Clear understanding of file state (original vs redacted)

---

## Rollback Plan

If critical issues found after release:

1. **Revert to v1.2.0**
   ```bash
   git checkout v1.2.0
   git checkout -b hotfix-revert
   git push
   ```

2. **Document Issues**
   - Create GitHub issues for problems
   - Tag as "v1.3.0 regression"

3. **Fix Forward**
   - Fix issues in new branch
   - Comprehensive testing
   - Release v1.3.1

---

## Timeline Estimate (UPDATED with Refactoring)

| Phase | Tasks | Est. Time | Tests | Notes |
|-------|-------|-----------|-------|-------|
| **Pre-Phase 0** | **Refactoring (Steps 0.1-0.3)** | **7 hours** | **29 tests** | **CRITICAL - Do first** |
| Phase 1 | Foundation (Steps 1.1-1.3) | 2 hours | 6 tests | Simplified by refactoring |
| Phase 2 | Pending Panel (Steps 2.1-2.4) | 5 hours | 12 tests | Easier with managers |
| Phase 3 | Apply All (Steps 3.1-3.2) | 4 hours | 11 tests | Stream extraction done |
| Phase 4 | Context Save (Steps 4.1-4.2) | 3 hours | 10 tests | Simpler with state mgr |
| Phase 5 | Integration Tests (Steps 5.1-5.3) | 4 hours | 19 tests | Same |
| Phase 6 | Polish (Steps 6.1-6.3) | 3 hours | 8 tests | Simpler |
| Phase 7 | Release (Steps 7.1-7.4) | 4 hours | Manual | Same |
| **Total** | **8 phases, 25 steps** | **32 hours** | **~95 tests** | **+15 tests, same time!** |

**Breakdown:**
- Pre-Phase 0 Refactoring: 7 hours (29 tests)
- Phases 1-7 Implementation: 25 hours (66 tests)
- **Total: 32 hours, 95 tests**

**Key Insight:** Refactoring adds 7 hours but saves 7 hours in subsequent phases = net zero time impact, but with:
- ✅ Much lower risk
- ✅ Root cause fixes
- ✅ Better code quality
- ✅ 15 additional tests

**Estimated Development Time: 4-5 days of focused work**

---

## Notes

- Commit frequently (after each step)
- Run tests after each change
- Don't move to next step until tests pass
- Document any deviations from plan
- Update this plan if scope changes
- Keep v1.2.0 branch available for comparison

---

**This implementation plan is ready to execute. Each step is small, testable, and builds on the previous step.**
