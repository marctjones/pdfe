#!/bin/bash

# GitHub Labels Setup Script
# Creates standardized labels for the PDF Editor project
# Requires: gh CLI tool (https://cli.github.com/)

set -e

echo "Setting up GitHub labels for PDF Editor..."
echo ""

# Function to create or update a label
create_label() {
    local name="$1"
    local description="$2"
    local color="$3"

    if gh label list | grep -q "^${name}"; then
        echo "Updating: $name"
        gh label edit "$name" --description "$description" --color "$color" 2>/dev/null || true
    else
        echo "Creating: $name"
        gh label create "$name" --description "$description" --color "$color"
    fi
}

echo "Creating GitHub Default Type Labels..."
create_label "bug" "Something isn't working" "d73a4a"
create_label "enhancement" "New feature or request" "a2eeef"
create_label "documentation" "Improvements or additions to documentation" "0075ca"
create_label "question" "Further information is requested" "d876e3"
create_label "duplicate" "This issue or pull request already exists" "cfd3d7"
create_label "wontfix" "This will not be worked on" "ffffff"
create_label "invalid" "This doesn't seem right" "e4e669"
create_label "help wanted" "Extra attention is needed" "008672"
create_label "good first issue" "Good for newcomers" "7057ff"
create_label "security" "Security vulnerability or concern" "ee0701"
create_label "dependencies" "Pull requests that update a dependency file" "0366d6"

echo ""
echo "Creating Component/Subsystem Labels..."
create_label "component: redaction-engine" "Content stream parsing, glyph removal" "fbca04"
create_label "component: pdf-rendering" "PDFium, image rendering, caching" "fbca04"
create_label "component: ui-framework" "Avalonia, XAML, bindings, ReactiveUI" "fbca04"
create_label "component: text-extraction" "Text extraction, OCR, search" "fbca04"
create_label "component: file-management" "Open/save, recent files, document state" "fbca04"
create_label "component: clipboard" "Copy/paste, clipboard history" "fbca04"
create_label "component: verification" "Signature/redaction verification" "fbca04"
create_label "component: coordinates" "PDF/screen coordinate systems" "fbca04"

echo ""
echo "Creating Priority Labels..."
create_label "priority: critical" "Blocks usage, data loss, security" "b60205"
create_label "priority: high" "Important but not blocking" "d93f0b"
create_label "priority: medium" "Nice to have" "fbca04"
create_label "priority: low" "Future consideration" "0e8a16"

echo ""
echo "Creating Effort Labels..."
create_label "effort: small" "< 1 hour" "c2e0c6"
create_label "effort: medium" "1-4 hours" "bfd4f2"
create_label "effort: large" "> 4 hours" "d4c5f9"

echo ""
echo "Creating Status Labels..."
create_label "status: blocked" "Waiting on something else" "d93f0b"

echo ""
echo "Creating Platform Labels..."
create_label "platform: linux" "Linux-specific issue" "ededed"
create_label "platform: windows" "Windows-specific issue" "ededed"
create_label "platform: macos" "macOS-specific issue" "ededed"

echo ""
echo "✓ GitHub labels setup complete!"
echo ""
echo "Total labels created: 32"
