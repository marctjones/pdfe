#!/bin/bash
# Run redaction demonstration
# Creates sample PDFs, redacts them, and verifies redaction with multiple tools

set -e

# Source common functions
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

# Initialize logging
init_logging "demo"

# Configuration
OUTPUT_DIR="$PROJECT_ROOT/demo_output"
VERBOSE=false
SKIP_VERIFICATION=false
KEEP_FILES=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --output|-o)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --verbose|-v)
            VERBOSE=true
            shift
            ;;
        --skip-verify)
            SKIP_VERIFICATION=true
            shift
            ;;
        --keep)
            KEEP_FILES=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --output, -o DIR  Output directory for demo files (default: ./demo_output)"
            echo "  --verbose, -v     Show detailed output"
            echo "  --skip-verify     Skip verification with external tools"
            echo "  --keep            Keep temporary files after demo"
            echo "  --help, -h        Show this help"
            echo ""
            echo "This script demonstrates PDF redaction by:"
            echo "  1. Creating sample PDFs with sensitive data"
            echo "  2. Redacting the sensitive data"
            echo "  3. Verifying redaction with multiple tools (pdftotext, qpdf, strings)"
            echo ""
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

log_section "PDF Redaction Demonstration"

log_info "Output directory: $OUTPUT_DIR"
log ""

# Find .NET
find_dotnet || exit 1

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Check for external tools
check_tools() {
    log_section "Checking Validation Tools"

    local tools_available=0

    if check_command pdftotext; then
        log_success "pdftotext: Available"
        tools_available=$((tools_available + 1))
    else
        log_warning "pdftotext: Not found (apt-get install poppler-utils)"
    fi

    if check_command qpdf; then
        log_success "qpdf: Available"
        tools_available=$((tools_available + 1))
    else
        log_warning "qpdf: Not found (apt-get install qpdf)"
    fi

    if check_command strings; then
        log_success "strings: Available"
        tools_available=$((tools_available + 1))
    else
        log_warning "strings: Not found"
    fi

    if check_command mutool; then
        log_success "mutool: Available"
        tools_available=$((tools_available + 1))
    else
        log_warning "mutool: Not found (apt-get install mupdf-tools)"
    fi

    log ""
    log_info "Validation tools available: $tools_available/4"

    return $tools_available
}

# Create a simple C# program to generate and redact PDFs
create_demo_program() {
    log_section "Creating Demo Program"

    local demo_cs="$OUTPUT_DIR/RedactionDemo.cs"

    cat > "$demo_cs" << 'CSHARP'
using System;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Avalonia;

// Set up font resolver
if (GlobalFontSettings.FontResolver == null)
{
    GlobalFontSettings.FontResolver = new SystemFontResolver();
}

var outputDir = args.Length > 0 ? args[0] : "./demo_output";
Directory.CreateDirectory(outputDir);

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<RedactionService>();
var redactionService = new RedactionService(logger, loggerFactory);

Console.WriteLine("=== PDF Redaction Demo ===\n");

// Demo 1: Simple text redaction
Console.WriteLine("Demo 1: Simple Text Redaction");
Console.WriteLine("-----------------------------");

var simplePdf = Path.Combine(outputDir, "demo1_simple.pdf");
var simpleRedacted = Path.Combine(outputDir, "demo1_simple_redacted.pdf");

// Create PDF with sensitive data
using (var doc = new PdfDocument())
{
    var page = doc.AddPage();
    using (var gfx = XGraphics.FromPdfPage(page))
    {
        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 12);

        gfx.DrawString("Employee Record", titleFont, XBrushes.Black, new XPoint(72, 72));
        gfx.DrawString("Name: JOHN_DOE_SECRET", bodyFont, XBrushes.Black, new XPoint(72, 120));
        gfx.DrawString("SSN: 123-45-6789", bodyFont, XBrushes.Black, new XPoint(72, 150));
        gfx.DrawString("Email: john.doe@secret.com", bodyFont, XBrushes.Black, new XPoint(72, 180));
        gfx.DrawString("Department: Engineering", bodyFont, XBrushes.Black, new XPoint(72, 210));
    }
    doc.Save(simplePdf);
}
Console.WriteLine($"  Created: {simplePdf}");

// Redact sensitive fields
using (var doc = PdfReader.Open(simplePdf, PdfDocumentOpenMode.Modify))
{
    var page = doc.Pages[0];
    var pageHeight = page.Height.Point;

    // Redact name
    redactionService.RedactArea(page, new Rect(120, pageHeight - 120 - 20, 200, 20), renderDpi: 72);
    // Redact SSN
    redactionService.RedactArea(page, new Rect(100, pageHeight - 150 - 20, 120, 20), renderDpi: 72);
    // Redact email
    redactionService.RedactArea(page, new Rect(110, pageHeight - 180 - 20, 180, 20), renderDpi: 72);

    doc.Save(simpleRedacted);
}
Console.WriteLine($"  Redacted: {simpleRedacted}");
Console.WriteLine("  Redacted fields: Name, SSN, Email");
Console.WriteLine();

