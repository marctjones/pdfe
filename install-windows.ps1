#Requires -Version 5.1
<#
.SYNOPSIS
    Install PdfEditor as a Windows application for the current user.

.DESCRIPTION
    Builds and installs PdfEditor to the user's local AppData folder,
    creates Start Menu shortcuts, and optionally registers as PDF handler.

.PARAMETER NoBuild
    Skip building and use existing published files.

.PARAMETER RegisterPdfHandler
    Register PdfEditor as an option for opening PDF files.

.EXAMPLE
    .\install-windows.ps1

.EXAMPLE
    .\install-windows.ps1 -RegisterPdfHandler
#>

param(
    [switch]$NoBuild,
    [switch]$RegisterPdfHandler
)

$ErrorActionPreference = "Stop"

$AppName = "PdfEditor"
$AppDisplayName = "PDF Editor"
$Publisher = "PdfEditor"
$InstallDir = Join-Path $env:LOCALAPPDATA $AppName
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$ShortcutPath = Join-Path $StartMenuDir "$AppDisplayName.lnk"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "PdfEditor"

Write-Host "=== PdfEditor Windows Installation ===" -ForegroundColor Cyan
Write-Host ""

# Check for .NET SDK
if (-not $NoBuild) {
    $dotnetVersion = & dotnet --version 2>$null
    if (-not $dotnetVersion) {
        Write-Host "Error: .NET SDK not found. Please install from https://dotnet.microsoft.com/download" -ForegroundColor Red
        Write-Host ""
        Write-Host "Or download a pre-built release from GitHub instead." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Found .NET SDK: $dotnetVersion" -ForegroundColor Green
}

# Verify project exists
if (-not (Test-Path (Join-Path $ProjectDir "PdfEditor.csproj"))) {
    Write-Host "Error: Cannot find PdfEditor.csproj in $ProjectDir" -ForegroundColor Red
    exit 1
}

# Build
if (-not $NoBuild) {
    Write-Host ""
    Write-Host "Step 1: Building release version..." -ForegroundColor Yellow
    Push-Location $ProjectDir
    try {
        & dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $InstallDir
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
    }
    finally {
        Pop-Location
    }
    Write-Host "  Built successfully to: $InstallDir" -ForegroundColor Green
}

# Create Start Menu shortcut
Write-Host ""
Write-Host "Step 2: Creating Start Menu shortcut..." -ForegroundColor Yellow

$ExePath = Join-Path $InstallDir "PdfEditor.exe"

if (-not (Test-Path $ExePath)) {
    Write-Host "Error: Executable not found at $ExePath" -ForegroundColor Red
    exit 1
}

# Create shortcut using WScript.Shell
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "PDF viewer and editor with TRUE redaction capabilities"
$Shortcut.Save()

Write-Host "  Created: $ShortcutPath" -ForegroundColor Green

# Create Desktop shortcut (optional)
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppDisplayName.lnk"
$Shortcut2 = $WshShell.CreateShortcut($DesktopShortcut)
$Shortcut2.TargetPath = $ExePath
$Shortcut2.WorkingDirectory = $InstallDir
$Shortcut2.Description = "PDF viewer and editor with TRUE redaction capabilities"
$Shortcut2.Save()

Write-Host "  Created: $DesktopShortcut" -ForegroundColor Green

# Register as PDF handler (optional)
if ($RegisterPdfHandler) {
    Write-Host ""
    Write-Host "Step 3: Registering as PDF handler..." -ForegroundColor Yellow

    $RegPath = "HKCU:\Software\Classes\PdfEditor.pdf"
    $RegPathOpen = "$RegPath\shell\open\command"

    # Create file type association
    New-Item -Path $RegPath -Force | Out-Null
    Set-ItemProperty -Path $RegPath -Name "(Default)" -Value "PDF Document (PdfEditor)"

    New-Item -Path "$RegPath\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path $RegPathOpen -Name "(Default)" -Value "`"$ExePath`" `"%1`""

    # Add to OpenWithProgids for .pdf
    $OpenWithPath = "HKCU:\Software\Classes\.pdf\OpenWithProgids"
    if (-not (Test-Path $OpenWithPath)) {
        New-Item -Path $OpenWithPath -Force | Out-Null
    }
    Set-ItemProperty -Path $OpenWithPath -Name "PdfEditor.pdf" -Value "" -Type String

    Write-Host "  Registered PDF handler in registry" -ForegroundColor Green
    Write-Host "  Right-click a PDF -> Open with -> Choose another app -> PDF Editor" -ForegroundColor Gray
}

# Create uninstaller script in install directory
$UninstallerPath = Join-Path $InstallDir "Uninstall.ps1"
@"
# PdfEditor Uninstaller
`$ErrorActionPreference = "Stop"

Write-Host "Uninstalling PdfEditor..." -ForegroundColor Yellow

# Remove shortcuts
`$StartMenuShortcut = Join-Path `$env:APPDATA "Microsoft\Windows\Start Menu\Programs\PDF Editor.lnk"
`$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "PDF Editor.lnk"

if (Test-Path `$StartMenuShortcut) { Remove-Item `$StartMenuShortcut -Force }
if (Test-Path `$DesktopShortcut) { Remove-Item `$DesktopShortcut -Force }

# Remove registry entries
Remove-Item -Path "HKCU:\Software\Classes\PdfEditor.pdf" -Recurse -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Classes\.pdf\OpenWithProgids" -Name "PdfEditor.pdf" -ErrorAction SilentlyContinue

Write-Host "Removing application files..." -ForegroundColor Yellow
Write-Host "Please close this window and delete the folder:" -ForegroundColor Cyan
Write-Host "  `$env:LOCALAPPDATA\PdfEditor" -ForegroundColor White

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
"@ | Set-Content -Path $UninstallerPath

Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Location: $InstallDir" -ForegroundColor White
Write-Host "Start Menu: $ShortcutPath" -ForegroundColor White
Write-Host "Desktop: $DesktopShortcut" -ForegroundColor White
Write-Host ""
Write-Host "You can now:" -ForegroundColor Cyan
Write-Host "  1. Find 'PDF Editor' in your Start Menu"
Write-Host "  2. Use the Desktop shortcut"
Write-Host "  3. Run from: $ExePath"
Write-Host ""
Write-Host "To uninstall, run: $UninstallerPath" -ForegroundColor Gray
