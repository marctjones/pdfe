# Download Tesseract language data files for bundling in releases

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$TessdataDir = Join-Path $ProjectRoot "tessdata"

Write-Host "Downloading Tesseract language data to $TessdataDir"

# Create tessdata directory
New-Item -ItemType Directory -Force -Path $TessdataDir | Out-Null

# Download English language data (most common)
Write-Host "Downloading English (eng) language data..."
$url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata"
$output = Join-Path $TessdataDir "eng.traineddata"
Invoke-WebRequest -Uri $url -OutFile $output

# Optional: Download additional languages
# Uncomment to include more languages in releases

# Write-Host "Downloading German (deu) language data..."
# $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/deu.traineddata"
# $output = Join-Path $TessdataDir "deu.traineddata"
# Invoke-WebRequest -Uri $url -OutFile $output

# Write-Host "Downloading French (fra) language data..."
# $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/fra.traineddata"
# $output = Join-Path $TessdataDir "fra.traineddata"
# Invoke-WebRequest -Uri $url -OutFile $output

# Write-Host "Downloading Spanish (spa) language data..."
# $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/spa.traineddata"
# $output = Join-Path $TessdataDir "spa.traineddata"
# Invoke-WebRequest -Uri $url -OutFile $output

Write-Host ""
Write-Host "Successfully downloaded Tesseract language data!"
Write-Host "Files in $TessdataDir:"
Get-ChildItem $TessdataDir | Format-Table Name, Length
