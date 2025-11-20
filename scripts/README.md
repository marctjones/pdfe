# PDF Editor Scripts

Standardized shell scripts for development, testing, and demonstration.

## Quick Start

```bash
# Set up development environment (installs .NET, tools, dependencies)
./scripts/setup-dev.sh

# Build all projects
./scripts/build.sh

# Run tests
./scripts/test.sh

# Run redaction demo
./scripts/demo.sh

# Launch GUI
./scripts/gui.sh
```

## Scripts

### setup-dev.sh
Sets up the complete development environment with **no user interaction**.

```bash
./scripts/setup-dev.sh
```

**What it does:**
- Detects OS (Ubuntu, Fedora, Arch, macOS)
- Installs system dependencies (libgdiplus, fonts)
- Installs .NET 8.0 SDK
- Installs PDF validation tools (pdftotext, qpdf, mutool)
- Restores NuGet packages
- Verifies installation

### build.sh
Builds one or more projects.

```bash
./scripts/build.sh [OPTIONS] [PROJECT...]
```

**Projects:** `editor`, `tests`, `demo`, `all` (default)

**Options:**
- `--debug` - Build in Debug configuration
- `--release` - Build in Release configuration (default)
- `--publish` - Create standalone executable
- `--runtime, -r` - Target runtime (linux-x64, win-x64, osx-x64)
- `--verbose, -v` - Show detailed output

**Examples:**
```bash
./scripts/build.sh                        # Build all
./scripts/build.sh editor tests           # Build specific projects
./scripts/build.sh --publish -r linux-x64 # Publish standalone
```

### test.sh
Runs test suites.

```bash
./scripts/test.sh [OPTIONS] [SUITE...]
```

**Suites:** `all`, `integration`, `redaction`, `excessive`, `forensic`, `metadata`, `external`, `unit`

**Options:**
- `--verbose, -v` - Detailed test output
- `--no-build` - Skip building
- `--filter, -f` - Custom test filter
- `--list` - List available test classes

**Examples:**
```bash
./scripts/test.sh                   # Run all tests
./scripts/test.sh redaction         # Run redaction tests
./scripts/test.sh forensic external # Run multiple suites
./scripts/test.sh -f 'Manafort'     # Custom filter
```

### demo.sh
Runs redaction demonstration and verification.

```bash
./scripts/demo.sh [OPTIONS]
```

**Options:**
- `--output, -o DIR` - Output directory (default: ./demo_output)
- `--verbose, -v` - Detailed output
- `--skip-verify` - Skip external tool verification
- `--keep` - Keep temporary files

**What it does:**
1. Creates sample PDFs with sensitive data
2. Redacts the sensitive data
3. Verifies with pdftotext, qpdf, strings

### gui.sh
Launches the PDF Editor GUI.

```bash
./scripts/gui.sh [OPTIONS]
```

**Options:**
- `--debug` - Debug configuration
- `--release` - Release configuration (default)
- `--no-build` - Skip building
- `--watch` - Auto-reload on code changes
- `--verbose, -v` - Detailed output

**Examples:**
```bash
./scripts/gui.sh              # Normal launch
./scripts/gui.sh --watch      # Development with auto-reload
./scripts/gui.sh --debug      # Debug mode
```

## Logging

All scripts log to both:
- **Screen** - Real-time output with colors
- **Log file** - `logs/<script>_<timestamp>.log`

Latest log files are symlinked for easy access:
```bash
cat logs/test_latest.log
cat logs/build_latest.log
```

Log files are useful for:
- Debugging failures
- Sharing with team members
- CI/CD integration
- Claude Code analysis

## Common Functions

Scripts share common functions from `common.sh`:

```bash
# In any script:
source "$(dirname "$0")/common.sh"

init_logging "my-script"
log_info "Information message"
log_success "Success message"
log_warning "Warning message"
log_error "Error message"
log_section "Section Header"
```

## Exit Codes

- `0` - Success
- `1` - General error
- `2` - Dependency missing
- Non-zero from dotnet test indicates test failures

## Troubleshooting

### .NET not found
```bash
./scripts/setup-dev.sh
# Or manually:
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
export PATH="$HOME/.dotnet:$PATH"
```

### Display not found (Linux GUI)
```bash
# For X11
export DISPLAY=:0

# For WSL with VcXsrv
export DISPLAY=$(grep nameserver /etc/resolv.conf | awk '{print $2}'):0
```

### Tests skipped for missing tools
```bash
# Install validation tools
sudo apt-get install poppler-utils qpdf mupdf-tools
```

## Integration with Claude Code

These scripts are designed to work with Claude Code CLI:

```bash
# Claude can read logs even from another terminal
claude "analyze the test failures in logs/test_latest.log"

# Run tests and have Claude analyze results
./scripts/test.sh redaction 2>&1 | tee /tmp/test.log
claude "what failed in /tmp/test.log"
```
