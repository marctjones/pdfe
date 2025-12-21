# Implementation Plan v1.3.0 Coverage Verification

This document maps all steps from IMPLEMENTATION_PLAN_V1.3.0.md to GitHub issues.

## Pre-Phase 0: Critical Refactoring

### Step 0.1: Extract DocumentStateManager (2 hours, 12 tests)
- **Status**: ✅ COMPLETED
- **Tracking**: Not in GitHub issues (already completed)
- **Files**: `PdfEditor/ViewModels/DocumentStateManager.cs` exists
- **Notes**: Part of earlier implementation work

### Step 0.2: Add Stream-Based Text Extraction (3 hours, 7 tests)
- **Status**: ✅ COMPLETED
- **Tracking**: Not in GitHub issues (already completed)
- **Files**: Stream extraction implemented
- **Notes**: Fixes "text still selectable after redaction" issue

### Step 0.3: Extract RedactionWorkflowManager (2 hours, 10 tests)
- **Status**: ✅ COMPLETED (but UI display broken)
- **Tracking**: Issue #18 - Test pending redactions UI display
- **Files**: `PdfEditor/ViewModels/RedactionWorkflowManager.cs` exists
- **Notes**: Backend exists, UI binding needs testing

---

## Phase 1: Foundation & File State Tracking

### Step 1.1: Wire Up DocumentStateManager
- **Status**: ✅ COMPLETED
- **Tracking**: Not in GitHub issues (already wired up)
- **Notes**: Already integrated into MainWindowViewModel

### Step 1.2: Add Filename Suggestion Service
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Service to suggest `filename_REDACTED.pdf` naming

### Step 1.3: Wire Up RedactionWorkflowManager
- **Status**: ⚠️ PARTIALLY COMPLETE
- **Tracking**: Issue #18 - Test pending redactions UI display
- **Notes**: Wired up but needs UI testing

---

## Phase 2: Pending Redactions Panel

### Step 2.1: Create PendingRedaction Model
- **Status**: ✅ COMPLETED
- **Tracking**: Not in GitHub issues
- **Files**: `PdfEditor/Models/PendingRedaction.cs` exists
- **Notes**: Model already created

### Step 2.2: Modify Redaction Mode to Mark Instead of Apply
- **Status**: ⚠️ PARTIALLY COMPLETE
- **Tracking**: Issue #19 - Implement "Apply All Redactions" button and workflow
- **Notes**: Mark functionality exists, needs "Apply All" to complete workflow

### Step 2.3: Add Pending Redactions UI Panel
- **Status**: ⚠️ PARTIALLY COMPLETE
- **Tracking**: Issue #18 - Test pending redactions UI display
- **Notes**: UI panel exists in MainWindow.axaml, binding issues need testing

### Step 2.4: Implement Visual Distinction (Pending vs Applied)
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Red dashed border for pending, black solid for applied

---

## Phase 3: Apply All Redactions with Auto-Save

### Step 3.1: Implement Apply All Redactions Command
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: Issue #19 - Implement "Apply All Redactions" button and workflow (CRITICAL)
- **Notes**: Core feature of v1.3.0

