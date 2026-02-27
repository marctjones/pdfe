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

## MANDATORY: AI State Reporting (REAL-TIME OBSERVABILITY)

**Enable users to monitor your work in real-time via the TUI (View 6).**

### BEFORE Every Major Action

Call `idlergear_ai_report_activity` BEFORE:
- Reading files (grep, cat, Read tool)
- Running commands (Bash tool)
- Editing/writing files (Edit, Write tools)
- Starting multi-step work

```python
idlergear_ai_report_activity(
    phase="researching",  # or "planning", "implementing", "testing"
    action="reading file",  # what you're about to do
    target="src/file.py",  # file/command/target
    reason="Understanding current implementation"  # WHY
)
```

### When Planning Multi-Step Work

Call `idlergear_ai_report_plan` when you have a plan with 2+ steps:

```python
idlergear_ai_report_plan(
    steps=[
        {"action": "read file", "target": "config.py", "reason": "check settings"},
        {"action": "edit file", "target": "main.py", "reason": "update logic"},
        {"action": "run tests", "target": "pytest", "reason": "verify changes"}
    ],
    confidence=0.85  # 0.0-1.0, be honest
)
```

### When Uncertain or Confused

Call `idlergear_ai_report_uncertainty` when confidence < 0.7:

```python
idlergear_ai_report_uncertainty(
    question="How should I handle database migrations?",
    confidence=0.4,  # be honest about low confidence
    context={
        "searched_files": ["migrations/", "db.py"],
        "not_found": "migration framework documentation"
    }
)
```

### After Searches

Call `idlergear_ai_report_search` after grep/file searches:

```python
idlergear_ai_report_search(
    query="database connection",
    search_type="grep",  # or "file", "documentation", "web"
    results_found=3,
    files_searched=["db.py", "config.py"]
)
```

**WHY THIS MATTERS:**
- Users can see what you're doing in real-time
- They can intervene BEFORE you waste time going down wrong path
- Low confidence alerts = user can provide answers immediately
- Repeated searches = user knows you're stuck

**This is NOT optional. Report your state proactively.**

## MANDATORY: File Annotations (93% Token Savings)

**You MUST annotate files proactively to enable token-efficient discovery.**

### When to Annotate (DO NOT SKIP)

1. **After creating a new file** - Annotate immediately with purpose
2. **After reading a file to understand it** - Capture that knowledge
3. **When refactoring** - Update annotations to stay accurate
4. **Instead of grep for finding files** - Search annotations first

### How to Annotate

```python
idlergear_file_annotate(
    path="src/api/auth.py",
    description="REST API endpoints for user authentication, JWT generation, session management",
    tags=["api", "auth", "endpoints", "jwt"],
    components=["AuthController", "TokenManager", "login"],
    related_files=["src/models/user.py"]
)
```

### Finding Files Efficiently

```python
# INSTEAD OF: grep + reading 10 files (15,000 tokens)
# DO THIS: search annotations (200 tokens, 93% savings!)
result = idlergear_file_search(query="authentication")
# Returns: [{"path": "src/api/auth.py", "description": "...", "tags": ["auth"]}]

# Then read only the right file
idlergear_fs_read_file(path="src/api/auth.py")
```

**Rules:**
- ✅ Annotate new files immediately
- ✅ Search annotations before grep
- ✅ Update annotations when refactoring
- ❌ Don't leave files unannotated
- ❌ Don't use grep when annotations exist

## MANDATORY: File Status Tracking (Prevent Using Outdated Code)

**You MUST mark old files as deprecated when refactoring or creating new versions.**

### When to Use File Status Tracking

1. **Creating new version of a file** - Deprecate old version, link to new one
2. **Refactoring moves functionality** - Deprecate old location
3. **Experimental code abandoned** - Archive it with reason
4. **File has known issues** - Mark as problematic

### File Statuses

- `current` - Active, should be used (default for new files)
- `deprecated` - Outdated, successor available
- `archived` - Historical, not for active work
- `problematic` - Has known issues, use cautiously

### How to Deprecate Files

```python
# When you create a new version, deprecate the old one
idlergear_file_deprecate(
    path="src/api_old/auth.py",
    successor="src/api/auth.py",  # Path to current version
    reason="Migrated to OAuth2 with JWT tokens"
)

# Check file status
idlergear_file_status(path="src/api_old/auth.py")
# Returns: {"status": "deprecated", "successor": "src/api/auth.py", "reason": "..."}
```

### Search Only Current Files

```python
# Filter out deprecated/archived files
idlergear_file_search(query="authentication", status="current")
# Only returns active, current files
```

