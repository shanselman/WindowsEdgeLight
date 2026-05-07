# Windows Package Manager (Winget) Integration

This document explains how to add Windows Edge Light to the Windows Package Manager (Winget) repository.

## What is Winget?

Windows Package Manager (Winget) is Microsoft's official package manager for Windows. It allows users to discover, install, upgrade, remove and configure applications from the command line.

Once Windows Edge Light is added to Winget, users can install it with:
```bash
winget install ScottHanselman.WindowsEdgeLight
```

## Manifest Files

The `manifests/` directory contains the Winget manifest files that describe how to install Windows Edge Light. These files follow the [Winget manifest schema v1.10.0](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.10.0).

### Directory Structure

```
manifests/
└── s/
    └── ScottHanselman/
        └── WindowsEdgeLight/
            └── 0.6.0/
                ├── ScottHanselman.WindowsEdgeLight.yaml
                ├── ScottHanselman.WindowsEdgeLight.installer.yaml
                └── ScottHanselman.WindowsEdgeLight.locale.en-US.yaml
```

### Manifest Files Explained

1. **ScottHanselman.WindowsEdgeLight.yaml** - Version manifest
   - Contains the package identifier, version, and manifest metadata

2. **ScottHanselman.WindowsEdgeLight.installer.yaml** - Installer manifest
   - Defines installer URLs, SHA256 hashes, and installation behavior
   - Specifies architectures (x64 and ARM64)
   - Configured as a portable app (zip with nested exe)

3. **ScottHanselman.WindowsEdgeLight.locale.en-US.yaml** - Default locale manifest
   - Contains package metadata: name, description, publisher, license
   - Includes tags and release notes

## How to Submit to Winget

### Prerequisites

1. A GitHub account
2. Git installed on your machine
3. PowerShell or Windows Terminal

### Method 1: Using WingetCreate (Recommended)

WingetCreate is the official tool for creating and submitting Winget manifests.

1. **Install WingetCreate:**
   ```bash
   winget install Microsoft.WingetCreate
   ```

2. **Create or Update Manifest:**
   ```bash
   wingetcreate update ScottHanselman.WindowsEdgeLight --urls https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.6.0/WindowsEdgeLight-v0.6.0-win-x64.zip https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.6.0/WindowsEdgeLight-v0.6.0-win-arm64.zip --version 0.6.0 --submit
   ```

   This command will:
   - Download the installers
   - Calculate SHA256 hashes automatically
   - Create updated manifest files
   - Fork the winget-pkgs repository (if needed)
   - Create a pull request automatically

3. **Follow the PR Process:**
   - The tool will open a browser to the pull request
   - Wait for automated validation to complete
   - Address any validation errors
   - Wait for Microsoft review and approval

### Method 2: Manual Submission

1. **Fork the winget-pkgs repository:**
   - Go to https://github.com/microsoft/winget-pkgs
   - Click "Fork" to create your own copy

2. **Clone your fork:**
   ```bash
   git clone https://github.com/YOUR_USERNAME/winget-pkgs.git
   cd winget-pkgs
   ```

3. **Create a new branch:**
   ```bash
   git checkout -b ScottHanselman.WindowsEdgeLight-0.6.0
   ```

4. **Copy manifest files:**
   - Copy the files from this repo's `manifests/` directory to the winget-pkgs repository
   - Place them in: `manifests/s/ScottHanselman/WindowsEdgeLight/0.6.0/`

5. **Update SHA256 hashes:**
   - Download the release files
   - Calculate SHA256 hashes:
     ```bash
     Get-FileHash -Algorithm SHA256 WindowsEdgeLight-v0.6.0-win-x64.zip
     Get-FileHash -Algorithm SHA256 WindowsEdgeLight-v0.6.0-win-arm64.zip
     ```
   - Replace `YOUR_SHA256_HASH_HERE` in the installer manifest

6. **Validate the manifests:**
   ```bash
   winget validate --manifest manifests/s/ScottHanselman/WindowsEdgeLight/0.6.0
   ```

