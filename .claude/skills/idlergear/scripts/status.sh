#!/bin/bash
# Zero-context script: Quick project status
# Output is returned to Claude, script content is NOT loaded into context

detailed="${1:-false}"
if [ "$detailed" = "true" ] || [ "$detailed" = "--detailed" ]; then
    idlergear status --detailed
else
    idlergear status
fi