### Archive Experimental Code

```python
idlergear_file_register(
    path="src/experimental/ml_auth.py",
    status="archived",
    reason="ML approach abandoned, too complex for use case"
)
```

### Rules

- ✅ Deprecate old files when creating new versions
- ✅ Always provide `successor` path for deprecated files
- ✅ Use `status="current"` filter when searching for files
- ✅ Provide clear `reason` for deprecation/archiving
- ❌ Don't leave old file versions without status tracking
- ❌ Don't let deprecated files accumulate without documentation

## MANDATORY: Knowledge Graph (95-98% Token Savings)

**You MUST use knowledge graph queries instead of grep/file reads for context retrieval.**

**The graph provides:**
- 95-98% token savings vs grep + file reads
- Sub-40ms query response times
- 2,003+ nodes indexed (commits, files, symbols)

### ALWAYS Prefer Graph Over Grep When:

1. **Finding symbols** - Functions, classes, methods by name
2. **Getting task context** - Files, commits, symbols related to a task
3. **Understanding file relationships** - Imports, dependencies, changes
4. **Searching code** - Fast symbol lookup without reading files

### Query Patterns

```python
# INSTEAD OF: grep -r "function_name" (7,500 tokens)
# USE: Knowledge graph symbol search (100 tokens, 98.7% savings!)
idlergear_graph_query_symbols(pattern="function_name", limit=10)

# INSTEAD OF: Reading 5 files to find task context (5,000 tokens)
# USE: Knowledge graph task query (100 tokens, 98% savings!)
idlergear_graph_query_task(task_id=278)

# INSTEAD OF: cat + grep for file relationships (3,000 tokens)
# USE: Knowledge graph file query (150 tokens, 95% savings!)
idlergear_graph_query_file(file_path="src/idlergear/mcp_server.py")
```

### When Graph is Empty

```python
# Populate everything in one command (once per project)
idlergear_graph_populate_all(max_commits=100, incremental=True)
# Populates: git history, code symbols, GitHub tasks, commit-task links

# Re-run periodically (incremental = skips existing data)
```

**Rules:**
- ✅ Use graph queries for symbol/task/file lookups
- ✅ Query graph BEFORE grepping (check if data exists)
- ✅ Fall back to grep only if graph returns no results
- ❌ Don't use grep when graph can answer the query
- ❌ Don't read multiple files when graph has the context

## MANDATORY: Tmux Session Management (Persistent Processes)

**You MUST use tmux sessions for long-running processes to enable persistent access and monitoring.**

### When to Use `idlergear run start` with `--tmux`

ALWAYS use `--tmux` flag for:
1. **Commands with sudo** - ANY command using sudo (requires password input)
2. **Long-running servers** - Web servers, databases, API servers
3. **Interactive processes** - Processes requiring user input
4. **Development servers** - Servers you might need to restart/control (hot reload, debugging)
5. **Monitoring required** - Processes where live output visibility is important
6. **Data operations** - Loading datasets, parsing large files, data transformations
7. **Large test suites** - Test runs that take more than a few minutes
8. **Long builds** - Compilation, bundling, or build processes taking >2 minutes
9. **Database migrations** - Schema changes, data migrations that can be slow
10. **Data processing** - ETL jobs, data analysis, batch processing

```bash
# CRITICAL: Any command with sudo MUST use tmux (requires password input)
idlergear run start "sudo apt update && sudo apt install -y build-essential" --tmux --name system-install
idlergear run start "sudo systemctl restart nginx" --tmux --name restart-nginx

# Start development server in tmux (user can attach later)
idlergear run start "python manage.py runserver" --tmux --name backend

# Start with environment variables
idlergear run start "uvicorn app:main --reload" --tmux --name api --env FLASK_ENV=development

# Long-running data operations
idlergear run start "python load_dataset.py --size large" --tmux --name data-load
idlergear run start "pytest tests/ -v" --tmux --name full-test-suite
```

### When to Use Standalone Tmux Sessions

Use `idlergear_tmux_create_session` for:
1. **Interactive development environments** - Python REPL, Node.js console
2. **Terminal multiplexing** - Multiple shells for complex workflows
3. **Persistent shells** - Shells that survive disconnection

```python
# Create interactive Python shell in tmux
idlergear_tmux_create_session(
    name="python-repl",
    command="python -i",
    working_directory="/home/user/project"
)

# Create persistent bash shell
idlergear_tmux_create_session(
    name="dev-shell",
    command="bash",
    working_directory="/home/user/project"
)
```

