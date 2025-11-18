#!/bin/bash
set -e

echo "=== PDF Redaction Demo Runner ==="
echo ""

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET 8.0 SDK is not installed"
    echo "Please install from: https://dotnet.microsoft.com/download"
    exit 1
fi

echo "✓ .NET SDK found: $(dotnet --version)"
echo ""

# Build and run the demo
cd PdfEditor.Demo

echo "Building demo program..."
dotnet build

echo ""
echo "Running demonstration..."
echo ""

dotnet run

echo ""
echo "=== Demo Complete ==="
echo ""
echo "Check the RedactionDemo directory for generated PDFs:"
ls -lh RedactionDemo/
