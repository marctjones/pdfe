#!/bin/bash
# Enable safe mode
set -euo pipefail

# Define log file
LOG_FILE="run_output.txt"

echo "Starting PdfEditor..."
echo "Logs will be written to $LOG_FILE"

# Run the application and redirect output to log file
# Redirecting both stdout and stderr
dotnet run --project PdfEditor/PdfEditor.csproj > "$LOG_FILE" 2>&1
