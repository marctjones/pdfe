#!/bin/bash

echo "=================================="
echo "PDF Editor - Build Script"
echo "=================================="
echo ""

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK is not installed"
    echo ""
    echo "Please install .NET 8.0 SDK:"
    echo "  Linux: wget https://dot.net/v1/dotnet-install.sh && chmod +x dotnet-install.sh && ./dotnet-install.sh --channel 8.0"
    echo "  macOS: brew install dotnet@8"
    echo "  Windows: winget install Microsoft.DotNet.SDK.8"
    exit 1
fi

echo "✓ .NET SDK found: $(dotnet --version)"
echo ""

# Navigate to project directory
cd "$(dirname "$0")/PdfEditor"

echo "Restoring NuGet packages..."
dotnet restore

if [ $? -ne 0 ]; then
    echo "ERROR: Failed to restore packages"
    exit 1
fi

echo ""
echo "Building project..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    exit 1
fi

echo ""
echo "=================================="
echo "✓ Build successful!"
echo "=================================="
echo ""
echo "To run the application:"
echo "  dotnet run --project PdfEditor"
echo ""
echo "To publish a standalone executable:"
echo "  Linux:   dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true"
echo "  macOS:   dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true"
echo "  Windows: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true"
echo ""
