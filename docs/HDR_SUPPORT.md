# HDR (High Dynamic Range) Support

Windows Edge Light now fully supports HDR displays while ensuring it doesn't interfere with other applications' color management.

## What is HDR Support?

HDR (High Dynamic Range) and Windows Advanced Color provide:
- Greater brightness and contrast range
- More accurate and vibrant colors
- Wide Color Gamut (WCG) support
- Higher bit-depth color (10-bit or more vs standard 8-bit)

## How Windows Edge Light Supports HDR

### 1. Display Detection
The application automatically detects:
- Whether your display supports HDR/Advanced Color
- Whether HDR is currently enabled in Windows settings
- The color bit depth of your display (8-bit SDR vs 10-bit+ HDR)

### 2. Respects Windows Auto Color Management (ACM)

Windows Edge Light is designed to work seamlessly with Windows 11's Auto Color Management:

- **Does NOT override system color management**: The app uses standard rendering that Windows automatically manages
- **No custom ICC profiles**: Avoids conflicts with system-level color management
- **Transparent to other apps**: Your graphics apps, photo editors, and color-critical applications remain unaffected

### 3. HDR-Aware Rendering Mode

When HDR is detected and enabled:

- **Optimized color temperature**: Slight adjustment for more comfortable viewing on HDR displays
- **Respects display capabilities**: Gracefully handles both SDR and HDR displays
- **User control**: Can be toggled via the system tray menu

## Features

### Automatic HDR Detection
- Runs at startup to detect your display's HDR capabilities
- Shows current status in the system tray menu:
  - 🎨 HDR Active (10-bit) - When HDR is enabled
  - 🎨 HDR Supported (Not Enabled) - When hardware supports HDR but it's not enabled
  - 🎨 SDR Display - For standard displays

### HDR-Aware Rendering Toggle
Right-click the system tray icon and look for:
- ✓ HDR-Aware Rendering - Currently enabled
- HDR-Aware Rendering - Currently disabled (click to enable)

### Auto Color Management Indicator
If you're running Windows 11 22H2 or later with Auto Color Management:
- ℹ️ Auto Color Management Active - Displayed in the menu

## Technical Implementation

### 1. Application Manifest
The `app.manifest` file is configured to:
- Support Windows 10 and later
- Work with Per-Monitor DPI awareness (PerMonitorV2)
- Be compatible with Windows Advanced Color features

### 2. Display Configuration API
Uses Windows Display Configuration APIs to query:
```
DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
```
This provides:
- Advanced color support status
- Advanced color enabled status
- Bits per color channel

### 3. Color Space Handling
- Renders in standard sRGB color space
- Windows composition layer (DWM) automatically converts to HDR when needed
- No explicit scRGB or HDR10 color space manipulation required
- Lets Windows handle the color space conversions automatically

### 4. No Impact on Other Applications
Because the app:
- Uses system-managed color pipelines
- Doesn't install custom color profiles
- Doesn't override display color settings
- Works within Windows' compositing framework

Other applications' color management remains completely unaffected.

## Usage

### Enabling HDR on Your Display
1. Open Windows Settings > System > Display
2. Select your HDR-capable display
3. Turn on "Use HDR"
4. Optionally enable "Auto HDR" and "Automatically manage color for apps"

### Using HDR-Aware Rendering in Windows Edge Light
1. Launch Windows Edge Light
2. Right-click the system tray icon
3. Check the HDR status at the top of the menu
4. Toggle "HDR-Aware Rendering" if desired

### Best Practices
- Leave HDR-Aware Rendering enabled (default) for optimal experience on HDR displays
- If you notice color shifts, verify Windows Auto Color Management is enabled
- For color-critical work in other apps, those apps should handle their own HDR/color management

## Compatibility

### Windows Versions
- **Windows 10 1709+**: Basic HDR support detection
- **Windows 11 22H2+**: Full Auto Color Management support

### Display Requirements
- Display must support HDR10 or better
- DisplayPort 1.4+ or HDMI 2.0+ connection
- Graphics card with HDR support

### No Requirements Changed
- Works on non-HDR displays (SDR mode)
- No special drivers needed
- No additional software required

## Troubleshooting

### HDR Status Shows "Supported (Not Enabled)"
**Solution**: Enable HDR in Windows Settings > Display

### Colors Look Wrong
**Solution**: 
1. Ensure "Automatically manage color for apps" is enabled in Windows Settings
2. Try toggling HDR-Aware Rendering in the system tray menu
3. Some legacy apps may need "Use legacy display ICC color management" in their compatibility settings (not Windows Edge Light)

### Other Apps Showing Color Issues
**Diagnosis**: This is likely NOT caused by Windows Edge Light. The app doesn't:
- Change display color profiles
- Install custom color management
- Override system settings

**Solution**: Check those apps' own HDR/color management settings

## Technical Notes for Developers

### Why Not DirectX with scRGB?
- WPF doesn't natively support DirectX HDR rendering
- Windows composition automatically handles SDR-to-HDR conversion
- Simpler implementation with better compatibility
- No need for explicit swap chain management

### Color Space Approach
- WPF renders in sRGB (8-bit per channel)
- DWM composites and converts to display's native color space
- HDR displays receive FP16 linear scRGB from DWM
- Auto Color Management ensures color accuracy

### Why This Approach Works
1. **System-level**: Windows manages all color conversions
2. **Application isolation**: Each app's rendering is independent
3. **Automatic**: No manual color space conversions needed
4. **Compatible**: Works on all Windows versions and display types

## References

- [Microsoft: Use DirectX with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)
- [Microsoft: ICC Profile Behavior with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles)
- [Windows 11 Auto Color Management](https://devblogs.microsoft.com/directx/auto-color-management/)

## Future Enhancements

Potential future improvements:
- Per-monitor HDR detection in multi-monitor setups
- HDR metadata support for enhanced brightness
- DirectX interop for native HDR rendering (if WPF adds support)
- HDR brightness/nits calibration settings

---

**Note**: This implementation prioritizes compatibility, simplicity, and ensuring Windows Edge Light works harmoniously with Windows' color management system without interfering with other applications.
