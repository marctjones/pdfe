#!/bin/bash
# Pre-tool-use hook: Block forbidden files and handle sudo commands

INPUT=$(cat)
TOOL=$(echo "$INPUT" | jq -r '.tool_name')
TOOL_INPUT=$(echo "$INPUT" | jq -r '.tool_input')

# === Sudo Detection for Bash commands ===
if [[ "$TOOL" == "Bash" ]]; then
    COMMAND=$(echo "$TOOL_INPUT" | jq -r '.command // empty')

    # Check if command contains sudo
    if [[ "$COMMAND" == *"sudo "* ]]; then
        # Check if sudo session is already active (no password needed)
        if sudo -n true 2>/dev/null; then
            # Already authenticated, proceed
            exit 0
        fi

        # Find ig-askpass script
        ASKPASS=""
        if command -v ig-askpass &>/dev/null; then
            ASKPASS="$(command -v ig-askpass)"
        elif [[ -x "$HOME/.local/bin/ig-askpass" ]]; then
            ASKPASS="$HOME/.local/bin/ig-askpass"
        elif [[ -x "./.claude/scripts/ig-askpass" ]]; then
            ASKPASS="./.claude/scripts/ig-askpass"
        fi

        # Check if GUI askpass is available
        if [[ -n "$ASKPASS" ]] && "$ASKPASS" --check &>/dev/null; then
            GUI_METHOD=$("$ASKPASS" --check)
            # Inform user that GUI prompt will appear
            cat >&2 <<EOF
ℹ️  sudo command detected - GUI password prompt will appear ($GUI_METHOD)
EOF
            exit 0
        fi

        # No GUI available - warn user with alternatives
        cat >&2 <<EOF
⚠️  This command requires sudo but no password prompt is available.

Options:
  1. Run 'sudo -v' in another terminal to pre-authenticate
  2. Run the command directly in your terminal:
     $COMMAND

The command will likely hang waiting for a password.
EOF
        # Don't block - just warn. User might have other auth methods.
        exit 0
    fi
    exit 0
fi

# Only check Write and Edit tools for forbidden files
if [[ "$TOOL" != "Write" && "$TOOL" != "Edit" ]]; then
    exit 0
fi

# Extract file path
FILE_PATH=$(echo "$TOOL_INPUT" | jq -r '.file_path // .path // empty')

if [ -z "$FILE_PATH" ]; then
    exit 0
fi

# Forbidden file patterns
FORBIDDEN_PATTERNS=(
    "TODO\.md"
    "NOTES\.md"
    "SESSION.*\.md"
    "BACKLOG\.md"
    "SCRATCH\.md"
    "TASKS\.md"
    "FEATURE_IDEAS\.md"
    "RESEARCH\.md"
)

# Check each pattern
for pattern in "${FORBIDDEN_PATTERNS[@]}"; do
    if echo "$FILE_PATH" | grep -qE "$pattern"; then
        # Extract base name for better error message
        BASENAME=$(basename "$FILE_PATH")

        # Suggest appropriate IdlerGear alternative
        case "$BASENAME" in
            TODO.md|TASKS.md|BACKLOG.md)
                ALTERNATIVE="idlergear task create \"...\""
                ;;
            NOTES.md|SCRATCH.md|SESSION*.md)
                ALTERNATIVE="idlergear note create \"...\""
                ;;
            FEATURE_IDEAS.md)
                ALTERNATIVE="idlergear note create \"...\" --tag idea"
                ;;
            RESEARCH.md)
                ALTERNATIVE="idlergear note create \"...\" --tag explore"
                ;;
            *)
                ALTERNATIVE="idlergear task create \"...\" or idlergear note create \"...\""
                ;;
        esac

        cat <<EOF >&2
❌ FORBIDDEN FILE: $FILE_PATH

IdlerGear projects use commands, not markdown files, for knowledge management.

Instead of creating $BASENAME, use:
  $ALTERNATIVE

Why? Knowledge in IdlerGear is:
  • Queryable (idlergear search)
  • Linkable (tasks ↔ commits ↔ notes)
  • Synced with GitHub (optional)
  • Available to all AI sessions via MCP

See CLAUDE.md for full guidelines.
EOF
        exit 2  # Exit code 2 = blocking error
    fi
done

exit 0
