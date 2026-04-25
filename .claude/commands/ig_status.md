---
description: Show IdlerGear status - MCP, daemon, queues, and configuration
---

Check the status of IdlerGear integration and report:

## 1. MCP Server Status
- Check if IdlerGear MCP tools are available by trying to call `idlergear_version()`
- If MCP works, report "MCP: Connected (version X.X.X)"
- If MCP fails, report "MCP: Not available - falling back to CLI"

## 2. Daemon Status
- Call `idlergear_status()` MCP tool (or run `idlergear status` CLI)
- Report if daemon is running or not
- If running, show:
  - Number of registered agents
  - Number of queued commands
  - Active runs

## 3. Project Status
- Number of open tasks (by priority if any)
- Number of notes
- Current plan (if any)
- Whether vision is set

## 4. Integration Status
- Check which files exist:
  - `.mcp.json` - MCP server registered
  - `.claude/skills/idlergear/` - Skill installed
  - `.claude/hooks.json` - Hooks configured
  - `.claude/rules/idlergear.md` - Rules installed

Format the output as a clear status dashboard.
