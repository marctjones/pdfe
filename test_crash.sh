#!/bin/bash
cd /home/marc/pdfe/PdfEditor
echo "========================================="
echo "Starting PdfEditor with full logging..."
echo "========================================="
echo ""
echo "Please:"
echo "1. Wait for the app to start"
echo "2. Click 'Open' and select a PDF"
echo "3. Watch the console output"
echo "4. If it crashes, the last log line will show where"
echo ""
echo "========================================="
echo "Log output:"
echo "========================================="
dotnet run 2>&1 | tee /tmp/pdfe_crash_log.txt
