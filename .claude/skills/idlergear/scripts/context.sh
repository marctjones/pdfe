#!/bin/bash
# Zero-context script: Get project context
# Output is returned to Claude, script content is NOT loaded into context

mode="${1:-minimal}"
idlergear context --mode "$mode"