// Demo 2: Legal document
Console.WriteLine("Demo 2: Legal Document Redaction");
Console.WriteLine("--------------------------------");

var legalPdf = Path.Combine(outputDir, "demo2_legal.pdf");
var legalRedacted = Path.Combine(outputDir, "demo2_legal_redacted.pdf");

using (var doc = new PdfDocument())
{
    var page = doc.AddPage();
    using (var gfx = XGraphics.FromPdfPage(page))
    {
        var titleFont = new XFont("Arial", 14, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 10);

        gfx.DrawString("SETTLEMENT AGREEMENT", titleFont, XBrushes.Black, new XPoint(72, 72));
        gfx.DrawString("Case No: 2024-CV-SECRET-12345", bodyFont, XBrushes.Black, new XPoint(72, 110));
        gfx.DrawString("Between: PLAINTIFF_JANE_SMITH", bodyFont, XBrushes.Black, new XPoint(72, 140));
        gfx.DrawString("And: DEFENDANT_CORP_INC", bodyFont, XBrushes.Black, new XPoint(72, 160));
        gfx.DrawString("Settlement Amount: $5,750,000.00", bodyFont, XBrushes.Black, new XPoint(72, 200));
        gfx.DrawString("This agreement is confidential.", bodyFont, XBrushes.Black, new XPoint(72, 240));
    }
    doc.Save(legalPdf);
}
Console.WriteLine($"  Created: {legalPdf}");

using (var doc = PdfReader.Open(legalPdf, PdfDocumentOpenMode.Modify))
{
    var page = doc.Pages[0];
    var pageHeight = page.Height.Point;

    // Redact case number
    redactionService.RedactArea(page, new Rect(140, pageHeight - 110 - 15, 180, 15), renderDpi: 72);
    // Redact plaintiff
    redactionService.RedactArea(page, new Rect(140, pageHeight - 140 - 15, 180, 15), renderDpi: 72);
    // Redact defendant
    redactionService.RedactArea(page, new Rect(100, pageHeight - 160 - 15, 180, 15), renderDpi: 72);
    // Redact amount
    redactionService.RedactArea(page, new Rect(180, pageHeight - 200 - 15, 120, 15), renderDpi: 72);

    doc.Save(legalRedacted);
}
Console.WriteLine($"  Redacted: {legalRedacted}");
Console.WriteLine("  Redacted fields: Case No, Plaintiff, Defendant, Amount");
Console.WriteLine();

// Demo 3: With metadata sanitization
Console.WriteLine("Demo 3: Redaction with Metadata Sanitization");
Console.WriteLine("--------------------------------------------");

var metadataPdf = Path.Combine(outputDir, "demo3_metadata.pdf");
var metadataRedacted = Path.Combine(outputDir, "demo3_metadata_redacted.pdf");

using (var doc = new PdfDocument())
{
    doc.Info.Title = "Report on PROJECT_ALPHA";
    doc.Info.Author = "SECRET_AUTHOR";
    doc.Info.Subject = "Confidential PROJECT_ALPHA analysis";

    var page = doc.AddPage();
    using (var gfx = XGraphics.FromPdfPage(page))
    {
        var font = new XFont("Arial", 12);
        gfx.DrawString("PROJECT_ALPHA Status Report", font, XBrushes.Black, new XPoint(72, 100));
        gfx.DrawString("Prepared by: SECRET_AUTHOR", font, XBrushes.Black, new XPoint(72, 130));
        gfx.DrawString("Public summary information", font, XBrushes.Black, new XPoint(72, 180));
    }
    doc.Save(metadataPdf);
}
Console.WriteLine($"  Created: {metadataPdf}");

using (var doc = PdfReader.Open(metadataPdf, PdfDocumentOpenMode.Modify))
{
    var page = doc.Pages[0];
    var pageHeight = page.Height.Point;

    var options = new RedactionOptions { SanitizeMetadata = true };
    var areas = new List<Rect>
    {
        new Rect(70, pageHeight - 100 - 20, 250, 20),
        new Rect(130, pageHeight - 130 - 20, 150, 20)
    };

    redactionService.RedactWithOptions(doc, page, areas, options, renderDpi: 72);
    doc.Save(metadataRedacted);
}
Console.WriteLine($"  Redacted: {metadataRedacted}");
Console.WriteLine("  Redacted: PROJECT_ALPHA, SECRET_AUTHOR (from content AND metadata)");
Console.WriteLine();

Console.WriteLine("=== Demo Complete ===");
Console.WriteLine($"Output files in: {outputDir}");

// Simple font resolver for demo
public class SystemFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo("Arial");
    }

    public byte[]? GetFont(string faceName)
    {
        // Try common font locations
        string[] fontPaths = {
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "C:\\Windows\\Fonts\\arial.ttf",
            "/System/Library/Fonts/Helvetica.ttc"
        };

        foreach (var path in fontPaths)
        {
            if (File.Exists(path))
                return File.ReadAllBytes(path);
        }

        return null;
    }
}
CSHARP

    log_success "Demo program created"
}

