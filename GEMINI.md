# GEMINI.md

This file provides guidance to Google Gemini when working with this repository.

## Primary Documentation

**See `AGENT.md` for complete project documentation**, including:
- Project overview and architecture
- Build and run commands
- Redaction engine details
- Testing infrastructure
- Development workflows
- GitHub issue management

## Gemini-Specific Notes

### CRITICAL: Flash Model Code Editing Restriction

**Gemini Flash models (1.5 Flash, 2.0 Flash, etc.) must NOT write or edit code in this repository.**

This codebase contains security-critical redaction logic that requires careful, accurate code changes. Flash models have produced unreliable code edits that could compromise TRUE redaction.

### If You Are a Flash Model

**What you CAN do:**
- Read and analyze code
- Answer questions about architecture and implementation
- Explain how features work
- Help debug by analyzing logs and error messages
- Search for files and patterns
- Summarize code structure

**What you MUST NOT do:**
- Write new code
- Edit existing code
- Create or modify files
- Implement features or fixes

**Instead:** When code changes are needed, clearly describe:
1. Which file(s) need to change
2. What the change should be (in plain English)
3. Why the change is needed

Then tell the user: *"I'm a Flash model and cannot reliably edit code in this codebase. Please use a Pro/Ultra model or make these changes manually."*

### If You Are a Pro or Ultra Model

You may perform code edits following all guidelines in `AGENT.md` and `REDACTION_AI_GUIDELINES.md`.

### Why This Restriction Exists

This PDF editor implements TRUE glyph-level redaction (text is removed from PDF structure, not just visually covered). Incorrect code changes could:
- Downgrade to visual-only redaction (security vulnerability)
- Break coordinate conversion (wrong content redacted)
- Corrupt PDF structure

The complexity of the redaction engine requires models with strong reasoning capabilities.
