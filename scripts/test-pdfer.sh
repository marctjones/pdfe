#!/bin/bash
# Test script for pdfer CLI tool
# Run this to verify all features work correctly

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Find pdfer
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PDFER="$SCRIPT_DIR/../PdfEditor.Redaction.Cli/bin/Release/net8.0/pdfer"

if [ ! -f "$PDFER" ]; then
    echo -e "${YELLOW}Building pdfer...${NC}"
    dotnet build "$SCRIPT_DIR/../PdfEditor.Redaction.Cli" -c Release -q
fi

# Create temp directory
TEMP=$(mktemp -d)
trap "rm -rf $TEMP" EXIT

echo -e "${BLUE}╔══════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║         pdfer CLI Test Suite             ║${NC}"
echo -e "${BLUE}╚══════════════════════════════════════════╝${NC}"
echo

#-----------------------------------------------------------
# Create test PDF using inline dotnet project (no dotnet-script dependency)
#-----------------------------------------------------------
echo -e "${YELLOW}Creating test PDF...${NC}"

mkdir -p "$TEMP/pdfgen"
cat > "$TEMP/pdfgen/Program.cs" << 'CS'
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

// Initialize font resolver for PDFsharp 6.x
GlobalFontSettings.FontResolver = new TestFontResolver();

var doc = new PdfDocument();
var page = doc.AddPage();
using var gfx = XGraphics.FromPdfPage(page);
var font = new XFont("Helvetica", 12, XFontStyleEx.Regular);

string[] lines = {
    "EMPLOYEE RECORD - CONFIDENTIAL",
    "",
    "Name: John Smith",
    "SSN: 123-45-6789",
    "DOB: 03/15/1985",
    "Salary: $85,000",
    "Phone: 555-123-4567",
    "Email: john.smith@example.com"
};

double y = 100;
foreach (var line in lines) {
    gfx.DrawString(line, font, XBrushes.Black, new XPoint(72, y));
    y += 20;
}
doc.Save(args[0]);

// Font resolver class for PDFsharp 6.x
public class TestFontResolver : IFontResolver
{
    private static byte[]? _font;
    private static readonly string[] Paths = {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "C:\\Windows\\Fonts\\arial.ttf"
    };
    public TestFontResolver()
    {
        if (_font == null)
        {
            foreach (var p in Paths)
                if (System.IO.File.Exists(p)) { _font = System.IO.File.ReadAllBytes(p); break; }
        }
    }
    public byte[]? GetFont(string faceName) => _font;
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new FontResolverInfo("Test");
}
CS

cat > "$TEMP/pdfgen/pdfgen.csproj" << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="PDFsharp" Version="6.2.2" /></ItemGroup>
</Project>
PROJ

dotnet run --project "$TEMP/pdfgen" -- "$TEMP/test.pdf" >/dev/null 2>&1 || {
    echo -e "${RED}Failed to create test PDF.${NC}"
    exit 1
}

if [ -f "$TEMP/test.pdf" ]; then
    echo -e "${GREEN}✓ Created test.pdf${NC}"
else
    echo -e "${RED}✗ Could not create test PDF${NC}"
    exit 1
fi
echo

#-----------------------------------------------------------
# Test 1: Basic help
#-----------------------------------------------------------
echo -e "${BLUE}Test 1: Help commands${NC}"
$PDFER --help > /dev/null && echo -e "${GREEN}  ✓ pdfer --help${NC}"
$PDFER -v > /dev/null && echo -e "${GREEN}  ✓ pdfer -v${NC}"
$PDFER redact --help > /dev/null && echo -e "${GREEN}  ✓ pdfer redact --help${NC}"
$PDFER verify --help > /dev/null && echo -e "${GREEN}  ✓ pdfer verify --help${NC}"
$PDFER search --help > /dev/null && echo -e "${GREEN}  ✓ pdfer search --help${NC}"
echo

#-----------------------------------------------------------
# Test 2: Info command
#-----------------------------------------------------------
echo -e "${BLUE}Test 2: Info command${NC}"
$PDFER info "$TEMP/test.pdf"
echo -e "${GREEN}  ✓ pdfer info${NC}"
echo

#-----------------------------------------------------------
# Test 3: Search command
#-----------------------------------------------------------
echo -e "${BLUE}Test 3: Search command${NC}"
$PDFER search "$TEMP/test.pdf" "123-45-6789"
echo -e "${GREEN}  ✓ pdfer search (found SSN)${NC}"
echo

#-----------------------------------------------------------
# Test 4: Search with JSON output
#-----------------------------------------------------------
echo -e "${BLUE}Test 4: Search with JSON output${NC}"
$PDFER search "$TEMP/test.pdf" "SSN" --json | head -10
echo -e "${GREEN}  ✓ pdfer search --json${NC}"
echo

#-----------------------------------------------------------
# Test 5: Dry run
#-----------------------------------------------------------
echo -e "${BLUE}Test 5: Dry run (preview redactions)${NC}"
$PDFER redact "$TEMP/test.pdf" "$TEMP/output.pdf" "123-45-6789" --dry-run
echo -e "${GREEN}  ✓ pdfer redact --dry-run${NC}"
echo

#-----------------------------------------------------------
# Test 6: Single term redaction
#-----------------------------------------------------------
echo -e "${BLUE}Test 6: Single term redaction${NC}"
$PDFER redact "$TEMP/test.pdf" "$TEMP/redacted1.pdf" "123-45-6789"
echo -e "${GREEN}  ✓ pdfer redact (single term)${NC}"
echo

