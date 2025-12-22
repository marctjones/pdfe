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

###   IMPORTANT: Model Restrictions for Code Editing

**DO NOT perform code edits when running as Gemini Flash 2.5 or older Flash models.**

Flash models do not produce reliable code edits for this codebase. If you are running as a Flash model:
1. **DO** research, read files, answer questions about the code
2. **DO** explain architecture, debug issues, suggest approaches
3. **DO NOT** write or edit code directly
4. **INSTEAD** describe the changes needed and let the user (or a Pro/Ultra model) implement them

This restriction applies to:
- Gemini 1.5 Flash
- Gemini 2.0 Flash
- Any future Flash-tier models

Pro and Ultra models may perform code edits following all guidelines in `AGENT.md` and `REDACTION_AI_GUIDELINES.md`.
