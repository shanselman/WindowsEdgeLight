# Winget Manifests

This directory contains Winget manifest files for Windows Edge Light.

## Directory Structure

Manifests follow the Winget repository structure:
```
manifests/
└── s/                          # First letter of publisher
    └── ScottHanselman/         # Publisher name
        └── WindowsEdgeLight/   # Application name
            └── 0.6.0/          # Version number
                ├── ScottHanselman.WindowsEdgeLight.yaml                 # Version manifest
                ├── ScottHanselman.WindowsEdgeLight.installer.yaml       # Installer details
                └── ScottHanselman.WindowsEdgeLight.locale.en-US.yaml    # Metadata/localization
```

## Updating for New Versions

### Using the Helper Script (Recommended)

Run the PowerShell script from the repository root:

```powershell
.\update-winget-manifest.ps1 -Version "0.7.0"
```

This will:
1. Download the release artifacts from GitHub
2. Calculate SHA256 hashes automatically
3. Generate all three manifest files
4. Validate the manifests (if winget is installed)

### Manual Update

1. Copy an existing version folder (e.g., `0.6.0`) to a new folder with the new version number
2. Update all three YAML files:
   - Change `PackageVersion` in all files
   - Update `InstallerUrl` in the installer manifest
   - Download the release files and calculate their SHA256 hashes
   - Update `InstallerSha256` in the installer manifest
   - Update `ReleaseDate` in the installer manifest
   - Update `ReleaseNotesUrl` in the locale manifest

## Submitting to Winget

See [docs/WINGET.md](../docs/WINGET.md) for detailed submission instructions.

### Quick Submit with WingetCreate

```bash
wingetcreate update ScottHanselman.WindowsEdgeLight \
  --urls https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.7.0/WindowsEdgeLight-v0.7.0-win-x64.zip https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.7.0/WindowsEdgeLight-v0.7.0-win-arm64.zip \
  --version 0.7.0 \
  --submit
```

## Testing Locally

Before submitting, test the manifest:

```bash
# Validate
winget validate --manifest manifests/s/ScottHanselman/WindowsEdgeLight/0.7.0

# Test install
winget install --manifest manifests/s/ScottHanselman/WindowsEdgeLight/0.7.0
```

## Important Notes

- **SHA256 hashes must match exactly** - download the actual release files to calculate them
- **URLs must be direct downloads** - GitHub release URLs are perfect
- **Version numbers must match** across all three files
- **Release must exist on GitHub** before creating/submitting manifests
- **Keep manifests for all versions** - Winget repository maintains history

## Resources

- [Winget Manifest Schema](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.10.0)
- [Submission Guide](../docs/WINGET.md)
- [Automation Options](../docs/WINGET_AUTOMATION.md)
