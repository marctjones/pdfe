# IdlerGear MCP Tools Reference

Complete reference for all 136 MCP tools provided by IdlerGear.

## Session Management (4 tools)

### idlergear_session_start
Start a new session, loading context and previous state.

**Parameters:**
- `context_mode`: "minimal" (default) | "standard" | "detailed" | "full"
- `load_state`: boolean (default: true)

**Returns:** Vision, plan, tasks, notes, session state, recommendations.

### idlergear_session_end
End session and save state for next time.

**Parameters:**
- `current_task_id`: integer (optional)
- `working_files`: list of strings (optional)
- `notes`: string (optional)

### idlergear_session_save
Save session state manually.

**Parameters:**
- `name`: string (optional, defaults to timestamp)
- `next_steps`: string (optional)
- `blockers`: string (optional)

### idlergear_session_restore
Restore a saved session.

**Parameters:**
- `name`: string (optional, restores most recent if omitted)

## Context & Status (3 tools)

### idlergear_context
Get project context with configurable verbosity.

**Parameters:**
- `mode`: "minimal" (~750 tokens) | "standard" (~2500) | "detailed" (~7000) | "full" (~17000)
- `include_refs`: boolean (default: false)

### idlergear_status
Quick project status dashboard.

**Parameters:**
- `detailed`: boolean (default: false)

### idlergear_search
Search across all knowledge types.

**Parameters:**
- `query`: string (required)
- `types`: list of "task" | "note" | "reference" | "plan"

## Knowledge Graph (6 tools) ⚡

**Token-efficient context retrieval using embedded graph database.**

### idlergear_graph_query_task ⚡
Query task context from knowledge graph. Returns task info with related files, commits, and symbols.

**Parameters:**
- `task_id`: integer (required)

**Returns:** Task with related files, commits, symbols.

**Token savings:** 98% vs grep + file reads (5,000 → 100 tokens)

### idlergear_graph_query_file ⚡
Query file context from knowledge graph. Returns file info with related tasks, imports, and symbols.

**Parameters:**
- `file_path`: string (required)

**Returns:** File metadata, tasks, imports, symbols.

**Token savings:** 95% vs cat + grep (3,000 → 150 tokens)

### idlergear_graph_query_symbols ⚡
Search for symbols (functions, classes, methods) by name pattern.

**Parameters:**
- `pattern`: string (required) - Name pattern to search for
- `limit`: integer (default: 10) - Max results
- `type`: string (optional) - Filter by "function" | "class" | "method"

**Returns:** List of symbols with file locations and line numbers.

**Token savings:** 98.5% vs grep + file reads (8,000 → 120 tokens)

**Use this instead of grep when searching for code symbols.**

### idlergear_graph_populate_git
Index git commit history into knowledge graph.

**Parameters:**
- `max_commits`: integer (default: 100) - Maximum commits to index
- `since`: string (optional) - Date filter (e.g., "2025-01-01")
- `incremental`: boolean (default: true) - Skip existing commits

**Returns:** `{commits: int, files: int, relationships: int}`

**Run this periodically to keep graph current.**

### idlergear_graph_populate_code
Index code symbols (functions, classes, methods) into knowledge graph.

**Parameters:**
- `directory`: string (default: "src") - Directory to scan
- `incremental`: boolean (default: true) - Skip unchanged files

**Returns:** `{files: int, symbols: int, relationships: int}`

**Supports:** Python (via AST parsing)

### idlergear_graph_schema_info
Get knowledge graph schema information and statistics.

**Returns:** Node types, relationship types, counts, total nodes/relationships.

**Use cases:**
- Check if graph is initialized
- Verify data has been indexed
- Monitor graph size

## Task Management (5 tools)

### idlergear_task_create
Create a new task.

**Parameters:**
- `title`: string (required)
- `body`: string (optional)
- `labels`: list of strings (optional)
- `priority`: "high" | "medium" | "low" (optional)
- `due`: "YYYY-MM-DD" (optional)

### idlergear_task_list
List tasks.

**Parameters:**
- `state`: "open" (default) | "closed" | "all"

### idlergear_task_show
Show task details.

