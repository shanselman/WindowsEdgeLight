<#
.SYNOPSIS
    Helper script to update Winget manifest files for a new version of Windows Edge Light.

.DESCRIPTION
    This script automates the process of creating or updating Winget manifest files:
    - Downloads release artifacts from GitHub
    - Calculates SHA256 hashes
    - Updates manifest files with correct version and hashes
    - Optionally validates the manifests

.PARAMETER Version
    The version number to create manifests for (e.g., "0.6.0")

.PARAMETER SkipDownload
    Skip downloading files if they already exist locally

.PARAMETER SkipValidation
    Skip manifest validation with winget

.EXAMPLE
    .\update-winget-manifest.ps1 -Version "0.6.0"
    
.EXAMPLE
    .\update-winget-manifest.ps1 -Version "0.7.0" -SkipValidation
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [switch]$SkipDownload,
    
    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"

# Configuration
$PackageId = "ScottHanselman.WindowsEdgeLight"
$Publisher = "ScottHanselman"
$AppName = "WindowsEdgeLight"
$GitHubRepo = "shanselman/WindowsEdgeLight"

# Paths
$ManifestsRoot = Join-Path $PSScriptRoot "manifests"
$VersionPath = Join-Path $ManifestsRoot "s\$Publisher\$AppName\$Version"
$TempPath = Join-Path $PSScriptRoot "temp"

Write-Host "Updating Winget manifests for $PackageId version $Version" -ForegroundColor Cyan

# Create directories
New-Item -ItemType Directory -Path $VersionPath -Force | Out-Null
New-Item -ItemType Directory -Path $TempPath -Force | Out-Null

# Download release files
$x64Url = "https://github.com/$GitHubRepo/releases/download/v$Version/WindowsEdgeLight-v$Version-win-x64.zip"
$arm64Url = "https://github.com/$GitHubRepo/releases/download/v$Version/WindowsEdgeLight-v$Version-win-arm64.zip"

$x64File = Join-Path $TempPath "WindowsEdgeLight-v$Version-win-x64.zip"
$arm64File = Join-Path $TempPath "WindowsEdgeLight-v$Version-win-arm64.zip"

if (-not $SkipDownload) {
    Write-Host "Downloading release artifacts..." -ForegroundColor Yellow
    
    try {
        Write-Host "  Downloading x64..." -NoNewline
        Invoke-WebRequest -Uri $x64Url -OutFile $x64File -ErrorAction Stop
        Write-Host " Done" -ForegroundColor Green
        
        Write-Host "  Downloading ARM64..." -NoNewline
        Invoke-WebRequest -Uri $arm64Url -OutFile $arm64File -ErrorAction Stop
        Write-Host " Done" -ForegroundColor Green
    }
    catch {
        Write-Host " Failed" -ForegroundColor Red
        Write-Error "Failed to download release artifacts. Make sure version $Version is released on GitHub."
        exit 1
    }
}
else {
    Write-Host "Skipping download (using existing files)" -ForegroundColor Yellow
    if (-not (Test-Path $x64File) -or -not (Test-Path $arm64File)) {
        Write-Error "Release files not found in temp directory. Remove -SkipDownload to download them."
        exit 1
    }
}

# Calculate SHA256 hashes
Write-Host "Calculating SHA256 hashes..." -ForegroundColor Yellow
$x64Hash = (Get-FileHash -Algorithm SHA256 -Path $x64File).Hash
$arm64Hash = (Get-FileHash -Algorithm SHA256 -Path $arm64File).Hash

Write-Host "  x64:   $x64Hash" -ForegroundColor Gray
Write-Host "  ARM64: $arm64Hash" -ForegroundColor Gray

# Get current date in ISO format
$releaseDate = Get-Date -Format "yyyy-MM-dd"

# Create version manifest
$versionManifest = @"
PackageIdentifier: $PackageId
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"@

$versionFile = Join-Path $VersionPath "$PackageId.yaml"
$versionManifest | Out-File -FilePath $versionFile -Encoding utf8 -NoNewline
Write-Host "Created: $versionFile" -ForegroundColor Green

# Create installer manifest
$installerManifest = @"
PackageIdentifier: $PackageId
PackageVersion: $Version
Platform:
  - Windows.Desktop
MinimumOSVersion: 10.0.0.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: WindowsEdgeLight.exe
    PortableCommandAlias: WindowsEdgeLight
Commands:
  - WindowsEdgeLight
ReleaseDate: $releaseDate
Installers:
  - Architecture: x64
    InstallerUrl: $x64Url
    InstallerSha256: $x64Hash
  - Architecture: arm64
    InstallerUrl: $arm64Url
    InstallerSha256: $arm64Hash
