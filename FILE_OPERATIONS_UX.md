# File Operations UX - Simplified Design

## Design Principle

**"Make the right thing easy, make everything else possible."**

- 90% of users get one obvious button that does the right thing
- 10% of power users can access advanced options if needed
- No analysis paralysis, no confusing choices
- Progressive disclosure: simplicity first, complexity hidden

## Core User Workflows

### Workflow 1: Simple Redaction (90% of cases)

```
User opens "contract.pdf"
User marks 3 areas for redaction
User clicks [Save Redacted Version]
Dialog appears with "contract_REDACTED.pdf"
User clicks [Save]
Done. ✓
```

**No decisions. No checkboxes. No confusion.**

### Workflow 2: Continue Working on Redacted File

```
User opens "contract_REDACTED.pdf"
User marks 2 more areas
User clicks [Save] (regular save, no dialog)
Done. ✓
```

**Already a redacted version, safe to update it.**

### Workflow 3: Power User Needs Control (10% of cases)

```
User opens "contract.pdf"
User marks areas, removes pages, rotates page
User clicks [Advanced Save...]
User sees checkboxes for what to include
User makes custom choices
User clicks [Save]
Done. ✓
```

**Available when needed, hidden when not.**

## File Menu Design

### When Working on Original File (No Changes)

```
File
├─ Open...                          Ctrl+O
├─ Save                             [GRAYED OUT - no changes to save]
├─ Save As...                       Ctrl+Shift+S
├─ ────────────────────────────────────────
├─ Export Current Page as Image...
└─ Exit
```

### When Working on Original File (With Redactions)

```
File
├─ Open...                          Ctrl+O
├─ Save Redacted Version            Ctrl+S        ← ONE OBVIOUS CHOICE
├─ ────────────────────────────────────────
├─ Advanced Save...                              ← For power users
├─ Export Current Page as Image...
└─ Exit
```

### When Working on Redacted Version File

```
File
├─ Open...                          Ctrl+O
├─ Save                             Ctrl+S        ← Simple save, updates file
├─ Save As...                       Ctrl+Shift+S  ← Create another copy
├─ ────────────────────────────────────────
├─ Export Current Page as Image...
└─ Exit
```

### Advanced Save Menu (Power Users Only)

```
Advanced Save...
├─ Save Copy of Original (no changes)
├─ Save Custom Version...
│   └─ [Dialog with checkboxes for what to include]
└─ Export to PDF/A...
```

## Dialog Designs

### Dialog 1: Save Redacted Version (Simple - Default)

Appears when user has redactions and clicks "Save Redacted Version"

```
┌─────────────────────────────────────────────────┐
│  Save Redacted Version                     [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  Save as:                                       │
│  [contract_REDACTED.pdf              ] [Browse] │
│                                                 │
│  3 areas will be permanently redacted           │
│  Your original file will be preserved ✓         │
│                                                 │
│  [Cancel]                            [Save]     │
└─────────────────────────────────────────────────┘
```

**No checkboxes. No choices. Just works.**

### Dialog 2: Save with Multiple Change Types

Appears when user has redactions + page changes

```
┌─────────────────────────────────────────────────┐
│  Save Modified Version                     [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  Save as:                                       │
│  [contract_modified.pdf              ] [Browse] │
│                                                 │
│  Changes:                                       │
│  • 3 areas redacted                             │
│  • 5 pages removed                              │
│                                                 │
│  Your original file will be preserved ✓         │
│                                                 │
│  [Redactions Only]  [Cancel]  [Save All Changes]│
└─────────────────────────────────────────────────┘
```

**Two clear choices when user has multiple change types.**

### Dialog 3: Update Existing Redacted File

Appears when user adds more redactions to already-redacted file

```
┌─────────────────────────────────────────────────┐
│  Save Changes                              [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  Update: contract_REDACTED.pdf                  │
│                                                 │
│  2 additional areas will be redacted            │
│                                                 │
│  Original file unchanged: contract.pdf ✓        │
│                                                 │
│  [Cancel]                            [Save]     │
└─────────────────────────────────────────────────┘
```

**Simple confirmation, user knows what's happening.**