### Available MCP Tools

```python
# Create standalone tmux session
idlergear_tmux_create_session(name="dev", command="bash")

# List all tmux sessions
idlergear_tmux_list_sessions()

# Get session details
idlergear_tmux_get_session(name="dev")

# Send commands to session (non-interactive control)
idlergear_tmux_send_keys(session_name="dev", keys="ls -la")

# Kill session when done
idlergear_tmux_kill_session(name="dev")

# Get attach command for a run
idlergear_run_attach(name="backend")
```

### Benefits

- **Persistent access** - User can attach to session anytime with `tmux attach -t idlergear-<name>`
- **Live monitoring** - See real-time output, logs, errors
- **Interactive control** - Restart, debug, send input to process
- **Survives disconnection** - Process keeps running if terminal closes

### Rules

- ✅ **CRITICAL:** Use `--tmux` for ALL commands with sudo (they ALWAYS need password input)
- ✅ Use `--tmux` for all long-running servers and development tools
- ✅ Use `--tmux` for data operations, large test suites, long builds
- ✅ Use descriptive names (`--name backend`, not `--name run1`)
- ✅ Provide attach instructions to user after starting tmux process
- ❌ Don't use regular background processes for servers (use tmux instead)
- ❌ Don't use tmux for short-lived commands (quick tests, fast builds - use regular bash)

**This is NOT optional. Use tmux for persistent processes.**

## MANDATORY: Container Management (Podman/Docker)

**You MUST use containers for isolated, reproducible environments and service dependencies.**

### When to Use `idlergear_container_start`

ALWAYS use containers for:
1. **Database services** - PostgreSQL, MySQL, MongoDB, Redis
2. **Message queues** - RabbitMQ, Kafka, NATS
3. **Development dependencies** - Elasticsearch, Memcached, MinIO
4. **Isolated testing** - Run tests in clean environment
5. **Reproducible builds** - Ensure consistent build environment
6. **Service mocking** - Mock external APIs, microservices
7. **Multi-version testing** - Test against different Python/Node/etc versions

```python
# Start PostgreSQL database for development
idlergear_container_start(
    image="postgres:15-alpine",
    name="dev-db",
    env={"POSTGRES_PASSWORD": "dev", "POSTGRES_DB": "myapp"},
    ports={"5432": "5432"},
    volumes={"/data/postgres": "/var/lib/postgresql/data"}
)

# Start Redis cache
idlergear_container_start(
    image="redis:alpine",
    name="dev-redis",
    ports={"6379": "6379"}
)

# Run tests in isolated Python 3.11 environment
idlergear_container_start(
    image="python:3.11-slim",
    name="test-py311",
    command="pytest tests/",
    volumes={"/home/user/project": "/app"}
)
```

### Available MCP Tools

```python
# List running containers
idlergear_container_list(all=False)  # Running only
idlergear_container_list(all=True)   # Include stopped

# Start container
idlergear_container_start(
    image="image:tag",
    name="container-name",
    command="optional command",
    env={"KEY": "value"},
    volumes={"/host/path": "/container/path"},
    ports={"8080": "80"},
    memory="512m",
    cpus="1.5"
)

# Stop container
idlergear_container_stop(container_id="name-or-id", force=False)

# Remove container
idlergear_container_remove(container_id="name-or-id", force=False)

# Get container logs
idlergear_container_logs(container_id="name-or-id", tail=100)

# Get container resource stats
idlergear_container_stats(container_id="name-or-id")
```

### Benefits

- **Isolation** - Services don't pollute host system
- **Reproducibility** - Same environment everywhere (dev, CI, prod)
- **Easy cleanup** - Remove container, no leftover state
- **Version flexibility** - Run multiple versions simultaneously
- **Fast setup** - Spin up complex dependencies in seconds

### Rules

- ✅ Use containers for all database/service dependencies
- ✅ Use descriptive names (`--name dev-db`, not `--name postgres1`)
- ✅ Mount project directory as volume for development containers
- ✅ Use alpine/slim images for faster downloads
- ✅ Stop and remove containers when done (don't leave orphans)
- ❌ Don't run containers for simple scripts (use regular bash)
- ❌ Don't commit database credentials to git (use env vars)

**This is NOT optional. Use containers for isolated environments.**

## Data Protection

**NEVER modify `.idlergear/` files directly** - Use CLI commands only
**NEVER modify `.claude/` or `.mcp.json`** - These are protected

## Enforcement

Hooks are configured to:
1. Block commits with TODO comments
2. Block creation of forbidden files
3. Remind you to run `idlergear context` at session start