**Parameters:**
- `id`: integer (required)

### idlergear_task_update
Update a task.

**Parameters:**
- `id`: integer (required)
- `title`: string (optional)
- `body`: string (optional)
- `labels`: list of strings (optional)
- `priority`: "high" | "medium" | "low" | "" (optional)
- `due`: "YYYY-MM-DD" | "" (optional)

### idlergear_task_close
Close a task.

**Parameters:**
- `id`: integer (required)

## Note Management (5 tools)

### idlergear_note_create
Create a note.

**Parameters:**
- `content`: string (required)
- `tags`: list of strings (optional) - "explore", "idea", "bug"

### idlergear_note_list
List notes.

**Parameters:**
- `tag`: string (optional) - filter by tag

### idlergear_note_show
Show note details.

**Parameters:**
- `id`: integer (required)

### idlergear_note_delete
Delete a note.

**Parameters:**
- `id`: integer (required)

### idlergear_note_promote
Promote note to task or reference.

**Parameters:**
- `id`: integer (required)
- `to`: "task" | "reference" (required)

## Vision & Plans (5 tools)

### idlergear_vision_show
Show project vision.

### idlergear_vision_edit
Update project vision.

**Parameters:**
- `content`: string (required)

### idlergear_plan_create
Create a plan.

**Parameters:**
- `name`: string (required)
- `title`: string (optional)
- `body`: string (optional)

### idlergear_plan_list
List all plans.

### idlergear_plan_show
Show a plan.

**Parameters:**
- `name`: string (optional, shows current if omitted)

## Reference Management (4 tools)

### idlergear_reference_add
Add reference document.

**Parameters:**
- `title`: string (required)
- `body`: string (optional)

### idlergear_reference_list
List all references.

### idlergear_reference_show
Show a reference.

**Parameters:**
- `title`: string (required)

### idlergear_reference_search
Search references.

**Parameters:**
- `query`: string (required)

## Filesystem (11 tools)

- `idlergear_fs_read_file(path)` - Read file
- `idlergear_fs_read_multiple(paths)` - Read multiple files
- `idlergear_fs_write_file(path, content)` - Write file
- `idlergear_fs_create_directory(path)` - Create directory
- `idlergear_fs_list_directory(path?, exclude_patterns?)` - List directory
- `idlergear_fs_directory_tree(path?, max_depth?, exclude_patterns?)` - Directory tree
- `idlergear_fs_move_file(source, destination)` - Move/rename
- `idlergear_fs_search_files(pattern?, path?, use_gitignore?)` - Glob search
- `idlergear_fs_file_info(path)` - File metadata
- `idlergear_fs_file_checksum(path, algorithm?)` - File hash
- `idlergear_fs_allowed_directories()` - Security boundary

## File Registry (8 tools) ⭐ NEW v0.6.0

**Track file status (current/deprecated/archived/problematic) to prevent AI from accessing outdated files. NEW in v0.6.0: File annotations for 93% token savings.**

### idlergear_file_register
Register a file with explicit status.

**Parameters:**
- `path`: string (required) - File path relative to project root
- `status`: "current" | "deprecated" | "archived" | "problematic" (required)
- `reason`: string (optional) - Reason for this status

**Example:**
```
idlergear_file_register(path="api_v2.py", status="current")
```

### idlergear_file_deprecate
Mark a file as deprecated with optional successor.

**Parameters:**
- `path`: string (required) - File to deprecate
- `successor`: string (optional) - Path to current version
- `reason`: string (optional) - Reason for deprecation

**Example:**
```
idlergear_file_deprecate(
    path="api.py",
    successor="api_v2.py",
    reason="Refactored to use async/await"
)
```

**Use this when creating new file versions to explicitly mark old ones as deprecated.**

### idlergear_file_status
Get status of a file.

**Parameters:**
- `path`: string (required) - File path to check

**Returns:**
- `registered`: boolean
- `status`: "current" | "deprecated" | "archived" | "problematic" (if registered)
- `reason`: string (optional)
- `current_version`: string (optional) - Path to current version if deprecated
- `deprecated_at`: timestamp (optional)
- `replaces`: list of strings (optional)
- `deprecated_versions`: list of strings (optional)