### Dialog 4: Advanced Save (Power Users)

Appears when user clicks "Advanced Save..."

```
┌─────────────────────────────────────────────────┐
│  Advanced Save                             [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  Save as:                                       │
│  [contract_custom.pdf                ] [Browse] │
│                                                 │
│  Include in saved file:                         │
│  ☑ Redactions (3 areas)                         │
│  ☑ Page removals (5 pages)                      │
│  ☑ Page rotations (1 page)                      │
│                                                 │
│  Result: 15 pages with redactions               │
│                                                 │
│  ☑ Keep original file unchanged                 │
│                                                 │
│  [Cancel]                            [Save]     │
└─────────────────────────────────────────────────┘
```

**All the control, but only when explicitly requested.**

## Warning Dialogs

### Warning 1: Trying to Save Original with Ctrl+S

When user presses Ctrl+S while editing original file:

```
┌─────────────────────────────────────────────────┐
│  Save Redacted Version                     [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  Save as:                                       │
│  [contract_REDACTED.pdf              ] [Browse] │
│                                                 │
│  3 areas will be permanently redacted           │
│  Your original file will be preserved ✓         │
│                                                 │
│  [Cancel]                            [Save]     │
└─────────────────────────────────────────────────┘
```

**Don't show a scary warning. Just show the save dialog.**
**Users understand: They pressed Save, they get save dialog.**

### Warning 2: Closing with Unsaved Changes

```
┌─────────────────────────────────────────────────┐
│  Unsaved Changes                           [×]  │
├─────────────────────────────────────────────────┤
│                                                 │
│  You have 3 redactions that haven't been saved. │
│                                                 │
│  [Discard Changes]  [Cancel]  [Save Redactions] │
└─────────────────────────────────────────────────┘
```

**Simple. Clear options. No complexity.**

## Filename Suggestions

### Auto-Generated Filenames

```
Original: contract.pdf

→ After redactions:
  contract_REDACTED.pdf

→ After page removal (pages 1-5 kept):
  contract_pages_1-5.pdf

→ After both:
  contract_pages_1-5_REDACTED.pdf

→ Multiple saves same day:
  contract_REDACTED.pdf
  contract_REDACTED_2.pdf
  contract_REDACTED_3.pdf
```

**Simple, predictable naming.**

### Algorithm

```csharp
private string SuggestFilename()
{
    var name = Path.GetFileNameWithoutExtension(_originalFilePath);
    var ext = Path.GetExtension(_originalFilePath);
    var dir = Path.GetDirectoryName(_currentFilePath);

    var suffix = "";

    // Add page info if significantly reduced
    if (HasRemovedPages() && RemovedPagesCount > 2)
    {
        suffix += "_pages_" + GetPageRangeString();
    }

    // Add REDACTED if has redactions
    if (HasRedactions())
    {
        suffix += "_REDACTED";
    }

    // Build filename
    var baseName = name + suffix + ext;
    var fullPath = Path.Combine(dir, baseName);

    // If exists, add number
    if (File.Exists(fullPath))
    {
        int counter = 2;
        while (File.Exists(Path.Combine(dir, $"{name}{suffix}_{counter}{ext}")))
            counter++;
        fullPath = Path.Combine(dir, $"{name}{suffix}_{counter}{ext}");
    }

    return fullPath;
}
```

## Status Bar

### Keep It Simple and Unobtrusive

```
Normal:
┌──────────────────────────────────────────────────────────┐
│ contract.pdf | Page 1 of 20 | 100%                       │
└──────────────────────────────────────────────────────────┘

After marking redactions:
┌──────────────────────────────────────────────────────────┐
│ contract.pdf | 3 areas marked | Page 1 of 20             │
└──────────────────────────────────────────────────────────┘

After saving redacted version:
┌──────────────────────────────────────────────────────────┐
│ contract_REDACTED.pdf | Page 1 of 20 | Saved             │
└──────────────────────────────────────────────────────────┘
```

**No alarm bells. No red warnings. Just gentle information.**

## File State Tracking

### Implementation