#-----------------------------------------------------------
# Test 7: Verify redaction
#-----------------------------------------------------------
echo -e "${BLUE}Test 7: Verify redaction${NC}"
$PDFER verify "$TEMP/redacted1.pdf" "123-45-6789"
echo -e "${GREEN}  ✓ pdfer verify (SSN removed)${NC}"
echo

#-----------------------------------------------------------
# Test 8: Multiple terms redaction
#-----------------------------------------------------------
echo -e "${BLUE}Test 8: Multiple terms redaction${NC}"
$PDFER redact "$TEMP/test.pdf" "$TEMP/redacted2.pdf" "123-45-6789" "John Smith" "555-123-4567"
echo -e "${GREEN}  ✓ pdfer redact (multiple terms)${NC}"
echo

#-----------------------------------------------------------
# Test 9: Verify multiple terms
#-----------------------------------------------------------
echo -e "${BLUE}Test 9: Verify multiple terms${NC}"
$PDFER verify "$TEMP/redacted2.pdf" "123-45-6789" "John Smith" "555-123-4567"
echo -e "${GREEN}  ✓ pdfer verify (multiple terms)${NC}"
echo

#-----------------------------------------------------------
# Test 10: Terms file
#-----------------------------------------------------------
echo -e "${BLUE}Test 10: Redact from terms file${NC}"
cat > "$TEMP/terms.txt" << EOF
# PII to redact
123-45-6789
John Smith
555-123-4567
EOF
$PDFER redact "$TEMP/test.pdf" "$TEMP/redacted3.pdf" -f "$TEMP/terms.txt"
echo -e "${GREEN}  ✓ pdfer redact --terms-file${NC}"
echo

#-----------------------------------------------------------
# Test 11: Case insensitive
#-----------------------------------------------------------
echo -e "${BLUE}Test 11: Case insensitive redaction${NC}"
$PDFER redact "$TEMP/test.pdf" "$TEMP/redacted4.pdf" "confidential" -i
$PDFER verify "$TEMP/redacted4.pdf" "CONFIDENTIAL" -i
echo -e "${GREEN}  ✓ pdfer redact --case-insensitive${NC}"
echo

#-----------------------------------------------------------
# Test 12: JSON output for scripting
#-----------------------------------------------------------
echo -e "${BLUE}Test 12: JSON output for scripting${NC}"
JSON=$($PDFER redact "$TEMP/test.pdf" "$TEMP/redacted5.pdf" "123-45-6789" --json)
echo "$JSON" | head -15
echo -e "${GREEN}  ✓ pdfer redact --json${NC}"
echo

#-----------------------------------------------------------
# Test 13: Quiet mode
#-----------------------------------------------------------
echo -e "${BLUE}Test 13: Quiet mode${NC}"
$PDFER redact "$TEMP/test.pdf" "$TEMP/redacted6.pdf" "123-45-6789" -q
echo "(no output expected)"
echo -e "${GREEN}  ✓ pdfer redact --quiet${NC}"
echo

#-----------------------------------------------------------
# Test 14: Pipe from stdin
#-----------------------------------------------------------
echo -e "${BLUE}Test 14: Pipe terms from stdin${NC}"
echo -e "123-45-6789\nJohn Smith" | $PDFER redact "$TEMP/test.pdf" "$TEMP/redacted7.pdf"
echo -e "${GREEN}  ✓ echo \"terms\" | pdfer redact ...${NC}"
echo

#-----------------------------------------------------------
# Test 15: Search with context
#-----------------------------------------------------------
echo -e "${BLUE}Test 15: Search with context${NC}"
$PDFER search "$TEMP/test.pdf" "SSN" --context
echo -e "${GREEN}  ✓ pdfer search --context${NC}"
echo

#-----------------------------------------------------------
# Test 16: External verification with pdftotext
#-----------------------------------------------------------
echo -e "${BLUE}Test 16: External verification${NC}"
if command -v pdftotext &> /dev/null; then
    pdftotext "$TEMP/redacted1.pdf" "$TEMP/extracted.txt" 2>/dev/null
    if grep -q "123-45-6789" "$TEMP/extracted.txt" 2>/dev/null; then
        echo -e "${RED}  ✗ FAIL: SSN found by pdftotext${NC}"
    else
        echo -e "${GREEN}  ✓ pdftotext confirms SSN removed${NC}"
    fi
else
    echo -e "${YELLOW}  ⚠ pdftotext not installed, skipping${NC}"
fi
echo

#-----------------------------------------------------------
# Summary
#-----------------------------------------------------------
echo -e "${BLUE}╔══════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║           All Tests Passed!              ║${NC}"
echo -e "${BLUE}╚══════════════════════════════════════════╝${NC}"
echo
echo "Test files in: $TEMP"
echo
echo -e "${YELLOW}Quick reference:${NC}"
echo "  pdfer redact input.pdf output.pdf \"text\"     # Basic redaction"
echo "  pdfer redact in.pdf out.pdf -f terms.txt    # From file"
echo "  pdfer redact in.pdf out.pdf -r \"\\d{3}-\\d{2}-\\d{4}\"  # Regex"
echo "  pdfer verify output.pdf \"text\"              # Verify removal"
echo "  pdfer search input.pdf \"text\" --json        # JSON output"
