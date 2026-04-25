#!/bin/bash
# Auto-inject IdlerGear context at session start
# FAST VERSION: Reads files directly instead of calling CLI

# Check if IdlerGear is initialized
if [ ! -d ".idlergear" ]; then
    exit 0  # Silent exit if not an IdlerGear project
fi

# Start daemon if not running (background, no output)
if command -v idlergear &>/dev/null; then
    idlergear daemon start &>/dev/null &
fi

# ============================================
# KNOWLEDGE GRAPH AUTO-POPULATION (Level 2)
# ============================================
# Check if knowledge graph is populated, populate if needed
if command -v python3 &>/dev/null; then
    GRAPH_CHECK=$(python3 -c "
try:
    from idlergear.graph import get_database
    from idlergear.graph.schema import get_schema_info
    db = get_database()
    info = get_schema_info(db)
    print(f\"{info['total_nodes']} {info['node_counts']['Symbol']} {info['node_counts']['File']}\")
except Exception as e:
    print('0 0 0')
" 2>/dev/null)

    read TOTAL_NODES SYMBOLS FILES <<< "$GRAPH_CHECK"

    # If graph is empty or has very few nodes, populate it
    if [ "$TOTAL_NODES" -lt 100 ] || [ "$SYMBOLS" -lt 50 ]; then
        echo "🔄 Knowledge graph has only $TOTAL_NODES nodes (need 100+). Populating..." >&2
        echo "   This runs once in background (~30 seconds)..." >&2

        # Run populate_all in background to avoid blocking session start
        (
            python3 -c "
from idlergear.graph.populate_all import populate_all
from pathlib import Path
import sys
try:
    results = populate_all(Path.cwd(), max_commits=100, incremental=True, verbose=False)
    total = sum([
        results.get('git', {}).get('commits', 0),
        results.get('code', {}).get('symbols', 0),
        results.get('tasks', {}).get('tasks', 0),
        results.get('plans', {}).get('plans', 0),
    ])
    print(f'✅ Knowledge graph populated: {total} nodes', file=sys.stderr)
except Exception as e:
    print(f'⚠️ Graph population failed: {e}', file=sys.stderr)
" 2>&1 | tee -a .idlergear/graph-populate.log >&2
        ) &

        # Don't wait - let it run in background
    else
        echo "✅ Knowledge graph ready: $SYMBOLS symbols, $FILES files, $TOTAL_NODES total nodes" >&2
    fi
fi

# Build context by reading files directly (no Python startup overhead)
CONTEXT=""

# Read vision (VISION.md in repo root)
if [ -f "VISION.md" ]; then
    VISION=$(cat "VISION.md" 2>/dev/null | head -20)
    if [ -n "$VISION" ]; then
        CONTEXT="${CONTEXT}## Vision\n${VISION}\n\n"
    fi
fi

# Count open tasks
TASK_COUNT=0
if [ -d ".idlergear/tasks" ]; then
    TASK_COUNT=$(ls -1 ".idlergear/tasks/"*.md 2>/dev/null | wc -l)
fi

if [ "$TASK_COUNT" -gt 0 ]; then
    CONTEXT="${CONTEXT}## Open Tasks: ${TASK_COUNT}\n"
    # Show first 5 task titles (from YAML frontmatter)
    for f in $(ls -1t ".idlergear/tasks/"*.md 2>/dev/null | head -5); do
        TITLE=$(grep "^title:" "$f" 2>/dev/null | head -1 | sed "s/^title: *['\"]*//" | sed "s/['\"]* *$//")
        if [ -n "$TITLE" ]; then
            CONTEXT="${CONTEXT}- ${TITLE}\n"
        fi
    done
    CONTEXT="${CONTEXT}\n"
fi

# Count notes
NOTE_COUNT=0
if [ -d ".idlergear/notes" ]; then
    NOTE_COUNT=$(ls -1 ".idlergear/notes/"*.md 2>/dev/null | wc -l)
fi

if [ "$NOTE_COUNT" -gt 0 ]; then
    CONTEXT="${CONTEXT}## Recent Notes: ${NOTE_COUNT}\n\n"
fi

# Check for pending messages in any inbox
# First, try to find our agent ID from presence files
AGENT_ID=""
if [ -d ".idlergear/agents" ]; then
    for f in .idlergear/agents/*.json; do
        [ -f "$f" ] || continue
        [ "$(basename "$f")" = "agents.json" ] && continue
        AGENT_ID=$(basename "$f" .json)
        break
    done
fi

# Check inbox for messages
MESSAGE_COUNT=0
MESSAGES=""
if [ -n "$AGENT_ID" ] && [ -d ".idlergear/inbox/$AGENT_ID" ]; then
    for msg_file in $(ls -1t ".idlergear/inbox/$AGENT_ID/"*.json 2>/dev/null); do
        [ -f "$msg_file" ] || continue
        # Check if unread (simple grep check)
        if grep -q '"read": false' "$msg_file" 2>/dev/null || ! grep -q '"read":' "$msg_file" 2>/dev/null; then
            MESSAGE_COUNT=$((MESSAGE_COUNT + 1))
            # Extract sender and message preview
            FROM=$(grep '"from":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"from": *"\([^"]*\)".*/\1/')
            MSG=$(grep '"message":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"message": *"\([^"]*\)".*/\1/' | cut -c1-100)
            if [ -n "$FROM" ] && [ -n "$MSG" ]; then
                MESSAGES="${MESSAGES}From ${FROM}: ${MSG}...\n"
            fi
        fi
    done
fi

if [ "$MESSAGE_COUNT" -gt 0 ]; then
    CONTEXT="${CONTEXT}## PENDING MESSAGES (${MESSAGE_COUNT})\n${MESSAGES}\nUse idlergear_message_list to see full messages and idlergear_message_mark_read after processing.\n\n"
fi

# Output context if any
if [ -n "$CONTEXT" ]; then
    # Escape for JSON
    CONTEXT_ESCAPED=$(echo -e "$CONTEXT" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | tr '\n' '\\' | sed 's/\\/\\n/g')
    cat <<EOF
{
  "additionalContext": "=== IDLERGEAR PROJECT ===\\n\\n${CONTEXT_ESCAPED}\\nRun 'idlergear context' for full details.\\n=== END ==="
}
EOF
fi

exit 0
