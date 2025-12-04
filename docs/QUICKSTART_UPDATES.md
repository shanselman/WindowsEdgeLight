# Quick Start: Setting Up Auto-Updates

This is a quick reference for setting up automatic updates for Windows Edge Light.

## Step 1: Update Your GitHub Repository Info

Edit `WindowsEdgeLight\App.xaml.cs` and change this line:

```csharp
internal static readonly UpdatumManager AppUpdater = new("YOUR_GITHUB_USERNAME", "WindowsEdgeLight")
```

Replace `YOUR_GITHUB_USERNAME` with your actual GitHub username.

## Step 2: Publish Your Application

Run this command to build a release:

```powershell
cd WindowsEdgeLight
dotnet publish -c Release /p:DebugType=None /p:DebugSymbols=false
```

The output will be at:
```
bin\Release\net10.0-windows\win-x64\publish\WindowsEdgeLight.exe
```

## Step 3: Create a GitHub Release

1. Go to your GitHub repository
2. Click **Releases** → **Draft a new release**
3. Fill in:
   - **Tag**: `v0.7.0` (must match your version)
   - **Title**: `Version 0.7.0`
   - **Description**: Your release notes in Markdown

4. The GitHub Actions workflow will automatically build and upload:
   - `WindowsEdgeLight-v0.7.0-win-x64.exe` (single-file executable)
   - `WindowsEdgeLight-v0.7.0-win-x64.zip` (portable package)
   - `WindowsEdgeLight-v0.7.0-win-arm64.exe` (ARM64 executable)
   - `WindowsEdgeLight-v0.7.0-win-arm64.zip` (ARM64 portable)

5. Click **Publish release**

**Note:** You don't need to manually upload files - the GitHub Actions workflow handles everything when you push a tag!

## Step 4: Test It

1. Set your app version to something lower (e.g., `0.1.0` in the `.csproj`)
2. Rebuild
3. Run the app
4. You should see the update dialog!

## Asset Naming Examples

The GitHub Actions workflow automatically creates these files:

**Single-file executables:**
```
WindowsEdgeLight-v0.7.0-win-x64.exe
WindowsEdgeLight-v0.7.0-win-arm64.exe
```

**Portable ZIP packages:**
```
WindowsEdgeLight-v0.7.0-win-x64.zip
WindowsEdgeLight-v0.7.0-win-arm64.zip
```

The app is configured to prefer ZIP files for auto-updates (easier to extract and replace).

## Example Release Notes

```markdown
## What's New in v0.7.0

### ✨ New Features
- Automatic update system
- Beautiful update dialog with release notes
- One-click download and install

### 🎨 Improvements
- Better performance
- Refined UI

### 🐛 Bug Fixes
- Fixed brightness control
- Fixed memory leak
```

## Troubleshooting

**Update not showing?**
- Check that your current version is lower than the release version
- Verify the asset name matches the pattern `WindowsEdgeLight.*win-x64`
- Make sure the release is published (not draft)
- Check your internet connection
- The GitHub Actions workflow automatically creates the correct filenames

**Want to test without publishing?**

Temporarily change the repository in `App.xaml.cs` to use a test repo:
```csharp
internal static readonly UpdatumManager AppUpdater = new("sn4k3", "UVtools")
{
    AssetRegexPattern = $"UVtools.*win-x64",
```

This will let you see the update system working with real releases.

---

For complete documentation, see [UPDATUM_INTEGRATION.md](UPDATUM_INTEGRATION.md)