**Example:**
```
result = idlergear_file_status(path="api.py")
# Returns: {"status": "deprecated", "current_version": "api_v2.py", ...}
```

**Check this before accessing files to avoid using outdated code.**

### idlergear_file_list
List all registered files, optionally filtered by status.

**Parameters:**
- `status`: "current" | "deprecated" | "archived" | "problematic" (optional)

**Returns:**
- `count`: integer - Number of files
- `files`: list of file entries with full metadata

**Example:**
```
# List all deprecated files
result = idlergear_file_list(status="deprecated")
# Returns: {"count": 3, "files": [...]}
```

### idlergear_file_search
Search files by annotations (tags, descriptions, components) for token-efficient file discovery.

**Parameters:**
- `query`: string - Search query for descriptions, tags, or components
- `tags`: list[string] (optional) - Filter by specific tags
- `limit`: integer (optional) - Maximum results to return

**Returns:**
- `count`: integer - Number of matching files
- `files`: list of files with annotations

**Example:**
```
# Search for authentication-related files
result = idlergear_file_search(query="authentication", tags=["api"])
# Returns: {"count": 2, "files": [{"path": "src/api/auth.py", "description": "...", "tags": ["api", "auth"]}]}
```

**Token savings:** 93% reduction vs grep (200 tokens vs 15,000)

### idlergear_file_annotate
Add annotations to files for token-efficient discovery.

**Parameters:**
- `path`: string - File path to annotate
- `description`: string - Human-readable description
- `tags`: list[string] (optional) - Searchable tags
- `components`: list[string] (optional) - Key classes/functions
- `related_files`: list[string] (optional) - Related file paths

**Returns:**
- `success`: boolean
- `path`: string - Annotated file path

**Example:**
```
idlergear_file_annotate(
    path="src/api/auth.py",
    description="REST API endpoints for user authentication, JWT generation",
    tags=["api", "auth", "jwt"],
    components=["AuthController", "TokenManager", "login"],
    related_files=["src/models/user.py"]
)
```

**Workflow:** Annotate files proactively after creating or understanding them.

### idlergear_file_get_annotation
Retrieve annotations and metadata for a file.

**Parameters:**
- `path`: string - File path

**Returns:**
- `path`: string
- `description`: string (if annotated)
- `tags`: list[string]
- `components`: list[string]
- `related_files`: list[string]
- `status`: string - File registry status

**Example:**
```
result = idlergear_file_get_annotation(path="src/api/auth.py")
# Returns: {"path": "...", "description": "...", "tags": ["api", "auth"], ...}
```

### idlergear_file_list_tags
List all tags used in file annotations.

**Parameters:** None

**Returns:**
- `tags`: list[string] - All unique tags across all annotated files
- `count`: integer - Number of unique tags

**Example:**
```
result = idlergear_file_list_tags()
# Returns: {"tags": ["api", "auth", "database", "jwt"], "count": 4}
```

**Condensed reference:**
- `idlergear_file_register(path, status, reason?)` - Register file
- `idlergear_file_deprecate(path, successor?, reason?)` - Mark as deprecated
- `idlergear_file_status(path)` - Check file status
- `idlergear_file_list(status?)` - List registered files
- `idlergear_file_search(query, tags?, limit?)` - Search by annotations ⭐ NEW v0.6.0
- `idlergear_file_annotate(path, description, tags?, components?, related_files?)` - Add annotations ⭐ NEW v0.6.0
- `idlergear_file_get_annotation(path)` - Get file metadata ⭐ NEW v0.6.0
- `idlergear_file_list_tags()` - List all tags ⭐ NEW v0.6.0

**See also:** `docs/guides/file-registry.md` for full documentation.

## Git Integration (18 tools)

