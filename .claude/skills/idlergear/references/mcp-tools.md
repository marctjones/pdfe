# IdlerGear MCP Tools Reference

Complete reference for all 51+ MCP tools provided by IdlerGear.

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

## Environment (4 tools)

- `idlergear_env_info()` - Python/Node/Rust versions, venvs, PATH
- `idlergear_env_which(command)` - Find all matches in PATH
- `idlergear_env_detect(path?)` - Detect project type
- `idlergear_env_find_venv(path?)` - Find virtual environments

## OpenTelemetry (3 tools)

- `idlergear_otel_query_logs(service?, severity?, search?, start_time?, end_time?, limit?)` - Query logs
- `idlergear_otel_stats()` - Log statistics
- `idlergear_otel_recent_errors(service?, limit?)` - Recent errors