# Verify redaction with external tools
verify_redaction() {
    local original="$1"
    local redacted="$2"
    local search_terms="$3"

    log ""
    log_info "Verifying: $(basename "$redacted")"

    local failures=0

    # Check with pdftotext
    if check_command pdftotext; then
        local text=$(pdftotext "$redacted" - 2>/dev/null)
        local found=false

        for term in $search_terms; do
            if echo "$text" | grep -q "$term"; then
                log_error "  pdftotext: FOUND '$term'"
                found=true
                failures=$((failures + 1))
            fi
        done

        if [ "$found" = false ]; then
            log_success "  pdftotext: Clean"
        fi
    fi

    # Check with qpdf
    if check_command qpdf; then
        local qdf_file="/tmp/verify_$$.qdf"
        qpdf --qdf --object-streams=disable "$redacted" "$qdf_file" 2>/dev/null
        local found=false

        for term in $search_terms; do
            if grep -q "$term" "$qdf_file"; then
                log_error "  qpdf streams: FOUND '$term'"
                found=true
                failures=$((failures + 1))
            fi
        done

        if [ "$found" = false ]; then
            log_success "  qpdf streams: Clean"
        fi

        rm -f "$qdf_file"
    fi

    # Check with strings
    if check_command strings; then
        local found=false

        for term in $search_terms; do
            if strings "$redacted" | grep -q "$term"; then
                log_error "  strings: FOUND '$term'"
                found=true
                failures=$((failures + 1))
            fi
        done

        if [ "$found" = false ]; then
            log_success "  strings: Clean"
        fi
    fi

    return $failures
}

# Main execution
main() {
    check_tools

    # Build the demo using dotnet script
    log_section "Running Redaction Demos"

    cd "$PROJECT_ROOT"

    # Check if we have a demo project
    if [ -d "PdfEditor.Demo" ]; then
        log_info "Building demo project..."
        $DOTNET_CMD build PdfEditor.Demo -c Release >> "$LOG_FILE" 2>&1

        log_info "Running demo..."
        $DOTNET_CMD run --project PdfEditor.Demo -c Release --no-build -- "$OUTPUT_DIR" 2>&1 | tee -a "$LOG_FILE"
    else
        # Fall back to inline demo using C# scripting
        create_demo_program

        log_info "Running demo program..."
        cd "$OUTPUT_DIR"

        # Create a simple project to run the demo
        cat > demo.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../PdfEditor/PdfEditor.csproj" />
  </ItemGroup>
</Project>
EOF
        mv RedactionDemo.cs Program.cs

        $DOTNET_CMD run -c Release 2>&1 | tee -a "$LOG_FILE"
        cd "$PROJECT_ROOT"
    fi

    # Verify redactions
    if [ "$SKIP_VERIFICATION" = false ]; then
        log_section "Verifying Redactions"

        local total_failures=0

        # Verify demo 1
        if [ -f "$OUTPUT_DIR/demo1_simple_redacted.pdf" ]; then
            verify_redaction \
                "$OUTPUT_DIR/demo1_simple.pdf" \
                "$OUTPUT_DIR/demo1_simple_redacted.pdf" \
                "JOHN_DOE_SECRET 123-45-6789 john.doe@secret.com"
            total_failures=$((total_failures + $?))
        fi

        # Verify demo 2
        if [ -f "$OUTPUT_DIR/demo2_legal_redacted.pdf" ]; then
            verify_redaction \
                "$OUTPUT_DIR/demo2_legal.pdf" \
                "$OUTPUT_DIR/demo2_legal_redacted.pdf" \
                "SECRET-12345 PLAINTIFF_JANE_SMITH DEFENDANT_CORP_INC 5,750,000"
            total_failures=$((total_failures + $?))
        fi

        # Verify demo 3
        if [ -f "$OUTPUT_DIR/demo3_metadata_redacted.pdf" ]; then
            verify_redaction \
                "$OUTPUT_DIR/demo3_metadata.pdf" \
                "$OUTPUT_DIR/demo3_metadata_redacted.pdf" \
                "PROJECT_ALPHA SECRET_AUTHOR"
            total_failures=$((total_failures + $?))
        fi

        log ""
        if [ $total_failures -eq 0 ]; then
            log_success "All verifications passed!"
        else
            log_error "Total verification failures: $total_failures"
        fi
    fi

    # Cleanup
    if [ "$KEEP_FILES" = false ] && [ -f "$OUTPUT_DIR/demo.csproj" ]; then
        rm -f "$OUTPUT_DIR/demo.csproj" "$OUTPUT_DIR/Program.cs"
        rm -rf "$OUTPUT_DIR/bin" "$OUTPUT_DIR/obj"
    fi

    print_summary "demo" "success"

    log "Demo output files:"
    ls -lh "$OUTPUT_DIR"/*.pdf 2>/dev/null | while read line; do
        log "  $line"
    done
    log ""
}

main "$@"
