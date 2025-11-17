# Windows Edge Light

A lightweight WPF application that adds a customizable glowing edge light effect around your primary monitor on Windows. Perfect for ambient lighting during video calls, streaming, or just adding a professional touch to your workspace.

## Features

- **Automatic Updates**: Built-in update system checks GitHub Releases for new versions
- **Primary Monitor Display**: Automatically detects and displays on your primary monitor, even in multi-monitor setups
- **DPI Aware**: Properly handles high-DPI displays (4K monitors with scaling)
- **Fluent Design**: Modern UX that fits in with the Windows look and feel
- **Click-Through Transparency**: Overlay doesn't interfere with your work - all clicks pass through to applications beneath
- **Customizable Brightness**: Adjust opacity with easy-to-use controls
- **Toggle On/Off**: Quickly enable or disable the edge light effect
- **Always On Top**: Stays visible above all other windows
- **Keyboard Shortcuts**: 
  - `Ctrl+Shift+L` - Toggle light on/off
  - `Ctrl+Shift+Up` - Increase brightness
  - `Ctrl+Shift+Down` - Decrease brightness
- **Gradient Effect**: Beautiful white gradient with subtle blur for a professional look

## Screenshots

The application creates a smooth, glowing border around the edges of your primary monitor:

- Adjustable brightness levels (20% to 100% opacity)
- Soft blur effect for a natural glow
- Minimal UI controls that fade in on hover

![Windows Edge Light](HeroImage.png)

## Installation

### Option 1: Download Pre-built Executable

Download the latest `WindowsEdgeLight.exe` from the [Releases page](https://github.com/shanselman/WindowsEdgeLight/releases). This is a single-file executable that includes everything you need - no .NET installation required!

### Option 2: Build from Source

#### Prerequisites

- Windows 10 or later
- .NET 10.0 SDK for building

#### Building from Source

1. Clone this repository:
   ```bash
   git clone https://github.com/shanselman/WindowsEdgeLight.git
   cd WindowsEdgeLight
   ```

2. Build the project:
   ```bash
   cd WindowsEdgeLight
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run
   ```

#### Creating a Single-File Executable

To create a standalone executable:
```bash
cd WindowsEdgeLight
dotnet publish -c Release /p:DebugType=None /p:DebugSymbols=false
```

The executable will be in `bin\Release\net10.0-windows\win-x64\publish\WindowsEdgeLight.exe`

**Note**: WPF applications cannot use AOT compilation or aggressive trimming. The single-file executable is self-contained and compressed (~72MB) but includes the full .NET runtime.

## Usage

1. Launch `WindowsEdgeLight.exe`
2. The edge light will appear around your primary monitor
3. Hover over the top-right corner to reveal controls:
   - 🔅 **Decrease Brightness** - Reduces opacity
   - 🔆 **Increase Brightness** - Increases opacity
   - 💡 **Toggle Light** - Turn the effect on/off
   - ✖ **Exit** - Close the application

### Keyboard Shortcuts

- **Ctrl+Shift+L**: Toggle the edge light on/off
- **Ctrl+Shift+Up**: Increase brightness
- **Ctrl+Shift+Down**: Decrease brightness
- **Taskbar**: Right-click the taskbar icon to close the application

## Technical Details

### Architecture

- **Framework**: .NET 10.0 WPF (Windows Presentation Foundation)
- **Language**: C#
- **UI**: XAML with transparent window overlay
- **Monitor Detection**: Windows Forms Screen API for accurate multi-monitor support

### Key Features Implementation

- **Click-Through**: Uses Win32 `WS_EX_TRANSPARENT` and `WS_EX_LAYERED` window styles
- **DPI Scaling**: Converts physical pixels to WPF Device Independent Pixels for proper sizing
- **Primary Monitor**: Uses `Screen.PrimaryScreen.Bounds` with DPI correction
- **Gradient Border**: Custom Rectangle with LinearGradientBrush and BlurEffect

## Multi-Monitor Support

The application specifically targets the **primary monitor** in your display setup:
- Automatically detects primary monitor position and dimensions
- Correctly handles DPI scaling (e.g., 150%, 200% on 4K displays)
- Works with any monitor arrangement (horizontal, vertical, mixed)
- Does not span across multiple monitors

## Development

### Project Structure

```
WindowsEdgeLight/
├── WindowsEdgeLight/
│   ├── App.xaml              # Application entry point
│   ├── App.xaml.cs
│   ├── MainWindow.xaml       # Main UI layout
│   ├── MainWindow.xaml.cs    # Application logic
│   ├── WindowsEdgeLight.csproj
│   └── AssemblyInfo.cs
└── README.md
```

### Building

Requires:
- Visual Studio 2022 or later (with .NET desktop development workload)
- Or .NET 10.0 SDK for command-line builds

## Version History

**Note**: Versions are automatically managed using GitVersion based on Git tags.

### v0.6 - Automatic Update System
- Integrated Updatum for automatic updates from GitHub Releases
- Beautiful update dialog with release notes
- Download progress tracking
- One-click install for new versions
- See [UPDATUM_INTEGRATION.md](UPDATUM_INTEGRATION.md) for details

### v0.3 - Global Hotkeys and Taskbar Support
- Added global hotkeys for brightness control (Ctrl+Shift+Up/Down)
- Fixed taskbar overlap - window now respects taskbar area
- Added taskbar icon for easy right-click close
- Removed conflicting exit hotkey
- Added custom ring light icon
- Added assembly information with author details

### v0.2 - Primary Monitor Display Fix
- Fixed window to display on primary monitor only
- Added proper DPI scaling support for high-resolution displays
- Resolved namespace conflicts with Windows Forms integration
- Improved multi-monitor setup compatibility

### v0.1 - Initial Release
- Basic edge light overlay functionality
- Customizable brightness controls
- Toggle and keyboard shortcut support
- Click-through transparency

## License

This project is provided as-is for personal and educational use.

## Contributing

Contributions are welcome! Feel free to:
- Report bugs
- Suggest new features
- Submit pull requests

## Acknowledgments

Inspired by the need for professional lighting effects during video conferences and streaming setups.

---

**Note**: This application is designed for Windows only and requires the .NET 10.0 runtime.
