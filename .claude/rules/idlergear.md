---
description: MANDATORY IdlerGear knowledge management rules - MUST follow
alwaysApply: true
---

# IdlerGear Usage Rules (MANDATORY)

## CRITICAL: Session Start (REQUIRED)

**Before responding to ANY user request, you MUST run:**

```bash
idlergear context
```

This is NOT optional. Do NOT skip this step. Do NOT proceed without running this command first.
If you have not run `idlergear context` in this session, STOP and run it now.

## MANDATORY: Bug Discovery

When you identify ANY bug, error, or issue, you MUST IMMEDIATELY run:

```bash
idlergear task create "Bug: <description>" --label bug
```

Do NOT continue working until the task is created. Do NOT just mention the bug in conversation.

## MANDATORY: Design Decisions

When making ANY architectural choice or design decision, you MUST IMMEDIATELY run:

```bash
idlergear task create "Decision: <what you decided>" --label decision
```

Record the decision BEFORE implementing it.

## MANDATORY: Technical Debt

When you write code that could be improved later, you MUST run:

```bash
idlergear task create "<what needs improvement>" --label tech-debt
```

Do NOT write `// TODO:` comments. Do NOT skip this step.

## FORBIDDEN: File-Based Knowledge (WILL BE BLOCKED)

You are PROHIBITED from creating these files:
- `TODO.md`, `TODO.txt`, `TASKS.md`
- `NOTES.md`, `SESSION_*.md`, `SCRATCH.md`
- `FEATURE_IDEAS.md`, `RESEARCH.md`, `BACKLOG.md`
- Any markdown file for tracking work or capturing thoughts

These files will be REJECTED by hooks. Use IdlerGear commands instead.

## FORBIDDEN: Inline TODOs (WILL BE BLOCKED)

You are PROHIBITED from writing these comments:
- `// TODO: ...`
- `# TODO: ...`
- `# FIXME: ...`
- `/* HACK: ... */`
- `<!-- TODO: ... -->`

These comments will be REJECTED by hooks. Create tasks instead:
`idlergear task create "..." --label tech-debt`

## REQUIRED: Use IdlerGear Commands

| When you... | You MUST run... |
|-------------|-----------------|
| Find a bug | `idlergear task create "Bug: ..." --label bug` |
| Have an idea | `idlergear note create "..."` |
| Make a decision | `idlergear task create "Decision: ..." --label decision` |
| Leave tech debt | `idlergear task create "..." --label tech-debt` |
| Complete work | `idlergear task close <id>` |
| Research something | `idlergear explore create "..."` |
| Document findings | `idlergear reference add "..." --body "..."` |

## Data Protection

**NEVER modify `.idlergear/` files directly** - Use CLI commands only
**NEVER modify `.claude/` or `.mcp.json`** - These are protected

## Enforcement

Hooks are configured to:
1. Block commits with TODO comments
2. Block creation of forbidden files
3. Remind you to run `idlergear context` at session start
