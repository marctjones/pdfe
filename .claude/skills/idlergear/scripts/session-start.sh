#!/bin/bash
# Zero-context script: Start a new session
# Output is returned to Claude, script content is NOT loaded into context

# Call the MCP tool via CLI fallback
idlergear context --mode minimal

echo ""
echo "---"
echo "Session started. Call idlergear_session_start() for full MCP integration."