```csharp
public class DocumentFileState
{
    // Simple state flags
    public string CurrentFilePath { get; set; }
    public string OriginalFilePath { get; set; }

    public bool IsOriginalFile =>
        CurrentFilePath == OriginalFilePath;

    public bool IsRedactedVersion =>
        Path.GetFileName(CurrentFilePath).Contains("_REDACTED",
            StringComparison.OrdinalIgnoreCase);

    // Change tracking
    public int PendingRedactionsCount { get; set; }
    public int RemovedPagesCount { get; set; }

    public bool HasUnsavedChanges =>
        PendingRedactionsCount > 0 || RemovedPagesCount > 0;

    // What to show in UI
    public string GetSaveButtonText()
    {
        if (IsOriginalFile && HasUnsavedChanges)
            return "Save Redacted Version";
        if (IsRedactedVersion && HasUnsavedChanges)
            return "Save";
        return "Save"; // Grayed out if no changes
    }

    public string GetStatusText()
    {
        if (PendingRedactionsCount > 0)
            return $"{PendingRedactionsCount} areas marked";
        return "Ready";
    }
}
```

## Keyboard Shortcuts

```
Ctrl+O          Open file
Ctrl+S          Save (smart behavior based on state)
Ctrl+Shift+S    Save As... (standard Windows convention)
Ctrl+W          Close document
Ctrl+Q          Exit application
```

**Ctrl+S behavior:**
- Original file + changes → Show "Save Redacted Version" dialog
- Redacted file + changes → Save directly (no dialog)
- No changes → Do nothing (button grayed out)

## Progressive Disclosure Examples

### Example 1: Simple Case (Shown by Default)

```
[Save Redacted Version]

↓ User clicks

┌─────────────────────────────────────┐
│ Save as: contract_REDACTED.pdf      │
│ [Cancel]  [Save]                    │
└─────────────────────────────────────┘
```

### Example 2: Complex Case (User Expands)

```
[Save Changes]

↓ User clicks

┌─────────────────────────────────────┐
│ Save as: contract_modified.pdf      │
│ Changes: 3 redactions, 5 pages      │
│ [Redactions Only] [Save All]        │
└─────────────────────────────────────┘

User can click "Redactions Only" for simple case,
or "Save All" to include everything.
```

### Example 3: Power User Mode (User Opts In)

```
[Advanced Save...]

↓ User clicks

┌─────────────────────────────────────┐
│ Save as: contract_custom.pdf        │
│                                     │
│ Include:                            │
│ ☑ Redactions (3)                    │
│ ☑ Page removals (5)                 │
│ ☑ Rotations (1)                     │
│                                     │
│ [Cancel]  [Save]                    │
└─────────────────────────────────────┘
```

## Error Prevention

### Prevent Accidental Original Overwrite

```csharp
private async Task SaveFileAsync()
{
    if (IsOriginalFile && HasUnsavedChanges)
    {
        // NEVER allow Ctrl+S to overwrite original
        // Always show "Save As" dialog
        await ShowSaveRedactedVersionDialog();
    }
    else if (IsRedactedVersion && HasUnsavedChanges)
    {
        // Safe to save directly
        await SaveToCurrentPath();
    }
    else
    {
        // No changes, do nothing
    }
}
```

### File Exists Handling

```
If suggested filename already exists:

Option A (Simple):
  Auto-increment: contract_REDACTED_2.pdf

Option B (Ask):
  ┌─────────────────────────────────────┐
  │ File exists: contract_REDACTED.pdf  │
  │ [Overwrite] [Save as New]           │
  └─────────────────────────────────────┘

Recommendation: Option A (auto-increment)
Less friction, prevents mistakes.
```

## Integration with Mark-Then-Apply Workflow

### Combined Workflow

```
1. User marks areas (visual only, no changes yet)
   Status: "3 areas marked"

2. User clicks "Apply All Redactions"
   Dialog appears:

   ┌──────────────────────────────────────┐
   │ Apply and Save Redactions            │
   ├──────────────────────────────────────┤
   │ Save as:                             │
   │ [contract_REDACTED.pdf    ] [Browse] │
   │                                      │
   │ 3 areas will be redacted             │
   │                                      │
   │ [Cancel]        [Apply and Save]     │
   └──────────────────────────────────────┘

3. User clicks "Apply and Save"
   - Redactions applied to in-memory document
   - Document saved to new file
   - Document reloaded from saved file
   - Status: "contract_REDACTED.pdf | Saved"

4. Done. No in-between state. ✓
```