7. **Commit and push:**
   ```bash
   git add manifests/s/ScottHanselman/WindowsEdgeLight/0.6.0/
   git commit -m "New version: ScottHanselman.WindowsEdgeLight version 0.6.0"
   git push origin ScottHanselman.WindowsEdgeLight-0.6.0
   ```

8. **Create a Pull Request:**
   - Go to your fork on GitHub
   - Click "Pull requests" → "New pull request"
   - Ensure the base repository is `microsoft/winget-pkgs` and base branch is `master`
   - Click "Create pull request"
   - Fill in the PR template

### Method 3: Using YamlCreate.ps1 Script

The winget-pkgs repository includes a PowerShell script for manifest creation.

1. **Clone winget-pkgs:**
   ```bash
   git clone https://github.com/microsoft/winget-pkgs.git
   cd winget-pkgs/Tools
   ```

2. **Run YamlCreate.ps1:**
   ```powershell
   .\YamlCreate.ps1
   ```

3. **Follow the interactive prompts:**
   - Package Identifier: `ScottHanselman.WindowsEdgeLight`
   - Version: `0.6.0`
   - Installer URLs: Provide the GitHub release URLs
   - The script will guide you through all required fields

4. **Review and submit:**
   - The script will create the manifests and optionally create a PR

## Updating for New Releases

When releasing a new version:

1. **Update the version number** in all manifest files
2. **Update installer URLs** to point to the new release
3. **Calculate new SHA256 hashes** for the new installers
4. **Update release notes** in the locale manifest
5. **Update the release date**
6. **Submit using one of the methods above**

### Automated Manifest Updates

For fully automated manifest updates on each release, you can add a GitHub Actions workflow (see `WINGET_AUTOMATION.md` for details).

## Validation and Requirements

### Automatic Validation

When you submit a PR to winget-pkgs, automated checks will:
- Validate manifest schema
- Check file hashes match downloads
- Verify URLs are accessible
- Scan for malware (SmartScreen)
- Test installation on Windows

### Common Requirements

- **Stable releases only**: Pre-releases and beta versions are generally not accepted
- **Direct download URLs**: Must be direct HTTPS download links (no redirects)
- **Consistent versioning**: Version in manifest must match actual file version
- **Secure sources**: Installers must be from reputable sources
- **No breaking changes**: Updates shouldn't break existing installations

### Review Process

1. **Automated validation** (~5-10 minutes)
2. **Microsoft review** (1-3 days typically)
3. **Approval and merge**
4. **Propagation** to Winget (within 24 hours)

## Testing Locally

Before submitting, test the manifest locally:

1. **Install from local manifest:**
   ```bash
   winget install --manifest manifests/s/ScottHanselman/WindowsEdgeLight/0.6.0
   ```

2. **Verify installation:**
   ```bash
   winget list ScottHanselman.WindowsEdgeLight
   ```

3. **Test uninstallation:**
   ```bash
   winget uninstall ScottHanselman.WindowsEdgeLight
   ```

## Resources

- [Winget-pkgs Repository](https://github.com/microsoft/winget-pkgs)
- [Manifest Authoring Guide](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest)
- [WingetCreate Documentation](https://github.com/microsoft/winget-create)
- [Manifest Schema Reference](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema)
- [Submission Guidelines](https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md)

## Troubleshooting

### "Hash mismatch" error
- Recalculate the SHA256 hash
- Ensure you're downloading the correct file
- Check for URL redirects

### "Manifest validation failed"
- Run `winget validate` locally
- Check YAML syntax (spaces, not tabs)
- Verify all required fields are present

### "Installation failed"
- Test the installer manually
- Check installer URL is accessible
- Verify architecture matches (x64 vs ARM64)

## Support

For issues with Winget submission:
- Check [winget-pkgs issues](https://github.com/microsoft/winget-pkgs/issues)
- Review [submission guidelines](https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md)
- Ask in [Winget discussions](https://github.com/microsoft/winget-pkgs/discussions)
