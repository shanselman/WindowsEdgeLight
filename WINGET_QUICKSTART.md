# Winget Integration - Quick Start Guide

Windows Edge Light can now be distributed via Windows Package Manager (Winget)!

## For Users

Once submitted and approved, users can install with:
```bash
winget install ScottHanselman.WindowsEdgeLight
```

## For Maintainers

### First-Time Submission

1. **Ensure a release exists on GitHub** with the correct version (e.g., v0.6.0)

2. **Update manifest hashes** using the helper script:
   ```powershell
   .\update-winget-manifest.ps1 -Version "0.6.0"
   ```

3. **Submit to Winget** using WingetCreate:
   ```bash
   wingetcreate update ScottHanselman.WindowsEdgeLight \
     --urls https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.6.0/WindowsEdgeLight-v0.6.0-win-x64.zip \
            https://github.com/shanselman/WindowsEdgeLight/releases/download/v0.6.0/WindowsEdgeLight-v0.6.0-win-arm64.zip \
     --version 0.6.0 \
     --submit
   ```

4. **Monitor the PR** on microsoft/winget-pkgs for validation results

### Updating for New Releases

For each new release:

1. Create the GitHub release first
2. Run: `.\update-winget-manifest.ps1 -Version "X.Y.Z"`
3. Submit with WingetCreate or manually
4. Wait for PR approval

### Automation (Optional)

For automatic submissions on each release, see [docs/WINGET_AUTOMATION.md](docs/WINGET_AUTOMATION.md)

## File Structure

```
WindowsEdgeLight/
├── manifests/                          # Winget manifest files
│   ├── README.md                      # Manifest documentation
│   └── s/ScottHanselman/WindowsEdgeLight/
│       └── 0.6.0/                     # Version-specific manifests
│           ├── *.yaml                 # Version manifest
│           ├── *.installer.yaml       # Installer details
│           └── *.locale.en-US.yaml    # Metadata
├── docs/
│   ├── WINGET.md                      # Complete submission guide
│   └── WINGET_AUTOMATION.md           # Automation options
├── update-winget-manifest.ps1         # Helper script
└── README.md                          # Updated with Winget install instructions
```

## Documentation

- **[docs/WINGET.md](docs/WINGET.md)** - Complete guide to Winget submission
- **[docs/WINGET_AUTOMATION.md](docs/WINGET_AUTOMATION.md)** - GitHub Actions automation
- **[manifests/README.md](manifests/README.md)** - Manifest file documentation

## Important Notes

- ✅ Manifests are created for version 0.6.0 as an example
- ⚠️ SHA256 hashes in installer manifest are placeholders (`YOUR_SHA256_HASH_HERE`)
- 🔧 Run `update-winget-manifest.ps1` to auto-populate correct hashes
- 📝 First submission requires manual PR to microsoft/winget-pkgs
- 🤖 Future updates can be automated with GitHub Actions

## Next Steps

1. Review all documentation in `docs/WINGET*.md`
2. Test the helper script: `.\update-winget-manifest.ps1 -Version "0.6.0"`
3. Validate manifests: `winget validate --manifest manifests/s/ScottHanselman/WindowsEdgeLight/0.6.0`
4. Submit to Winget when ready
5. (Optional) Set up automation for future releases

---

**Questions?** See the comprehensive guides in the `docs/` directory or check the [Winget documentation](https://learn.microsoft.com/en-us/windows/package-manager/).
