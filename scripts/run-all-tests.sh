#!/bin/bash
# Run ALL tests in the solution and provide comprehensive summary
#
# Usage:
#   ./run-all-tests.sh           # Run all tests (with logging)
#   ./run-all-tests.sh --no-build # Skip build, just test (with logging)
#
# Output is automatically logged to logs/all_tests_TIMESTAMP.log
# Live output is shown on screen AND saved to log file

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
LOG_DIR="$PROJECT_ROOT/logs"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
LOG_FILE="$LOG_DIR/all_tests_$TIMESTAMP.log"

# Parse arguments
SKIP_BUILD=false
if [[ "$1" == "--no-build" ]]; then
    SKIP_BUILD=true
fi

# Create logs directory BEFORE setting up redirection
mkdir -p "$LOG_DIR"

# Redirect all output to both console and log file using tee
exec > >(tee "$LOG_FILE")
exec 2>&1

echo "================================================="
echo "pdfe Full Test Suite"
echo "================================================="
echo ""
echo "Log file: $LOG_FILE"
echo ""

cd "$PROJECT_ROOT"

# Build tests first (unless --no-build specified)
if [ "$SKIP_BUILD" = false ]; then
    echo "Building all test projects..."
    dotnet build PdfEditor.Tests/PdfEditor.Tests.csproj --nologo -v quiet
    if [ $? -ne 0 ]; then
        echo "❌ PdfEditor.Tests build failed"
        exit 1
    fi
    echo "✅ PdfEditor.Tests built successfully"
    echo ""
fi

# Run all tests
echo "Running all tests..."
echo "================================================="
echo ""

dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj \
    --logger "console;verbosity=normal" \
    --no-build

EXIT_CODE=$?

echo ""
echo "================================================="
echo "Test Results Summary"
echo "================================================="
echo ""

# Extract summary from output
if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ ALL TESTS PASSED"
else
    echo "❌ TESTS FAILED"
    echo ""
    echo "Check log for details: $LOG_FILE"
fi

echo ""
echo "Full log: $LOG_FILE"

exit $EXIT_CODE