ManifestType: installer
ManifestVersion: 1.10.0
"@

$installerFile = Join-Path $VersionPath "$PackageId.installer.yaml"
$installerManifest | Out-File -FilePath $installerFile -Encoding utf8 -NoNewline
Write-Host "Created: $installerFile" -ForegroundColor Green

# Create locale manifest
$localeManifest = @"
PackageIdentifier: $PackageId
PackageVersion: $Version
PackageLocale: en-US
Publisher: Scott Hanselman
PublisherUrl: https://github.com/shanselman
PublisherSupportUrl: https://github.com/$GitHubRepo/issues
Author: Scott Hanselman
PackageName: Windows Edge Light
PackageUrl: https://github.com/$GitHubRepo
License: Proprietary
LicenseUrl: https://github.com/$GitHubRepo/blob/main/README.md
Copyright: Copyright © 2025 Scott Hanselman
ShortDescription: A lightweight WPF application that adds a customizable glowing edge light effect around your primary monitor
Description: |-
  Windows Edge Light is a lightweight WPF application that adds a customizable glowing edge light effect around your primary monitor on Windows. Perfect for ambient lighting during video calls, streaming, or just adding a professional touch to your workspace.
  
  Features:
  - Automatic Updates: Built-in update system checks GitHub Releases for new versions
  - Primary Monitor Display: Automatically detects and displays on your primary monitor, even in multi-monitor setups
  - DPI Aware: Properly handles high-DPI displays (4K monitors with scaling)
  - Fluent Design: Modern UX that fits in with the Windows look and feel
  - Click-Through Transparency: Overlay doesn't interfere with your work - all clicks pass through to applications beneath
  - Customizable Brightness: Adjust opacity with easy-to-use controls
  - Adjustable Color Temperature: Shift the edge light from cooler (blue-ish) to warmer (amber) tones
  - Toggle On/Off: Quickly enable or disable the edge light effect
  - Hideable Controls: Hide the control toolbar for a cleaner look, restore via tray menu
  - Always On Top: Stays visible above all other windows
  - Exclude from Screen Capture: Optional setting to hide the edge light from screen sharing (Teams, Zoom) and screenshots
  - Keyboard Shortcuts: Ctrl+Shift+L (Toggle), Ctrl+Shift+Up (Increase brightness), Ctrl+Shift+Down (Decrease brightness)
Moniker: edge-light
Tags:
  - ambient-light
  - edge-light
  - monitor
  - lighting
  - video-call
  - streaming
  - wpf
  - overlay
  - screen-light
  - glow-effect
ReleaseNotesUrl: https://github.com/$GitHubRepo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@

$localeFile = Join-Path $VersionPath "$PackageId.locale.en-US.yaml"
$localeManifest | Out-File -FilePath $localeFile -Encoding utf8 -NoNewline
Write-Host "Created: $localeFile" -ForegroundColor Green

# Validate manifests
if (-not $SkipValidation) {
    Write-Host "`nValidating manifests..." -ForegroundColor Yellow
    
    # Check for placeholder hashes
    $installerContent = Get-Content $installerFile -Raw
    if ($installerContent -match 'YOUR_SHA256_HASH_HERE') {
        Write-Host "ERROR: Placeholder SHA256 hashes detected!" -ForegroundColor Red
        Write-Host "This should not happen. The script should have calculated real hashes." -ForegroundColor Red
        exit 1
    }
    
    # Check if winget is available
    $wingetAvailable = Get-Command winget -ErrorAction SilentlyContinue
    
    if ($wingetAvailable) {
        try {
            winget validate --manifest $VersionPath
            Write-Host "Validation passed!" -ForegroundColor Green
        }
        catch {
            Write-Host "Validation failed!" -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor Red
            exit 1
        }
    }
    else {
        Write-Host "Warning: winget not found. Skipping validation." -ForegroundColor Yellow
        Write-Host "Install winget to validate manifests before submission." -ForegroundColor Yellow
    }
}

Write-Host "`nManifest files created successfully!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Review the manifest files in: $VersionPath"
Write-Host "2. Test locally: winget install --manifest $VersionPath"
Write-Host "3. Submit to winget-pkgs using one of these methods:"
Write-Host "   - WingetCreate: wingetcreate update $PackageId --urls $x64Url $arm64Url --version $Version --submit"
Write-Host "   - Manual PR: Copy manifests to your winget-pkgs fork and create a PR"
Write-Host "`nSee docs/WINGET.md for detailed submission instructions."

# Cleanup temp files
Write-Host "`nCleaning up temporary files..." -ForegroundColor Yellow
Remove-Item $TempPath -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Done!" -ForegroundColor Green
