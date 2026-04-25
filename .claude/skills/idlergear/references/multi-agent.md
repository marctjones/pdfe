# Multi-Agent Coordination

IdlerGear daemon enables multiple AI assistants to work together on the same codebase.

## Starting the Daemon

The daemon provides coordination features for multi-agent workflows.

**MCP Tools:**
- `idlergear_daemon_register_agent(name, agent_type?, metadata?)` - Register as agent
- `idlergear_daemon_list_agents()` - See active agents
- `idlergear_daemon_queue_command(command, priority?, wait_for_result?)` - Queue work
- `idlergear_daemon_send_message(message)` - Broadcast to agents
- `idlergear_daemon_update_status(agent_id, status)` - Update status
- `idlergear_daemon_list_queue()` - View command queue

## Agent Registration

When using IdlerGear MCP tools, you can register as an agent:

```python
idlergear_daemon_register_agent(
    name="Claude Code Session",
    agent_type="claude-code"
)
```

This makes your session visible to other agents and allows receiving broadcasts.

## Coordination Features

### Message Passing

Broadcast to all active agents:
```python
idlergear_daemon_send_message("API schema changed, please refresh")
```

### Command Queue

Queue work for any available agent:
```python
idlergear_daemon_queue_command(
    command="run full test suite",
    priority=5
)
```

### Status Updates

Signal your current state:
```python
idlergear_daemon_update_status(
    agent_id="your-agent-id",
    status="busy"  # "active" | "idle" | "busy"
)
```

## Use Cases

1. **Long-running tasks** - Queue work while continuing on other things
2. **Multi-terminal coordination** - Multiple AI sessions see same state
3. **Background execution** - Queue tests/builds asynchronously
4. **Team coordination** - Share context across AI sessions

## Script Generation

Generate shell scripts that auto-register with daemon:

```python
idlergear_generate_dev_script(
    name="backend",
    command="python manage.py runserver",
    venv_path="./venv",
    requirements=["django"],
    env_vars={"DEBUG": "1"},
    stream_logs=True
)
```

**Available Templates:**
- `pytest` - Test runner
- `django-dev` - Django development server
- `flask-dev` - Flask development server
- `jupyter` - Jupyter Lab
- `fastapi-dev` - FastAPI with uvicorn

Use `idlergear_list_script_templates()` to see all templates.
Use `idlergear_get_script_template(template_name)` for details.