### Step 3.2: Add Simple Save Redacted Version Dialog
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE (related to #19)
- **Action Required**: CREATE NEW ISSUE or expand #19
- **Details**: Dialog to confirm filename for redacted version

---

## Phase 4: Context-Aware Save Button

### Step 4.1: Add Dynamic Save Button Text
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: "Save" vs "Save Redacted Version" based on file state

### Step 4.2: Implement Context-Aware Save Command
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Prevent original overwrite, force Save As dialog

---

## Phase 5: Integration Testing

### Step 5.1: End-to-End Workflow Tests
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: Issue #28 - Add integration test for complete mark-then-apply workflow (HIGH)
- **Notes**: Comprehensive test coverage needed

### Step 5.2: Text Extraction After Redaction Tests
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE (related to #28)
- **Action Required**: CREATE NEW ISSUE or expand #28
- **Details**: Verify text extraction fix

### Step 5.3: Original File Protection Tests
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE (related to #28)
- **Action Required**: CREATE NEW ISSUE or expand #28
- **Details**: Test that original cannot be overwritten

---

## Phase 6: Polish & User Experience

### Step 6.1: Add Status Bar Updates
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Show pending count, file type in status bar

### Step 6.2: Add Keyboard Shortcuts
- **Status**: ⚠️ PARTIALLY COMPLETE
- **Tracking**: Issue #24 - Add keyboard shortcut documentation to README
- **Notes**: Some shortcuts exist, needs documentation and Ctrl+Enter for Apply All

### Step 6.3: Add Remove Pending Redaction
- **Status**: ❌ NOT IMPLEMENTED
- **Tracking**: Issue #31 - Add "Clear All" button for pending redactions
- **Notes**: Individual remove + clear all needed

---

## Phase 7: Final Integration & Release

### Step 7.1: Run Full Test Suite
- **Status**: ⚠️ ONGOING
- **Tracking**: Issue #30 - Verify PDF 1.7 and PDF 2.0 conformance
- **Notes**: Test suite exists, conformance verification needed

### Step 7.2: Manual Testing Checklist
- **Status**: ❌ NOT STARTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Manual testing scenarios for v1.3.0 release

### Step 7.3: Update Documentation
- **Status**: ❌ NOT STARTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Update README, CHANGELOG for v1.3.0

### Step 7.4: Create Release
- **Status**: ❌ NOT STARTED
- **Tracking**: ❌ NO GITHUB ISSUE
- **Action Required**: CREATE NEW ISSUE
- **Details**: Version bump, build binaries, create tag/release

---

## Summary

### Completion Status by Phase

| Phase | Steps | Completed | Partial | Not Started | Tracked in Issues |
|-------|-------|-----------|---------|-------------|-------------------|
| Pre-Phase 0 | 3 | 2 | 1 | 0 | 1/3 (33%) |
| Phase 1 | 3 | 2 | 1 | 0 | 1/3 (33%) |
| Phase 2 | 4 | 1 | 2 | 1 | 2/4 (50%) |
| Phase 3 | 2 | 0 | 0 | 2 | 1/2 (50%) |
| Phase 4 | 2 | 0 | 0 | 2 | 0/2 (0%) |
| Phase 5 | 3 | 0 | 0 | 3 | 1/3 (33%) |
| Phase 6 | 3 | 0 | 1 | 2 | 2/3 (67%) |
| Phase 7 | 4 | 0 | 1 | 3 | 1/4 (25%) |
| **TOTAL** | **24** | **5** | **6** | **13** | **9/24 (38%)** |

### GitHub Issues Created (✅ COMPLETE)

All missing issues have been created:

1. **Phase 1.2**: Issue #37 - Add Filename Suggestion Service
2. **Phase 2.4**: Issue #38 - Visual distinction (pending vs applied redactions)
3. **Phase 3.2**: Issue #39 - Save Redacted Version dialog
4. **Phase 4.1**: Issue #40 - Dynamic Save button text
5. **Phase 4.2**: Issue #41 - Context-aware Save command ⚠️ **CRITICAL**
6. **Phase 5.2**: Issue #42 - Text extraction verification tests
7. **Phase 5.3**: Issue #43 - Original file protection tests
8. **Phase 6.1**: Issue #44 - Status bar updates
9. **Phase 7.2**: Issue #45 - Manual testing checklist
10. **Phase 7.3**: Issue #46 - Update documentation for v1.3.0
11. **Phase 7.4**: Issue #47 - Create v1.3.0 release

### Existing Issues That Map to Plan

- **Issue #17**: Test Recent Files menu (not in plan, but related to file management)
- **Issue #18**: Test pending redactions UI display → Phase 1.3, Phase 2.3
- **Issue #19**: Implement "Apply All Redactions" → Phase 2.2, Phase 3.1 ⭐ CRITICAL
- **Issue #20-21**: Debug logging cleanup (not in plan)
- **Issue #22**: Recent Files display improvements (not in plan)
- **Issue #23**: Save window position (not in plan)
- **Issue #24**: Keyboard shortcut documentation → Phase 6.2
- **Issue #25**: Handle deleted files in Recent Files (not in plan)
- **Issue #26-27**: Optimization and tests (not in plan)
- **Issue #28**: Integration test for mark-then-apply → Phase 5.1 ⭐ HIGH PRIORITY
- **Issue #29**: Fix CS8618 warnings (not in plan)
- **Issue #30**: PDF conformance verification → Phase 7.1
- **Issue #31**: "Clear All" button → Phase 6.3
- **Issue #32-36**: Various enhancements (not in plan)

### Gaps Identified

**MAJOR GAPS (not tracked in GitHub issues):**
1. ❌ Filename suggestion service (Phase 1.2)
2. ❌ Visual distinction for pending/applied redactions (Phase 2.4)
3. ❌ Save Redacted Version dialog (Phase 3.2)
4. ❌ Context-aware Save button (Phase 4.1, 4.2) - **CRITICAL FOR SAFETY**
5. ❌ Comprehensive integration tests (Phase 5.2, 5.3)
6. ❌ Status bar updates (Phase 6.1)
7. ❌ Release process (Phase 7.2, 7.3, 7.4)

**STATUS: ✅ COMPLETE**

The implementation plan is **NOW FULLY TRACKED** in GitHub issues. All 11 missing issues have been created (#37-#47).

**COMPLETED ACTIONS:**
1. ✅ Created 11 new GitHub issues for all missing implementation steps
2. ✅ Duplicate issues #14-16 already closed
3. ✅ All v1.3.0 work now tracked in GitHub Issues

**NEXT STEPS:**
1. **Safe to delete**: `IMPLEMENTATION_PLAN_V1.3.0.md` (all content tracked in issues)
2. **Safe to delete**: `REFACTORING_PLAN_V1.3.0.md` (refactoring already complete)
3. **Keep**: `IMPLEMENTATION_PLAN_COVERAGE.md` (this file - shows mapping)
4. Use GitHub Issues as single source of truth for v1.3.0 work
