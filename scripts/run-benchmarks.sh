#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet run -c Release --project PdfEditor.Benchmarks/PdfEditor.Benchmarks.csproj "$@"
