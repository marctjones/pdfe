#!/bin/bash
# Notification hook - Check for messages when agent is idle/waiting
# This hook fires when Claude Code shows notifications (e.g., waiting for user input)
# Perfect time to check for messages since the agent isn't actively working

# Check if IdlerGear is initialized
if [ ! -d ".idlergear" ]; then
    exit 0
fi

# Only check if daemon is running
if [ ! -S ".idlergear/daemon.sock" ]; then
    exit 0
fi

# Find our agent ID from presence files
AGENT_ID=""
if [ -d ".idlergear/agents" ]; then
    for f in .idlergear/agents/*.json; do
        [ -f "$f" ] || continue
        [ "$(basename "$f")" = "agents.json" ] && continue
        AGENT_ID=$(basename "$f" .json)
        break
    done
fi

# No agent ID means we're not registered
if [ -z "$AGENT_ID" ]; then
    exit 0
fi

# Check inbox for unread messages
if [ -d ".idlergear/inbox/$AGENT_ID" ]; then
    MESSAGE_COUNT=0
    MESSAGES=""

    for msg_file in $(ls -1t ".idlergear/inbox/$AGENT_ID/"*.json 2>/dev/null | head -5); do
        [ -f "$msg_file" ] || continue

        # Check if unread
        if grep -q '"read": false' "$msg_file" 2>/dev/null || ! grep -q '"read":' "$msg_file" 2>/dev/null; then
            MESSAGE_COUNT=$((MESSAGE_COUNT + 1))
            FROM=$(grep '"from":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"from": *"\([^"]*\)".*/\1/')
            MSG=$(grep '"message":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"message": *"\([^"]*\)".*/\1/' | cut -c1-100)
            TIMESTAMP=$(grep '"timestamp":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"timestamp": *"\([^"]*\)".*/\1/' | cut -c1-19)

            if [ -n "$FROM" ] && [ -n "$MSG" ]; then
                MESSAGES="${MESSAGES}  [${TIMESTAMP}] ${FROM}: ${MSG}\n"
            fi
        fi
    done

    if [ "$MESSAGE_COUNT" -gt 0 ]; then
        # Escape for JSON output
        MESSAGES_ESCAPED=$(echo -e "$MESSAGES" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | tr '\n' ' ' | sed 's/  */ /g')
        cat <<EOF
{
  "additionalContext": "📬 INBOX (${MESSAGE_COUNT} unread):\\n${MESSAGES_ESCAPED}\\nCall idlergear_message_list() for full messages."
}
EOF
    fi
fi

exit 0