**One button. One dialog. Done.**

## Testing Criteria

### Success Metrics

A user who has never used the app should be able to:

1. **Open a PDF** (< 5 seconds to find button)
2. **Mark area for redaction** (< 10 seconds to understand)
3. **Save redacted version** (< 5 seconds, zero confusion)
4. **Verify original is unchanged** (obvious from filename)

**Total time to first successful redaction: < 2 minutes**

**Number of questions asked: 0**

### Failure Indicators

- User asks "Which save option do I need?"
- User overwrites original file by accident
- User doesn't understand what "_REDACTED" means
- User is confused by dialog options
- User can't find the save button

### A/B Testing

**Test A (Complex):**
- Menu: 4 save options
- Dialog: Checkboxes and options
- Result: Measure time to completion, confusion

**Test B (Simple):**
- Menu: 1 save button
- Dialog: Filename + Save button
- Result: Measure time to completion, confusion

**Hypothesis: Test B will be 3x faster with 90% fewer questions.**

## Documentation (User Manual)

### Simple Instructions

```markdown
## How to Save a Redacted PDF

1. Mark areas you want to redact
2. Click "Save Redacted Version"
3. Choose where to save (default: adds "_REDACTED" to filename)
4. Click Save

Your original file is never modified.
```

**That's it. No need to explain options, choices, or complexity.**

### Advanced Instructions (Separate Section)

```markdown
## Advanced Save Options

For power users who need fine control:

1. Click "Advanced Save..." in File menu
2. Choose what to include:
   - Redactions
   - Page removals
   - Page rotations
3. Enter custom filename
4. Click Save

This is rarely needed. Most users can use "Save Redacted Version".
```

## Comparison: Before and After

### BEFORE (Complex)

```
File → Save As ▶
  ├─ Save Copy of Original...
  ├─ Save Redacted Version...
  ├─ Save Modified Pages...
  └─ Save With All Changes...

Dialog:
  ┌────────────────────────────────┐
  │ What to include:               │
  │ ☐ Redactions (3)               │
  │ ☐ Page removals (5)            │
  │ ☐ Rotations (1)                │
  │                                │
  │ Result: 15 pages with...       │
  │ Warning: Removed pages...      │
  │                                │
  │ [Cancel] [Save]                │
  └────────────────────────────────┘
```

**User reaction: "Huh? Which one?"**

### AFTER (Simple)

```
File → Save Redacted Version

Dialog:
  ┌────────────────────────────────┐
  │ Save as:                       │
  │ [contract_REDACTED.pdf]        │
  │                                │
  │ [Cancel] [Save]                │
  └────────────────────────────────┘
```

**User reaction: "Oh, that's easy." [Clicks Save]**

## Implementation Priority

### Phase 1: Core Simplicity (v1.3.0)
- ✓ Single "Save Redacted Version" button
- ✓ Simple dialog with filename suggestion
- ✓ Prevent original overwrite
- ✓ Auto-increment for duplicate names

### Phase 2: Polish (v1.4.0)
- ✓ Smart status bar messages
- ✓ File state tracking
- ✓ Keyboard shortcuts
- ✓ Better error messages

### Phase 3: Power User Features (v1.5.0)
- ✓ Advanced Save menu
- ✓ Custom save options
- ✓ Checkboxes for what to include
- ✓ Export presets

## Key Takeaways

1. **One obvious button** beats five choices
2. **Smart defaults** beat user decisions
3. **Progressive disclosure** beats showing everything
4. **Simple dialog** beats comprehensive dialog
5. **Trust users** to understand "_REDACTED"
6. **Hide complexity** until explicitly needed
7. **Test with real users** before adding options

## The Golden Rule

**"If a user has to read the manual to save a file, we failed."**

The save button should be so obvious that:
- No tooltip needed
- No help text needed
- No tutorial needed
- Just click and it works

That's the goal.