- `idlergear_git_status(repo_path?)` - Structured status
- `idlergear_git_diff(staged?, files?, context_lines?, repo_path?)` - Diff
- `idlergear_git_log(max_count?, author?, grep?, since?, until?, repo_path?)` - History
- `idlergear_git_add(files, all?, repo_path?)` - Stage files
- `idlergear_git_commit(message, task_id?, repo_path?)` - Commit
- `idlergear_git_reset(files?, hard?, repo_path?)` - Unstage/reset
- `idlergear_git_show(commit, repo_path?)` - Show commit
- `idlergear_git_branch_list(repo_path?)` - List branches
- `idlergear_git_branch_create(name, checkout?, repo_path?)` - Create branch
- `idlergear_git_branch_checkout(name, repo_path?)` - Switch branch
- `idlergear_git_branch_delete(name, force?, repo_path?)` - Delete branch
- `idlergear_git_commit_task(task_id, message, auto_add?, repo_path?)` - Commit with task link
- `idlergear_git_status_for_task(task_id, repo_path?)` - Task-filtered status
- `idlergear_git_task_commits(task_id, max_count?, repo_path?)` - Find task commits
- `idlergear_git_sync_tasks(since?, repo_path?)` - Sync from commits

## Process Management (11 tools)

- `idlergear_pm_list_processes(filter_name?, filter_user?, sort_by?)` - List processes
- `idlergear_pm_get_process(pid)` - Process details
- `idlergear_pm_kill_process(pid, force?)` - Kill process
- `idlergear_pm_system_info()` - System stats
- `idlergear_pm_start_run(command, name?, task_id?)` - Background run
- `idlergear_pm_list_runs()` - List runs
- `idlergear_pm_get_run_status(name)` - Run status
- `idlergear_pm_get_run_logs(name, stream?, tail?)` - Run logs
- `idlergear_pm_stop_run(name)` - Stop run
- `idlergear_pm_task_runs(task_id)` - Runs for task
- `idlergear_pm_quick_start(executable, args?)` - Foreground process

## Environment (5 tools)

**Auto-activation**: The MCP server automatically detects and activates project virtualenvs on startup.

- `idlergear_env_info()` - Python/Node/Rust versions, venvs, PATH
- `idlergear_env_which(command)` - Find all matches in PATH
- `idlergear_env_detect(path?)` - Detect project type
- `idlergear_env_find_venv(path?)` - Find virtual environments
- `idlergear_env_active()` - Show currently active venv (auto-activated)

## OpenTelemetry (3 tools)

- `idlergear_otel_query_logs(service?, severity?, search?, start_time?, end_time?, limit?)` - Query logs
- `idlergear_otel_stats()` - Log statistics
- `idlergear_otel_recent_errors(service?, limit?)` - Recent errors

## Documentation (6 tools)

### idlergear_docs_check
Check if pdoc is available for documentation generation.

**Returns:** `{available: boolean}`

### idlergear_docs_module
Generate documentation for a single Python module.

**Parameters:**
- `module`: string (required) - Module name (e.g., "json", "idlergear.tasks")

**Returns:** Structured JSON with functions, classes, docstrings.

### idlergear_docs_generate
Generate full documentation for a Python package.

**Parameters:**
- `package`: string (required) - Package name
- `format`: "json" | "markdown" (default: "json")
- `include_private`: boolean (default: false)
- `max_depth`: integer (optional)

### idlergear_docs_summary ⚡
**TOKEN-EFFICIENT**: Generate compact API summary for AI consumption.

**Parameters:**
- `package`: string (required) - Package name
- `mode`: "minimal" (~500 tokens) | "standard" (~2k) | "detailed" (~5k)
- `include_private`: boolean (default: false)
- `max_depth`: integer (optional)

**Use this to quickly understand an API without consuming many tokens.**

### idlergear_docs_build
Build HTML documentation using pdoc.

**Parameters:**
- `package`: string (optional, auto-detects if not provided)
- `output_dir`: string (default: "docs/api")
- `logo`: string (optional)
- `favicon`: string (optional)

**Returns:** `{success, output_dir, files, count}`

### idlergear_docs_detect
Detect Python project configuration.

**Parameters:**
- `path`: string (default: current directory)

**Returns:** `{detected, name, version, config_file, source_dir, packages}`

## Test Framework (10 tools)

