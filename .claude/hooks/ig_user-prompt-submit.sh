#!/bin/bash
# UserPromptSubmit hook - Detect implementation commands and suggest task creation
# Also checks for pending messages from other agents
# FAST VERSION: Avoids CLI calls, uses simple pattern matching only

INPUT=$(cat)
PROMPT=$(echo "$INPUT" | jq -r '.prompt // empty' 2>/dev/null)

if [ -z "$PROMPT" ]; then
    exit 0
fi

ADDITIONAL_CONTEXT=""

# ============================================
# CHECK FOR URGENT MESSAGES FROM OTHER AGENTS
# ============================================
# Only inject URGENT messages - normal/low priority are routed to tasks
# via idlergear_message_process() MCP tool at session start
if [ -S ".idlergear/daemon.sock" ] || [ -d ".idlergear/inbox" ]; then
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

    # Check inbox for URGENT unread messages only
    if [ -n "$AGENT_ID" ] && [ -d ".idlergear/inbox/$AGENT_ID" ]; then
        URGENT_COUNT=0
        URGENT_MESSAGES=""
        NORMAL_COUNT=0

        for msg_file in $(ls -1t ".idlergear/inbox/$AGENT_ID/"*.json 2>/dev/null | head -5); do
            [ -f "$msg_file" ] || continue

            # Check if unread
            if grep -q '"read": false' "$msg_file" 2>/dev/null || ! grep -q '"read":' "$msg_file" 2>/dev/null; then
                # Check priority
                PRIORITY=$(grep '"priority":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"priority": *"\([^"]*\)".*/\1/')

                if [ "$PRIORITY" = "urgent" ]; then
                    URGENT_COUNT=$((URGENT_COUNT + 1))
                    FROM=$(grep '"from":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"from": *"\([^"]*\)".*/\1/')
                    MSG=$(grep '"message":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"message": *"\([^"]*\)".*/\1/')
                    MSG_TYPE=$(grep '"type":' "$msg_file" 2>/dev/null | head -1 | sed 's/.*"type": *"\([^"]*\)".*/\1/')
                    ACTION=$(grep '"action_requested":' "$msg_file" 2>/dev/null | head -1 | grep -q 'true' && echo " [ACTION NEEDED]" || echo "")

                    if [ -n "$FROM" ] && [ -n "$MSG" ]; then
                        URGENT_MESSAGES="${URGENT_MESSAGES}━━━ From: ${FROM} (${MSG_TYPE:-info})${ACTION} ━━━\\n${MSG}\\n\\n"
                    fi
                else
                    NORMAL_COUNT=$((NORMAL_COUNT + 1))
                fi
            fi
        done

        # Only show URGENT messages immediately
        if [ "$URGENT_COUNT" -gt 0 ]; then
            ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}🚨 URGENT MESSAGE(S) - Handle before continuing:\\n\\n${URGENT_MESSAGES}"
        fi

        # Mention normal messages exist (will become tasks)
        if [ "$NORMAL_COUNT" -gt 0 ]; then
            ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}📬 ${NORMAL_COUNT} pending message(s) to process. Run idlergear_message_process() to create tasks.\\n\\n"
        fi
    fi
fi

# Pattern: Implementation command (implement, add, create, build, write, make)
if echo "$PROMPT" | grep -qiE "^(implement|add|create|build|write|make|develop|fix) "; then
    # Extract feature name (first 5 words after the verb)
    FEATURE=$(echo "$PROMPT" | sed -E 's/^(implement|add|create|build|write|make|develop|fix) //i' | cut -d' ' -f1-5)

    ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}📋 Implementation request: \"${FEATURE}\"\n"
    ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}Consider: idlergear task create \"${FEATURE}\"\n\n"
fi

# Pattern: User asks about next steps
if echo "$PROMPT" | grep -qiE "(what.s next|what should|to do|work on|continue|where did we)"; then
    # Count open tasks from files (fast)
    TASK_COUNT=0
    if [ -d ".idlergear/tasks" ]; then
        TASK_COUNT=$(ls -1 ".idlergear/tasks/"*.md 2>/dev/null | wc -l)
    fi
    if [ "$TASK_COUNT" -gt 0 ]; then
        ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}📌 You have ${TASK_COUNT} open task(s). Run: idlergear task list\n\n"
    fi
fi

# Pattern: User mentions bugs or errors
if echo "$PROMPT" | grep -qiE "(bug|broken|error|issue|problem|failing|doesn.t work)"; then
    ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}🐛 Bug/error mentioned. When identified:\n"
    ADDITIONAL_CONTEXT="${ADDITIONAL_CONTEXT}  idlergear task create \"Fix: <description>\" --label bug\n\n"
fi

# Output additional context if any
if [ -n "$ADDITIONAL_CONTEXT" ]; then
    # Escape for JSON
    CONTEXT_ESCAPED=$(echo -e "$ADDITIONAL_CONTEXT" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | tr '\n' ' ' | sed 's/  */ /g')
    cat <<EOF
{
  "additionalContext": "${CONTEXT_ESCAPED}"
}
EOF
fi

exit 0
