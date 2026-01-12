#!/bin/bash
# PostToolUse hook - Detect test failures and suggest bug task creation
# Also tracks edit count and suggests commits after multiple edits
# Periodically checks for messages from other agents

INPUT=$(cat)
TOOL=$(echo "$INPUT" | jq -r '.tool_name')
TOOL_RESPONSE=$(echo "$INPUT" | jq -r '.tool_response // empty')
SESSION_ID=$(echo "$INPUT" | jq -r '.session_id // "unknown"')

SUGGESTIONS=""

# Track edit count for commit suggestions
EDIT_COUNT_FILE="/tmp/idlergear-edit-count-${SESSION_ID}"
MESSAGE_CHECK_FILE="/tmp/idlergear-msg-check-${SESSION_ID}"

# ============================================
# PERIODIC MESSAGE CHECK (every 10 tool uses)
# ============================================
TOOL_COUNT=$(cat "$MESSAGE_CHECK_FILE" 2>/dev/null || echo 0)
TOOL_COUNT=$((TOOL_COUNT + 1))
echo "$TOOL_COUNT" > "$MESSAGE_CHECK_FILE"

# Check for messages every 10 tool uses (reduces overhead)
if [ $((TOOL_COUNT % 10)) -eq 0 ] && [ -S ".idlergear/daemon.sock" ]; then
    # Find our agent ID
    AGENT_ID=""
    if [ -d ".idlergear/agents" ]; then
        for f in .idlergear/agents/*.json; do
            [ -f "$f" ] || continue
            [ "$(basename "$f")" = "agents.json" ] && continue
            AGENT_ID=$(basename "$f" .json)
            break
        done
    fi

    # Check inbox
    if [ -n "$AGENT_ID" ] && [ -d ".idlergear/inbox/$AGENT_ID" ]; then
        MESSAGE_COUNT=0
        for msg_file in ".idlergear/inbox/$AGENT_ID/"*.json; do
            [ -f "$msg_file" ] || continue
            if grep -q '"read": false' "$msg_file" 2>/dev/null || ! grep -q '"read":' "$msg_file" 2>/dev/null; then
                MESSAGE_COUNT=$((MESSAGE_COUNT + 1))
            fi
        done

        if [ "$MESSAGE_COUNT" -gt 0 ]; then
            SUGGESTIONS="${SUGGESTIONS}📬 ${MESSAGE_COUNT} unread message(s) waiting. Use idlergear_message_list() to check.\n\n"
        fi
    fi
fi

# Count file edits
if [[ "$TOOL" == "Edit" || "$TOOL" == "Write" ]]; then
    COUNT=$(cat "$EDIT_COUNT_FILE" 2>/dev/null || echo 0)
    COUNT=$((COUNT + 1))
    echo "$COUNT" > "$EDIT_COUNT_FILE"

    # After 5 edits, suggest commit
    if [ "$COUNT" -ge 5 ]; then
        SUGGESTIONS="${SUGGESTIONS}📝 You've made ${COUNT} file changes. Consider:\n"
        SUGGESTIONS="${SUGGESTIONS}  1. Creating a git commit\n"
        SUGGESTIONS="${SUGGESTIONS}  2. Updating current task status\n"
        SUGGESTIONS="${SUGGESTIONS}  3. Creating notes for any discoveries\n\n"
        rm "$EDIT_COUNT_FILE" 2>/dev/null  # Reset counter
    fi
fi

# Detect test failures in Bash output
if [[ "$TOOL" == "Bash" ]] && [ -n "$TOOL_RESPONSE" ]; then
    # Pytest failures
    if echo "$TOOL_RESPONSE" | grep -qiE "(FAILED|ERROR.*test_|failed.*passed|=+ FAILURES =+)"; then
        # Extract test name if possible
        TEST_NAME=$(echo "$TOOL_RESPONSE" | grep -oE "test_[a-zA-Z0-9_]+" | head -1)
        if [ -n "$TEST_NAME" ]; then
            SUGGESTIONS="${SUGGESTIONS}🐛 Test failure detected: ${TEST_NAME}\n"
            SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix failing test: ${TEST_NAME}\" --label bug\n\n"
        else
            SUGGESTIONS="${SUGGESTIONS}🐛 Test failure detected\n"
            SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix failing test\" --label bug\n\n"
        fi
    fi

    # Python exceptions/tracebacks
    if echo "$TOOL_RESPONSE" | grep -qiE "(Traceback \(most recent call last\)|^[A-Z][a-z]+Error:)"; then
        ERROR_TYPE=$(echo "$TOOL_RESPONSE" | grep -oE "^[A-Z][a-z]+Error" | head -1)
        if [ -n "$ERROR_TYPE" ]; then
            SUGGESTIONS="${SUGGESTIONS}🐛 Runtime error detected: ${ERROR_TYPE}\n"
            SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix ${ERROR_TYPE}\" --label bug\n\n"
        fi
    fi

    # Assertion errors
    if echo "$TOOL_RESPONSE" | grep -qiE "AssertionError"; then
        SUGGESTIONS="${SUGGESTIONS}🐛 Assertion error detected\n"
        SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix assertion failure\" --label bug\n\n"
    fi

    # Performance issues (freeze, timeout, hang)
    if echo "$TOOL_RESPONSE" | grep -qiE "(froze|freeze|hung|timeout|timed out)"; then
        SUGGESTIONS="${SUGGESTIONS}⚠️ Performance issue detected (freeze/timeout)\n"
        SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix performance issue\" --label bug --label performance\n\n"
    fi

    # JavaScript/Node test failures
    if echo "$TOOL_RESPONSE" | grep -qiE "(✗|✕|FAIL.*spec|test.*failed)"; then
        SUGGESTIONS="${SUGGESTIONS}🐛 JavaScript test failure detected\n"
        SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix failing JS test\" --label bug\n\n"
    fi

    # Rust test failures
    if echo "$TOOL_RESPONSE" | grep -qiE "(test.*FAILED|panicked at|thread.*panicked)"; then
        SUGGESTIONS="${SUGGESTIONS}🐛 Rust test/panic detected\n"
        SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix Rust test failure\" --label bug\n\n"
    fi

    # Go test failures
    if echo "$TOOL_RESPONSE" | grep -qiE "(--- FAIL:|FAIL.*\[)"; then
        SUGGESTIONS="${SUGGESTIONS}🐛 Go test failure detected\n"
        SUGGESTIONS="${SUGGESTIONS}  Consider: idlergear task create \"Fix Go test failure\" --label bug\n\n"
    fi
fi

# Output suggestions if any
if [ -n "$SUGGESTIONS" ]; then
    # Escape for JSON
    SUGGESTIONS_ESCAPED=$(echo -e "$SUGGESTIONS" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | tr '\n' ' ' | sed 's/  */ /g')
    cat <<EOF
{
  "additionalContext": "${SUGGESTIONS_ESCAPED}"
}
EOF
fi

exit 0
