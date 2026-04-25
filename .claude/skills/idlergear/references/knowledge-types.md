# IdlerGear Knowledge Types

IdlerGear manages 6 knowledge types, each with specific use cases and MCP tools.

## 1. Tasks

Work items with lifecycle (open → closed). Syncs to GitHub Issues.

**When to use:** Actionable work with clear completion criteria.

**MCP Tools:**
- `idlergear_task_create(title, body?, labels?, priority?, due?)`
- `idlergear_task_list(state="open"|"closed"|"all")`
- `idlergear_task_show(id)`
- `idlergear_task_update(id, title?, body?, labels?, priority?, due?)`
- `idlergear_task_close(id)`

**Labels:**
- `bug` - Something broken
- `enhancement` - New feature request
- `tech-debt` - Code needing improvement
- `decision` - Architectural decision made
- `priority: high|medium|low` - Priority level

**Example:**
```python
idlergear_task_create(
    title="Fix authentication timeout",
    body="Users getting logged out after 5 minutes",
    labels=["bug", "priority: high"]
)
```

## 2. Notes

Quick capture for thoughts, discoveries, and observations.

**When to use:** Capture now, organize later. Perfect for:
- Discoveries while debugging
- API behaviors learned
- Quick ideas
- Gotchas and quirks

**MCP Tools:**
- `idlergear_note_create(content, tags?)`
- `idlergear_note_list(tag?)`
- `idlergear_note_show(id)`
- `idlergear_note_delete(id)`
- `idlergear_note_promote(id, to="task"|"reference")`

**Tags:**
- `explore` - Research questions and investigations
- `idea` - Future possibilities and enhancements
- `bug` - Bug observations (not yet tasks)

**Example:**
```python
idlergear_note_create(
    content="Auth endpoint requires Bearer prefix in header",
    tags=["explore"]
)
```

## 3. Vision

Project goals and direction. Single document per project.

**When to use:** Define or update project purpose.

**MCP Tools:**
- `idlergear_vision_show()`
- `idlergear_vision_edit(content)`

## 4. Plans

Implementation roadmaps grouping related tasks.

**When to use:** Multi-step features or initiatives.

**MCP Tools:**
- `idlergear_plan_create(name, title?, body?)`
- `idlergear_plan_list()`
- `idlergear_plan_show(name?)`
- `idlergear_plan_switch(name)`

## 5. References

Permanent documentation that persists across sessions.

**When to use:**
- API documentation
- Architecture decisions
- Setup guides
- Integration patterns

**MCP Tools:**
- `idlergear_reference_add(title, body?)`
- `idlergear_reference_list()`
- `idlergear_reference_show(title)`
- `idlergear_reference_search(query)`

**Example:**
```python
idlergear_reference_add(
    title="Authentication Flow",
    body="## OAuth2 Implementation\n\n1. Redirect to /auth/login..."
)
```

## 6. Runs

Process execution with captured output.

**When to use:** Long-running commands, test suites, dev servers.

**MCP Tools:**
- `idlergear_run_start(command, name?)`
- `idlergear_run_list()`
- `idlergear_run_status(name)`
- `idlergear_run_logs(name, stream?, tail?)`
- `idlergear_run_stop(name)`

## Knowledge Flow

```
note → task or reference
         ↓
    task → close
```

1. Capture quickly with notes
2. Promote actionable items to tasks
3. Promote documentation to references
4. Close completed tasks

Use `idlergear_note_promote(id, to="task")` to convert.
