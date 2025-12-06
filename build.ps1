# Build script for Windows Edge Light
# Creates x64 and ARM64 single-file executables

param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

# Target framework - must match csproj
$TargetFramework = "net10.0-windows10.0.19041.0"

# If version not provided, read from csproj
if ([string]::IsNullOrEmpty($Version)) {
    Write-Host "Version not specified, reading from project file..." -ForegroundColor Yellow
    [xml]$projectFile = Get-Content "WindowsEdgeLight\WindowsEdgeLight.csproj"
    $Version = $projectFile.Project.PropertyGroup.Version | Where-Object { $_ -ne $null } | Select-Object -First 1
    
    if ([string]::IsNullOrEmpty($Version)) {
        Write-Host "ERROR: Could not read version from WindowsEdgeLight.csproj" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Using version from project file: $Version" -ForegroundColor Green
}

Write-Host "Building Windows Edge Light v$Version..." -ForegroundColor Cyan
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\publish") {
    Remove-Item ".\publish" -Recurse -Force
}
New-Item -ItemType Directory -Path ".\publish" | Out-Null

# Build x64 version
Write-Host ""
Write-Host "Building x64 version..." -ForegroundColor Green
dotnet publish WindowsEdgeLight\WindowsEdgeLight.csproj `
    -c $Configuration `
    -r win-x64 `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    --self-contained

if ($LASTEXITCODE -eq 0) {
    Copy-Item "WindowsEdgeLight\bin\$Configuration\$TargetFramework\win-x64\publish\WindowsEdgeLight.exe" `
              ".\publish\WindowsEdgeLight-v$Version-win-x64.exe"
    $x64Size = [math]::Round((Get-Item ".\publish\WindowsEdgeLight-v$Version-win-x64.exe").Length / 1MB, 2)
    Write-Host "✓ x64 build complete: $x64Size MB" -ForegroundColor Green
} else {
    Write-Host "✗ x64 build failed" -ForegroundColor Red
    exit 1
}

# Build ARM64 version
Write-Host ""
Write-Host "Building ARM64 version..." -ForegroundColor Green
dotnet publish WindowsEdgeLight\WindowsEdgeLight.csproj `
    -c $Configuration `
    -r win-arm64 `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    --self-contained

if ($LASTEXITCODE -eq 0) {
    Copy-Item "WindowsEdgeLight\bin\$Configuration\$TargetFramework\win-arm64\publish\WindowsEdgeLight.exe" `
              ".\publish\WindowsEdgeLight-v$Version-win-arm64.exe"
    $arm64Size = [math]::Round((Get-Item ".\publish\WindowsEdgeLight-v$Version-win-arm64.exe").Length / 1MB, 2)
    Write-Host "✓ ARM64 build complete: $arm64Size MB" -ForegroundColor Green
} else {
    Write-Host "✗ ARM64 build failed" -ForegroundColor Red
    exit 1
}

# Summary
Write-Host ""
Write-Host "Build Complete!" -ForegroundColor Cyan
Write-Host "==================" -ForegroundColor Cyan
Get-ChildItem ".\publish\*.exe" | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "$($_.Name) - $size MB" -ForegroundColor White
}
Write-Host ""
Write-Host "Output directory: .\publish\" -ForegroundColor Yellow
