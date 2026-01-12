---
name: idlergear
description: |
  Knowledge management for AI-assisted development. Use this skill when:
  - Starting a new session or resuming work
  - User asks about tasks, bugs, notes, or project status
  - User mentions: "what's next", "where did we leave off", "TODO", "track this"
  - Creating, updating, or closing tasks
  - Capturing notes, ideas, or research findings
  - Checking project vision or plans
  - User asks about project context or goals
  - Coordinating with other AI agents on the same codebase
  - User mentions: "save this", "remember", "note", "task", "bug", "idea"
---

# IdlerGear Knowledge Management

IdlerGear provides structured knowledge persistence across AI sessions.

## Session Start (MANDATORY)

**Call this MCP tool at the start of EVERY session:**

```
idlergear_session_start()
```

This returns:
- Project vision and goals
- Current plan and open tasks
- Recent notes and session state
- Recommendations for what to work on

## Quick Reference

### Creating Knowledge

| Action | MCP Tool |
|--------|----------|
| Create task | `idlergear_task_create(title="...", labels=["bug"])` |
| Quick note | `idlergear_note_create(content="...", tags=["idea"])` |
| Research | `idlergear_note_create(content="...", tags=["explore"])` |
| Documentation | `idlergear_reference_add(title="...", body="...")` |

### Retrieving Knowledge

| Action | MCP Tool |
|--------|----------|
| List tasks | `idlergear_task_list(state="open")` |
| Search all | `idlergear_search(query="...")` |
| Show vision | `idlergear_vision_show()` |
| Project status | `idlergear_status()` |

### Task Labels

- `bug` - Something broken
- `enhancement` - New feature
- `tech-debt` - Code to improve later
- `decision` - Architectural choice made

### Note Tags

- `explore` - Research questions
- `idea` - Future possibilities
- `bug` - Bug observations

## Forbidden Actions

**DO NOT create files:**
- `TODO.md`, `NOTES.md`, `SESSION_*.md`, `SCRATCH.md`

**DO NOT write comments:**
- `// TODO:`, `# FIXME:`, `/* HACK: */`

**INSTEAD:** Use `idlergear_task_create()` or `idlergear_note_create()`

## Session End

Before ending a session, consider:
```
idlergear_session_end(notes="what was accomplished")
```

This saves state for the next session.

## Health Check (Doctor)

To check if IdlerGear is properly configured and up-to-date:
```
idlergear_doctor()
```

This checks:
- Configuration health (version, initialization)
- File installation status (MCP, hooks, rules, skills)
- Legacy files from older versions
- Unmanaged knowledge files (TODO.md, NOTES.md)

To auto-fix issues:
```
idlergear_doctor(fix=True)
```

## Sudo Handling

When a command requires sudo, IdlerGear provides assistance:

### Pre-authentication (Preferred)
If a GUI prompt isn't available, ask the user to pre-authenticate:
```
"Please run 'sudo -v' in another terminal, then I'll run the command."
```

### GUI Password Prompt (Automatic)
If zenity, kdialog, or osascript is available, a GUI password dialog will appear automatically when sudo is needed. The pre-tool-use hook detects sudo commands and:
1. Checks if already authenticated (`sudo -n true`)
2. If not, checks for GUI askpass availability
3. Informs user if a password dialog will appear

### Manual Execution
For complex commands or when no GUI is available:
```
"Please run this command directly in your terminal:
  sudo <command>"
```

### Utility Scripts
IdlerGear installs helper scripts in `.claude/scripts/`:
- `ig-askpass` - Multi-platform GUI password prompt (zenity, kdialog, osascript)
- `ig-sudo` - Wrapper that auto-uses askpass when available

---

For detailed documentation, see `references/` in this skill directory.
