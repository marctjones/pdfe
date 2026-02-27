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
  - User wants to explore or document a Python API
  - User mentions: "docs", "documentation", "API", "explore package"
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

### File Status Tracking

Track which files are current, deprecated, archived, or problematic to prevent using outdated code:

| Action | MCP Tool |
|--------|----------|
| Register file | `idlergear_file_register(path="...", status="current")` |
| Deprecate file | `idlergear_file_deprecate(path="...", successor="...", reason="...")` |
| Check file status | `idlergear_file_status(path="...")` |
| List files by status | `idlergear_file_list(status="deprecated")` |

**File statuses:**
- `current` - Active, should be used
- `deprecated` - Outdated, successor available
- `archived` - Old version kept for reference
- `problematic` - Has known issues

**Automatic Protection:**
When files are registered with non-current statuses, IdlerGear's MCP server automatically intercepts file operations:
- **Deprecated files**: Reads are blocked with suggestions to use the successor. Writes are allowed with warnings.
- **Archived/Problematic files**: All access is blocked with explanatory messages.
- All access attempts are logged to `.idlergear/access_log.jsonl` for audit purposes.
- Use `_allow_deprecated=True` parameter to bypass checks when intentionally accessing deprecated files.

### File Annotations (Token-Efficient Discovery) ⭐

**IMPORTANT: Annotate files proactively to enable 93% token savings on file discovery!**

Instead of grep + reading 10-15 files (~15,000 tokens), annotate once and search efficiently (~200 tokens).

| Action | MCP Tool |
|--------|----------|
| Annotate file | `idlergear_file_annotate(path="...", description="...", tags=[], components=[])` |
| Find files | `idlergear_file_search(query="authentication")` or `tags=["api"]` |
| Get annotation | `idlergear_file_get_annotation(path="...")` |
| List all tags | `idlergear_file_list_tags()` |

**When to Annotate:**
- ✅ After creating a new file (immediate context for future sessions)
- ✅ When you understand what a file does (capture that knowledge)
- ✅ When refactoring (update annotations to stay accurate)
- ✅ When you see missing annotations during file search

**Good Annotation Practices:**
1. **Description**: Clear, concise summary (1-2 sentences)
   - Good: "REST API endpoints for user authentication, JWT generation, session management"
   - Bad: "API stuff" (too vague)

2. **Tags**: 2-5 searchable keywords
   - Good: `["api", "auth", "endpoints", "jwt"]`
   - Bad: `["file", "code"]` (too generic)

3. **Components**: Key classes/functions users will search for
   - Good: `["AuthController", "TokenManager", "login", "verify_token"]`
   - Bad: `["helper", "utils"]` (not specific)

4. **Related Files**: Files that work together
   - Good: `["src/models/user.py", "src/middleware/auth.py"]`

**Workflow Example:**

```python
# User: "Where is the authentication code?"

# Step 1: Search efficiently (200 tokens instead of 15,000!)
result = idlergear_file_search(query="authentication")
# Returns: [{"path": "src/api/auth.py", "description": "REST API endpoints...", "components": ["AuthController"]}]

# Step 2: Read only the right file (1,000 tokens)
content = idlergear_fs_read_file(path="src/api/auth.py")

# Total: 1,200 tokens vs 15,000 (93% savings!)
```

**Proactive Annotation Strategy:**
- Annotate as you work (don't wait until the end)
- When reading a file to understand it, annotate it
- When creating new files, annotate immediately
- Keep annotations updated when code changes

See `references/mcp-tools.md` for detailed documentation.

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

## Python Documentation (API Exploration)

Quickly explore Python APIs with token-efficient summaries:

### Token-Efficient Summaries ⚡
```
idlergear_docs_summary(package="requests", mode="minimal")   # ~500 tokens
idlergear_docs_summary(package="requests", mode="standard")  # ~2000 tokens
idlergear_docs_summary(package="requests", mode="detailed")  # ~5000 tokens
```

### Other Docs Tools

| Action | MCP Tool |
|--------|----------|
| Check pdoc available | `idlergear_docs_check()` |
| Single module docs | `idlergear_docs_module(module="json")` |
| Full package docs | `idlergear_docs_generate(package="...", format="json")` |
| Build HTML docs | `idlergear_docs_build(package="...")` |
| Detect project | `idlergear_docs_detect()` |

**Requires:** `pip install 'idlergear[docs]'`

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
