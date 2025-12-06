# Camera Detection Feature

## Overview

The Camera Detection feature automatically turns on the Windows Edge Light when your camera is being used by applications like Teams, Zoom, or any other video conferencing software. This ensures you always have proper lighting during video calls without having to manually toggle the light.

## How It Works

The feature monitors Windows registry keys that the operating system uses to track camera access by applications. Specifically, it watches the `CapabilityAccessManager\ConsentStore\webcam` registry path, which Windows updates whenever an application accesses the camera.

### Key Features

- **Automatic Light Control**: When the camera is detected as in use, the edge light automatically turns on
- **Smart State Management**: The light remembers its previous state and restores it when the camera is no longer in use
- **Windows Hello Filtering**: Filters out Windows Hello authentication events to prevent false triggers
- **Low Resource Usage**: Uses efficient polling (every 2 seconds) instead of continuous monitoring
- **Non-Intrusive**: If camera registry access is unavailable, the feature gracefully disables itself

## How to Use

1. **Enable the Feature**: Right-click the Windows Edge Light system tray icon and select "📷 Enable Camera Detection"
2. **Start a Video Call**: Open any application that uses your camera (Teams, Zoom, etc.)
3. **Automatic Activation**: The edge light will automatically turn on when the camera is in use
4. **Automatic Deactivation**: When you end your video call, the light returns to its previous state

## Technical Implementation

### Registry Monitoring

The feature monitors the following registry path:
```
HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
```

Each application that accesses the camera has a subkey with a `LastUsedTimeStop` value:
- **Value = 0**: Camera is currently in use by this application
- **Value > 0**: Timestamp when the application stopped using the camera

### Windows Hello Filtering

To avoid false triggers from Windows Hello facial recognition, the monitor filters out applications with these patterns in their names:
- `hello`
- `windows.internal`
- `microsoft.windows.cloudexperiencehost`

### Polling Strategy

Instead of using event-based monitoring (which isn't available for registry changes at this level), the feature uses a polling approach with a 2-second interval. This provides:
- Quick response time (max 2-second delay to detect camera usage)
- Low CPU usage (only checks registry every 2 seconds)
- Reliable detection across all applications

## Research Background

This implementation is based on research documented in Issue #21. Key findings:

1. **UWP MediaCapture API**: While Windows provides `CaptureDeviceExclusiveControlStatusChanged` event for UWP apps, this is app-specific and not system-wide
2. **No Native .NET API**: There's no official .NET API for system-wide camera usage detection
3. **Registry Monitoring**: The most practical approach for WPF/.NET desktop applications is monitoring the `CapabilityAccessManager` registry keys
4. **Community Solutions**: Similar approach used by open-source tools like [webcam-usage-detector](https://github.com/Lineblacka/webcam-usage-detector)

## Limitations

- **Registry Access Required**: Needs read access to HKEY_CURRENT_USER registry keys
- **2-Second Detection Delay**: Maximum 2-second delay between camera activation and light turning on
- **Windows 10+ Only**: Requires Windows 10 or later (CapabilityAccessManager introduced in Windows 10)
- **No App Identification**: Cannot reliably determine which specific application is using the camera (only package names)

## Privacy & Security

- **Read-Only Access**: Only reads registry values, never writes or modifies system settings
- **No Camera Access**: Does not access or use your camera directly
- **Local Only**: All monitoring happens locally on your PC, no data is sent anywhere
- **Opt-In**: Feature is disabled by default and must be manually enabled by the user

## Troubleshooting

### Feature Doesn't Work

1. **Check Registry Permissions**: Ensure you have read access to the registry path mentioned above
2. **Windows Version**: Verify you're running Windows 10 or later
3. **Camera Privacy Settings**: Check Windows Settings > Privacy > Camera to ensure camera access is enabled
4. **Try Restarting**: Disable and re-enable the feature from the tray menu

### False Positives

If the light turns on unexpectedly:
1. Check if Windows Hello is triggering it (should be filtered but may have edge cases)
2. Check if any background applications are accessing the camera
3. Review Windows Settings > Privacy > Camera to see which apps recently used the camera

### Light Doesn't Turn Off

If the light stays on after closing camera apps:
1. Wait 2-4 seconds (polling interval)
2. Check if any application still has the camera open
3. Disable and re-enable camera detection to reset the state

## Future Enhancements

Potential improvements for future versions:
- Configurable polling interval
- Notification when camera detection activates the light
- List of detected applications using the camera
- Per-application filtering (whitelist/blacklist)
- Integration with brightness adjustment based on time of day
