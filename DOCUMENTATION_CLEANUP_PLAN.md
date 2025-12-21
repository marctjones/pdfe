# Documentation Cleanup Plan

Now that all v1.3.0 implementation work is tracked in GitHub Issues, we can safely delete obsolete planning documents.

## ✅ Safe to Delete

These documents have been fully migrated to GitHub Issues:

### 1. `IMPLEMENTATION_PLAN_V1.3.0.md` (1223 lines)
- **Reason**: All 24 implementation steps now tracked in GitHub issues #37-#47
- **Replacement**: See `IMPLEMENTATION_PLAN_COVERAGE.md` for mapping
- **Content preserved in**: GitHub Issues with full acceptance criteria and test plans

### 2. `REFACTORING_PLAN_V1.3.0.md`
- **Reason**: Pre-Phase 0 refactoring already completed
- **Status**: DocumentStateManager, RedactionWorkflowManager, stream extraction all done
- **Notes**: Historical interest only

### 3. `REDACTION_UX_ISSUES.md`
- **Reason**: Issues documented here now tracked as GitHub issues
- **Example**: "Text still selectable after redaction" → tracked in #42, #43
- **Replacement**: GitHub Issues with labels

### 4. `FILE_OPERATIONS_UX.md`
- **Reason**: UX proposals now implemented or tracked in issues
- **Replacement**: Issues #40, #41 (context-aware Save)

### 5. `UX_REDESIGN_PROPOSAL.md`
- **Reason**: Proposal converted to implementation plan, now in issues
- **Replacement**: GitHub Issues #37-#47 cover the full UX redesign

### 6. `DEBUG_REDACTION_MODE.md`
- **Reason**: Debug notes for old issues, likely resolved
- **Action**: Review first, then delete if obsolete

### 7. `TESTING_DEBUG_MODE.md`
- **Reason**: Temporary debug documentation
- **Action**: Review first, then delete if obsolete

### 8. `CRASH_DEBUG_INSTRUCTIONS.md`
- **Reason**: One-time crash investigation
- **Action**: Delete if crash resolved

## 🔧 Keep and Update

These documents should be kept but may need updates:

### 1. `README.md` ⭐ KEEP
- **Status**: Active user documentation
- **Update needed**: Add v1.3.0 features (tracked in Issue #46)

### 2. `CLAUDE.md` ⭐ KEEP
- **Status**: AI assistant instructions
- **Current**: Already updated with GitHub Issues workflow

### 3. `ARCHITECTURE_DIAGRAM.md` ⭐ KEEP
- **Status**: Visual architecture reference
- **Notes**: May need update after v1.3.0 implementation

### 4. `REDACTION_ENGINE.md` ⭐ KEEP
- **Status**: Technical deep dive on redaction implementation
- **Update needed**: Remove "Future Enhancements" list, point to GitHub Issues

### 5. `REDACTION_AI_GUIDELINES.md` ⭐ KEEP
- **Status**: Critical safety guidelines for AI assistants
- **Notes**: Must maintain for redaction integrity

### 6. `TESTING_GUIDE.md` ⭐ KEEP
- **Status**: Test infrastructure documentation
- **Notes**: Keep updated as testing strategy evolves

### 7. `LICENSES.md` ⭐ KEEP
- **Status**: Legal compliance documentation
- **Notes**: Required for license compliance

### 8. `IMPLEMENTATION_PLAN_COVERAGE.md` ⭐ KEEP
- **Status**: Historical mapping of plan → issues
- **Reason**: Shows verification that plan was fully migrated
- **Use**: Reference for understanding how issues relate to original plan

### 9. `GITHUB_ISSUES.md` ⭐ KEEP (or delete)
- **Status**: Human-readable backlog snapshot
- **Decision**: Keep if useful for batch review, delete if redundant with GitHub

## 📋 Recommended Actions

### Step 1: Delete Obsolete Planning Docs
```bash
git rm IMPLEMENTATION_PLAN_V1.3.0.md
git rm REFACTORING_PLAN_V1.3.0.md
git rm REDACTION_UX_ISSUES.md
git rm FILE_OPERATIONS_UX.md
git rm UX_REDESIGN_PROPOSAL.md
git commit -m "docs: Remove obsolete planning docs (all work tracked in GitHub Issues)"
```

### Step 2: Review and Delete Debug Docs (if obsolete)
```bash
# Review these first to see if still needed:
cat DEBUG_REDACTION_MODE.md
cat TESTING_DEBUG_MODE.md
cat CRASH_DEBUG_INSTRUCTIONS.md

# If obsolete:
git rm DEBUG_REDACTION_MODE.md
git rm TESTING_DEBUG_MODE.md
git rm CRASH_DEBUG_INSTRUCTIONS.md
git commit -m "docs: Remove obsolete debug documentation"
```

### Step 3: Update REDACTION_ENGINE.md
Remove "Future Enhancements" section, replace with:
```markdown
## Future Enhancements

See GitHub Issues with label `component: redaction-engine` for planned enhancements.

Current priorities:
- Issue #19: Apply All Redactions workflow
- Issue #38: Visual distinction for pending/applied redactions
- Issue #42: Comprehensive text extraction verification
```

### Step 4: Update README.md
Tracked in Issue #46 - will add v1.3.0 features section.

### Step 5: Optional - Delete GITHUB_ISSUES.md
If you prefer using `gh issue list` instead of markdown snapshot:
```bash
git rm GITHUB_ISSUES.md
git commit -m "docs: Remove static issue snapshot (use 'gh issue list' instead)"
```

## Summary

**Can delete immediately**: 5 files (implementation plans, UX proposals)
**Review then delete**: 3 files (debug docs)
**Keep**: 8-9 files (active documentation)

This cleanup will:
- ✅ Reduce documentation duplication
- ✅ Establish GitHub Issues as single source of truth
- ✅ Keep essential technical documentation
- ✅ Maintain historical mapping (IMPLEMENTATION_PLAN_COVERAGE.md)

**Total reduction**: ~2500 lines of obsolete planning documentation
