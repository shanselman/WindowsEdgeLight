# Updatum Integration Guide

This document explains how the automatic update system has been integrated into Windows Edge Light using [Updatum](https://github.com/sn4k3/Updatum).

## Critical Update Fix (v1.6-v1.7)

**IMPORTANT:** There was a bug in Updatum when handling ZIP files with only a single file. The fix was implemented in v1.6-v1.7:

### The Problem
- When a ZIP file contained only `WindowsEdgeLight.exe` (single file), Updatum would extract it but then try to run the original ZIP file path as an installer
- This caused Windows Explorer to fail with "can't find the zip" error
- The extracted .exe was never properly used for the update

### The Solution
Two changes were made to fix this:

1. **Modified ZIP structure** (in `.github/workflows/build.yml`):
   - ZIPs now contain **both** `WindowsEdgeLight.exe` AND `README.md`
   - This forces Updatum to treat it as a "portable app" (multiple files)
   - Updatum then properly copies files and restarts the application

2. **Removed installer configuration** (in `App.xaml.cs`):
   - Removed `InstallUpdateWindowsInstallerArguments = "/qb"`
   - This property should only be set for actual MSI/EXE installers, not for single-file apps
   - Leaving it set was confusing Updatum's detection logic

### Current Configuration (v1.7+)

```csharp
// In App.xaml.cs
internal static readonly UpdatumManager AppUpdater = new("shanselman", "WindowsEdgeLight")
{
    // Default pattern (win-x64) will match our ZIP assets
    // ZIP files are portable apps with exe and README for proper update handling
    FetchOnlyLatestRelease = true, // Saves GitHub API rate limits
    // Specify the executable name for single-file app (without .exe extension)
    InstallUpdateSingleFileExecutableName = "WindowsEdgeLight",
};
```

```powershell
# In .github/workflows/build.yml - ZIP creation
New-Item -ItemType Directory -Path "temp-x64" -Force
Copy-Item "WindowsEdgeLight/bin/Release/net10.0-windows/win-x64/publish/WindowsEdgeLight.exe" -Destination "temp-x64/"
Copy-Item "README.md" -Destination "temp-x64/"  # ← This is the fix!
Compress-Archive -Path "temp-x64/*" -DestinationPath "artifacts/WindowsEdgeLight-v$version-win-x64.zip"
```

### Why This Works
- Updatum has different code paths for single-file ZIPs vs multi-file ZIPs
- Single-file extraction has a bug where it updates a local variable but not the asset path
- Multi-file ZIPs are treated as portable apps with proper batch script generation
- The batch script correctly copies files and relaunches the application

### Testing Notes
- Users on v1.5 upgrading to v1.6 may still hit the bug (v1.5 ZIPs were single-file)
- Users on v1.6+ upgrading to v1.7+ will have the fixed experience
- Always include at least 2 files in ZIP releases going forward

## What is Updatum?

Updatum is a lightweight C# library that automates application updates using GitHub Releases. It handles:
- Checking for new versions
- Downloading updates with progress tracking
- Installing updates automatically
- Displaying release notes

## How It Works

### 1. Update Checking

When the application starts, it automatically checks for updates after a 2-second delay:

```csharp
// In App.xaml.cs
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    _ = CheckForUpdatesAsync();
}
```

### 2. Update Dialog

If a new version is available, a beautiful dialog appears showing:
- The new version number
- Release notes formatted from GitHub
- Three action buttons:
  - **Download & Install** - Downloads and installs the update
  - **Remind Me Later** - Skips this time, will check again next launch
  - **Skip This Version** - Ignores this version

### 3. Download Progress

When downloading an update, a progress dialog shows:
- Download progress bar
- Download size (MB downloaded / Total MB)
- Percentage complete

### Installation

After downloading, the user is asked to confirm installation. If confirmed:
- **For portable apps (ZIP with multiple files)**: Extracts files, creates a batch script to copy over old files, kills current process, copies files, and relaunches
- **For single-file executables**: Replaces the current executable and relaunches
- **For MSI installers**: Launches the installer with specified arguments

**Current configuration uses portable app approach** - ZIPs contain .exe + README.md

## Configuration

### Setting Your GitHub Repository

**IMPORTANT:** The repository is currently set to `shanselman/WindowsEdgeLight`. If you fork this, update `App.xaml.cs`:

```csharp
internal static readonly UpdatumManager AppUpdater = new("YOUR_GITHUB_USERNAME", "WindowsEdgeLight")
```

Replace `YOUR_GITHUB_USERNAME` with your actual GitHub username.

### Asset Naming Convention

For Updatum to find your releases, name your GitHub Release assets following this pattern:

**Current naming (used by the GitHub Actions workflow):**

**For ZIP Files (Portable - RECOMMENDED):**
```
WindowsEdgeLight-v1.7-win-x64.zip
WindowsEdgeLight-v1.7-win-arm64.zip
```

**Important:** ZIP files MUST contain at least 2 files (see Critical Update Fix above). Current ZIPs include:
- `WindowsEdgeLight.exe`
- `README.md`

The asset pattern automatically matches the current platform (win-x64 or win-arm64).

### Publishing Configuration

When you publish your application, make sure the version number matches:

```xml
<!-- In WindowsEdgeLight.csproj -->
<AssemblyVersion>1.7.0.0</AssemblyVersion>
<FileVersion>1.7.0.0</FileVersion>
<Version>1.7</Version>
```

## Creating a GitHub Release

The GitHub Actions workflow automatically creates releases when you push a tag:

1. **Update version in WindowsEdgeLight.csproj** (e.g., to 1.8)
2. **Commit and push:**
   ```powershell
   git add -A
   git commit -m "Bump to v1.8 - Description of changes"
   git tag v1.8
   git push && git push --tags
   ```
3. **GitHub Actions will automatically:**
   - Build win-x64 and win-arm64 versions
   - Create ZIPs with both .exe and README.md
   - Create a GitHub Release with the ZIPs attached
   - Generate release notes

**Manual Release (if needed):**
1. Go to your repository on GitHub
2. Click "Releases" → "Draft a new release"
3. Tag: `v1.8` (must start with 'v')
4. Title: `Version 1.8`
5. Upload your ZIP files
6. Publish the release

## Release Notes Format

Updatum will automatically parse and display your GitHub release notes. Use Markdown for formatting:

```markdown
## v0.7.0

### New Features
- ✨ Automatic update system
- 📝 Release notes display

### Improvements
- 🚀 Better performance
- 🎨 Updated UI

### Bug Fixes
- 🐛 Fixed brightness control
- 🔧 Fixed memory leak
```

## Customization Options

### Update Check Timing

To check for updates at different times:

```csharp
// Check on startup with a delay
await Task.Delay(5000); // 5 second delay
var updateFound = await AppUpdater.CheckForUpdatesAsync();

// Periodic checks
AppUpdater.AutoUpdateCheckTimer.Interval = TimeSpan.FromHours(1).TotalMilliseconds;
AppUpdater.AutoUpdateCheckTimer.Start();
```

### MSI Installer Arguments

**NOTE:** As of v1.6+, we do NOT use `InstallUpdateWindowsInstallerArguments` because we ship portable ZIPs, not installers.

If you switch to MSI installers in the future:

```csharp
// Shows basic UI (/qb)
InstallUpdateWindowsInstallerArguments = "/qb",

// Silent with no UI:
InstallUpdateWindowsInstallerArguments = "/quiet",

// Full UI (default installer behavior):
InstallUpdateWindowsInstallerArguments = "",
```

### Asset Filtering

If you have multiple assets (e.g., portable and installer):

```csharp
// Prefer MSI files
AppUpdater.AssetExtensionFilter = "msi";

// Or prefer ZIP files
AppUpdater.AssetExtensionFilter = "zip";
```

## Testing the Update System

### Test with a Different Repository

To test without publishing your own releases:

```csharp
// Use the Updatum test repository
internal static readonly UpdatumManager AppUpdater = new("sn4k3", "UVtools")
{
    AssetRegexPattern = $"UVtools.*win-x64",
    InstallUpdateWindowsInstallerArguments = "/qb",
};
```

This will show you real update dialogs with the UVtools releases.

### Manual Testing

1. Set your version to something lower (e.g., 0.1.0)
2. Create a GitHub release with version 0.2.0
3. Run your application
4. You should see the update dialog

## Files Added

The following files were added to implement the update system:

- **UpdateDialog.xaml** - Update notification dialog UI
- **UpdateDialog.xaml.cs** - Update dialog logic
- **DownloadProgressDialog.xaml** - Download progress UI
- **DownloadProgressDialog.xaml.cs** - Download progress logic
- **App.xaml.cs** (modified) - Update checker integration

## NuGet Package

The following NuGet package was added:

```xml
<PackageReference Include="Updatum" Version="1.1.6" />
```

## Troubleshooting

### Update Not Found

1. **Check version numbers**: Your current version must be lower than the release version
2. **Check asset naming**: Ensure your asset name matches the regex pattern
3. **Check internet connection**: Updatum needs to access GitHub API
4. **Check GitHub repository**: Ensure the repository is public or you have access

### Download Fails

1. **Check asset size**: GitHub has size limits for releases
2. **Check permissions**: Ensure the app can write to the temp folder
3. **Check antivirus**: Some antivirus software may block downloads

### Installation Fails

1. **For MSI**: User needs admin privileges
2. **For single-file**: App must be able to replace its own executable
3. **Check file locks**: Close all instances before installing

## Advanced Features

### Skip Version Tracking

Currently not implemented, but you can add:

```csharp
// Save skipped version
Properties.Settings.Default.SkippedVersion = version;
Properties.Settings.Default.Save();

// Check if version was skipped
if (Properties.Settings.Default.SkippedVersion == release.TagName)
    return;
```

### Custom UI

You can create your own dialogs by replacing UpdateDialog and DownloadProgressDialog with your custom implementations.

### Rate Limiting

GitHub API has rate limits. Updatum handles this gracefully, but for frequent checks, consider:

```csharp
// Check only once per day
var lastCheck = Properties.Settings.Default.LastUpdateCheck;
if ((DateTime.Now - lastCheck).TotalHours < 24)
    return;
```

## Resources

- [Updatum GitHub Repository](https://github.com/sn4k3/Updatum)
- [Updatum Documentation](https://github.com/sn4k3/Updatum#readme)
- [GitHub Releases Documentation](https://docs.github.com/en/repositories/releasing-projects-on-github)
