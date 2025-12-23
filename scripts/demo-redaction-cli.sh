#!/bin/bash
# Demo script for pdfe-redact CLI tool
# This demonstrates TRUE glyph-level PDF redaction

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Path to the CLI tool
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI="$SCRIPT_DIR/../PdfEditor.Redaction.Cli/bin/Release/net8.0/pdfe-redact"

# Check if CLI exists
if [ ! -f "$CLI" ]; then
    echo -e "${YELLOW}Building CLI tool...${NC}"
    dotnet build "$SCRIPT_DIR/../PdfEditor.Redaction.Cli" -c Release --no-restore
fi

# Create temp directory for demo
DEMO_DIR=$(mktemp -d)
trap "rm -rf $DEMO_DIR" EXIT

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}  pdfe-redact CLI Demo${NC}"
echo -e "${BLUE}  TRUE Glyph-Level PDF Redaction${NC}"
echo -e "${BLUE}========================================${NC}"
echo

# Step 1: Create a test PDF with sensitive data
echo -e "${YELLOW}Step 1: Creating test PDF with sensitive data...${NC}"

# Generate test PDF using inline dotnet project (no dotnet-script dependency)
mkdir -p "$DEMO_DIR/pdfgen"
cat > "$DEMO_DIR/pdfgen/Program.cs" << 'CS'
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

// Initialize font resolver for PDFsharp 6.x
GlobalFontSettings.FontResolver = new DemoFontResolver();

var doc = new PdfDocument();
var page = doc.AddPage();
using var gfx = XGraphics.FromPdfPage(page);
var titleFont = new XFont("Helvetica", 14, XFontStyleEx.Bold);
var font = new XFont("Helvetica", 12, XFontStyleEx.Regular);

string[] lines = {
    "CONFIDENTIAL EMPLOYEE RECORD",
    "",
    "Name: John Smith",
    "SSN: 123-45-6789",
    "Date of Birth: 03/15/1985",
    "Salary: $85,000",
    "Department: Engineering",
    "",
    "Emergency Contact: Jane Smith",
    "Phone: (555) 123-4567"
};

double y = 100;
gfx.DrawString(lines[0], titleFont, XBrushes.Black, new XPoint(72, y));
y += 30;
foreach (var line in lines.Skip(1)) {
    gfx.DrawString(line, font, XBrushes.Black, new XPoint(72, y));
    y += 20;
}
doc.Save(args[0]);

// Font resolver class for PDFsharp 6.x
public class DemoFontResolver : IFontResolver
{
    private static byte[]? _font;
    private static readonly string[] Paths = {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "C:\\Windows\\Fonts\\arial.ttf"
    };
    public DemoFontResolver()
    {
        if (_font == null)
        {
            foreach (var p in Paths)
                if (System.IO.File.Exists(p)) { _font = System.IO.File.ReadAllBytes(p); break; }
        }
    }
    public byte[]? GetFont(string faceName) => _font;
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new FontResolverInfo("Demo");
}
CS

cat > "$DEMO_DIR/pdfgen/pdfgen.csproj" << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="PDFsharp" Version="6.2.2" /></ItemGroup>
</Project>
PROJ

dotnet run --project "$DEMO_DIR/pdfgen" -- "$DEMO_DIR/employee_record.pdf" >/dev/null 2>&1 || {
    echo -e "${RED}Could not create test PDF.${NC}"
    exit 1
}

echo -e "${GREEN}✓ Created test PDF${NC}"
echo

# Step 2: Search for sensitive data
echo -e "${YELLOW}Step 2: Searching for SSN in the document...${NC}"
"$CLI" search "$DEMO_DIR/employee_record.pdf" "123-45-6789" || true
echo

# Step 3: Redact the SSN
echo -e "${YELLOW}Step 3: Redacting SSN from document...${NC}"
"$CLI" redact "$DEMO_DIR/employee_record.pdf" "$DEMO_DIR/redacted.pdf" "123-45-6789" --verbose
echo

# Step 4: Verify the redaction
echo -e "${YELLOW}Step 4: Verifying SSN is no longer extractable...${NC}"
"$CLI" verify "$DEMO_DIR/redacted.pdf" "123-45-6789"
echo

# Step 5: Try to extract text with pdftotext (if available)
echo -e "${YELLOW}Step 5: External verification with pdftotext...${NC}"
if command -v pdftotext &> /dev/null; then
    pdftotext "$DEMO_DIR/redacted.pdf" "$DEMO_DIR/extracted.txt"
    if grep -q "123-45-6789" "$DEMO_DIR/extracted.txt"; then
        echo -e "${RED}✗ FAIL: SSN still found by pdftotext!${NC}"
    else
        echo -e "${GREEN}✓ PASS: SSN not found by pdftotext${NC}"
    fi
    echo "  Extracted text:"
    cat "$DEMO_DIR/extracted.txt" | head -20 | sed 's/^/    /'
else
    echo "  (pdftotext not installed, skipping external verification)"
fi
echo

# Step 6: Demonstrate batch redaction
echo -e "${YELLOW}Step 6: Chaining multiple redactions...${NC}"
"$CLI" redact "$DEMO_DIR/redacted.pdf" "$DEMO_DIR/redacted2.pdf" "John Smith"
"$CLI" redact "$DEMO_DIR/redacted2.pdf" "$DEMO_DIR/final.pdf" "$85,000"
echo

# Final verification
echo -e "${YELLOW}Step 7: Final verification of all redactions...${NC}"
"$CLI" verify "$DEMO_DIR/final.pdf" "123-45-6789"
"$CLI" verify "$DEMO_DIR/final.pdf" "John Smith"
"$CLI" verify "$DEMO_DIR/final.pdf" "$85,000"
echo

echo -e "${BLUE}========================================${NC}"
echo -e "${GREEN}Demo complete!${NC}"
echo -e "${BLUE}========================================${NC}"
echo
echo "Output files in: $DEMO_DIR"
echo "  - employee_record.pdf (original)"
echo "  - redacted.pdf (SSN removed)"
echo "  - final.pdf (all PII removed)"