- `idlergear_test_detect(path?)` - Detect test framework (pytest, cargo test, jest, etc.)
- `idlergear_test_status(path?)` - Show last test run results
- `idlergear_test_run(path?, args?)` - Run tests and parse results
- `idlergear_test_history(path?, limit?)` - Show test run history
- `idlergear_test_list(path?, files_only?)` - List all tests in project
- `idlergear_test_coverage(path?, file?)` - Show test coverage mapping
- `idlergear_test_uncovered(path?)` - List files without tests
- `idlergear_test_changed(path?, since?, run?)` - Tests for changed files
- `idlergear_test_sync(path?)` - Import external test runs
- `idlergear_test_staleness(path?)` - Check how stale test results are

## Watch Mode (3 tools)

- `idlergear_watch_check(act?)` - One-shot project analysis (TODO/FIXME/HACK detection)
- `idlergear_watch_act(suggestion_id)` - Execute action for a specific suggestion
- `idlergear_watch_stats()` - Quick watch statistics (changed files, TODOs count)

## Health & Utility (3 tools)

- `idlergear_doctor(fix?)` - Check installation health and auto-fix issues
- `idlergear_version()` - Show MCP server version
- `idlergear_reload()` - Reload MCP server to pick up code changes

## Configuration & Backend (4 tools)

- `idlergear_config_get(key)` - Get a configuration value
- `idlergear_config_set(key, value)` - Set a configuration value
- `idlergear_backend_show()` - Show configured backends for all knowledge types
- `idlergear_backend_set(type, backend)` - Set backend for a knowledge type

## Run Management (5 tools)

- `idlergear_run_start(command, name?)` - Start background script/command
- `idlergear_run_list(limit?)` - List all runs
- `idlergear_run_status(name)` - Get run status
- `idlergear_run_logs(name, stream?, tail?)` - Get run logs
- `idlergear_run_stop(name)` - Stop a running process

## Project Boards (9 tools)

**Auto-add Configuration:** Tasks can be automatically added to a default project by setting `projects.auto_add = true`, `projects.default_project`, and `projects.default_column` in config.toml. When configured, `idlergear_task_create()` will automatically add new tasks to the project and return `added_to_project: true` in the response.

- `idlergear_project_create(title, columns?, create_on_github?)` - Create Kanban board
- `idlergear_project_list(include_github?)` - List all project boards
- `idlergear_project_show(name)` - Show project with columns and tasks
- `idlergear_project_delete(name, delete_on_github?)` - Delete project board
- `idlergear_project_add_task(project_name, task_id, column?)` - Add task to board
- `idlergear_project_remove_task(project_name, task_id)` - Remove task from board
- `idlergear_project_move_task(project_name, task_id, column)` - Move task to column
- `idlergear_project_sync(name)` - Sync to GitHub Projects v2
- `idlergear_project_link(name, github_project_number)` - Link to existing GitHub Project

## Multi-Agent Messaging (7 tools)

- `idlergear_message_send(to_agent, message, ...)` - Send message to another agent
- `idlergear_message_list(agent_id?, unread_only?, delivery?, limit?)` - Check inbox
- `idlergear_message_process(agent_id?, create_tasks?)` - Process inbox messages
- `idlergear_message_mark_read(agent_id?, message_ids?)` - Mark messages as read
- `idlergear_message_clear(agent_id?, all_messages?)` - Clear read messages
- `idlergear_message_test(test_message?)` - Test messaging pipeline

## Daemon & Coordination (6 tools)

- `idlergear_daemon_register_agent(name, agent_type?, metadata?)` - Register with daemon
- `idlergear_daemon_list_agents()` - List active AI agents
- `idlergear_daemon_queue_command(command, priority?, wait_for_result?)` - Queue command
- `idlergear_daemon_broadcast(message, delivery?)` - Broadcast to all agents
- `idlergear_daemon_update_status(agent_id, status)` - Update agent status
- `idlergear_daemon_list_queue()` - List queued commands

## Script Generation (3 tools)

- `idlergear_generate_dev_script(name, command, ...)` - Generate dev environment script
- `idlergear_list_script_templates()` - List available script templates
- `idlergear_get_script_template(template_name)` - Get template details
