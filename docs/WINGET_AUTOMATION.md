# Automated Winget Manifest Updates

This document describes how to automate Winget manifest creation and submission using GitHub Actions.

## Overview

This automation will:
1. Trigger on new release tags
2. Generate Winget manifest files with correct SHA256 hashes
3. Optionally create a pull request to the winget-pkgs repository

## Option 1: Manual Automation (Recommended for Initial Setup)

For the first few releases, it's recommended to submit manually using WingetCreate to ensure everything works correctly.

## Option 2: GitHub Actions Workflow

### Prerequisites

1. **GitHub Personal Access Token (PAT)**
   - Create a PAT with `public_repo` scope
   - Add it as a secret in your repository: `WINGET_SUBMIT_TOKEN`

2. **Fork winget-pkgs**
   - Fork https://github.com/microsoft/winget-pkgs to your account
   - This is where PRs will be created from

### Workflow File

Create `.github/workflows/winget-submit.yml`:

```yaml
name: Publish to Winget

on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to publish (e.g., 0.6.0)'
        required: true
        type: string

jobs:
  publish:
    runs-on: windows-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Get version
        id: version
        shell: pwsh
        run: |
          if ("${{ github.event_name }}" -eq "release") {
            $version = "${{ github.event.release.tag_name }}" -replace '^v', ''
          } else {
            $version = "${{ github.event.inputs.version }}"
          }
          echo "VERSION=$version" >> $env:GITHUB_OUTPUT
          echo "Publishing version: $version"

      - name: Install WingetCreate
        run: |
          Invoke-WebRequest -Uri https://aka.ms/wingetcreate/latest -OutFile wingetcreate.exe

      - name: Submit to Winget
        shell: pwsh
        env:
          WINGET_TOKEN: ${{ secrets.WINGET_SUBMIT_TOKEN }}
        run: |
          $version = "${{ steps.version.outputs.VERSION }}"
          
          # URLs for the release artifacts
          $urlX64 = "https://github.com/${{ github.repository }}/releases/download/v$version/WindowsEdgeLight-v$version-win-x64.zip"
          $urlArm64 = "https://github.com/${{ github.repository }}/releases/download/v$version/WindowsEdgeLight-v$version-win-arm64.zip"
          
          # Submit to Winget (this will create a PR in winget-pkgs)
          .\wingetcreate.exe update ScottHanselman.WindowsEdgeLight `
            --urls $urlX64 $urlArm64 `
            --version $version `
            --token $env:WINGET_TOKEN `
            --submit
```

### Using the Workflow

**Automatic (on release):**
- Simply create a new release on GitHub
- The workflow will automatically submit to Winget

**Manual:**
```bash
# Via GitHub UI:
# 1. Go to Actions → Publish to Winget → Run workflow
# 2. Enter the version number (e.g., 0.6.0)

# Via GitHub CLI:
gh workflow run winget-submit.yml -f version=0.6.0
```

## Option 3: Winget Releaser Action

An alternative is to use the community-maintained `vedantmgoyal2009/winget-releaser` action.

### Workflow File

Create `.github/workflows/winget-releaser.yml`:

```yaml
name: Publish to WinGet
on:
  release:
    types: [released]

jobs:
  winget:
    name: Publish to WinGet
    runs-on: windows-latest
    steps:
      - name: Publish to WinGet
        uses: vedantmgoyal2009/winget-releaser@v2
        with:
          identifier: ScottHanselman.WindowsEdgeLight
          installers-regex: '\.zip$'  # Only publish zip files
          token: ${{ secrets.WINGET_SUBMIT_TOKEN }}
```

### Benefits of Winget Releaser
- Simpler configuration
- Automatically detects release assets
- Handles SHA256 calculation
- Creates PR to winget-pkgs automatically

## Setup Instructions

### 1. Create GitHub Personal Access Token

1. Go to GitHub Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Click "Generate new token (classic)"
3. Name: "Winget Submission"
4. Scopes: Check `public_repo`
5. Click "Generate token"
6. Copy the token (you won't see it again!)

### 2. Add Token as Secret

1. Go to your repository → Settings → Secrets and variables → Actions
2. Click "New repository secret"
3. Name: `WINGET_SUBMIT_TOKEN`
4. Value: Paste your PAT
5. Click "Add secret"

### 3. Enable Workflow

1. Commit one of the workflow files above to `.github/workflows/`
2. Push to your repository
3. The workflow will activate on the next release

## Testing the Automation

### Test with Workflow Dispatch

Before relying on automatic triggers, test manually:

```bash
# Create a test release
gh release create v0.6.1-test --prerelease --notes "Test release"

# Manually trigger the workflow
gh workflow run winget-submit.yml -f version=0.6.1-test

# Check workflow status
gh run list --workflow=winget-submit.yml
```

### Verify the PR

After the workflow runs:
1. Check the winget-pkgs repository
2. Look for a new PR from your account
3. Verify the manifest files are correct
4. Check that automated validation passes

## Updating the Automation

### When Release Naming Changes

If you change the release asset naming:
- Update the URLs in the workflow
- Update the regex patterns
- Test with a pre-release first

### Adding New Architectures

If you add more architectures (e.g., x86):
- Add the new URL to the `--urls` parameter
- Update the installer manifest template
- Test thoroughly before submitting

## Troubleshooting

### "Authentication failed"
- Verify `WINGET_SUBMIT_TOKEN` secret is set
- Check the PAT hasn't expired
- Ensure PAT has `public_repo` scope

### "Release assets not found"
- Verify release assets are uploaded before workflow runs
- Check asset naming matches expectations
- Wait a few seconds after release creation

### "Validation failed"
- Check the PR on winget-pkgs for validation errors
- Review manifest files generated
- Verify URLs are accessible
- Check SHA256 hashes match

### "PR already exists"
- Close or merge the existing PR first
- Or increment the version number

## Best Practices

1. **Test manually first**: Submit your first few versions manually to understand the process
2. **Use pre-releases for testing**: Test automation with pre-releases before using on stable releases
3. **Monitor the PR**: Even with automation, watch the PR for validation failures
4. **Version consistency**: Ensure version numbers match across all files
5. **Release notes**: Include good release notes in your GitHub releases (they'll appear in Winget)

## Alternative: Semi-Automatic Approach

For more control, generate manifests automatically but submit manually:

```yaml
name: Generate Winget Manifests

on:
  release:
    types: [published]

jobs:
  generate:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Generate manifests
        shell: pwsh
        run: |
          # Script to generate manifest files
          # (without auto-submitting)
          
      - name: Upload manifests
        uses: actions/upload-artifact@v4
        with:
          name: winget-manifests
          path: manifests/
```

Then manually:
1. Download the generated manifests
2. Review them
3. Submit via WingetCreate or manually

## Resources

- [WingetCreate GitHub](https://github.com/microsoft/winget-create)
- [Winget Releaser Action](https://github.com/vedantmgoyal2009/winget-releaser)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Winget-pkgs Contributing Guide](https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md)
