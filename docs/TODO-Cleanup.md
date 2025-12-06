# Code Cleanup TODO

Items identified during code review (2025-12-06). None are critical - address as time permits.

## Duplicated Code

### 1. Color Lerp & Temperature Constants
**Location:** `MainWindow.xaml.cs` lines 731-740 and 773-787

The `Lerp` function and cool/warm color constants are duplicated in two places:
- `UpdateAdditionalMonitorWindows()`
- `SetColorTemperature()`

**Fix:** Extract to shared helper:
```csharp
private static readonly Color CoolColor = Color.FromRgb(220, 235, 255);
private static readonly Color WarmColor = Color.FromRgb(255, 220, 180);

private static Color LerpColor(Color a, Color b, double t)
{
    byte LerpByte(byte x, byte y, double tt) => (byte)(x + (y - x) * tt);
    return Color.FromArgb(255, LerpByte(a.R, b.R, t), LerpByte(a.G, b.G, t), LerpByte(a.B, b.B, t));
}
```

---

## Unused Code

### 2. FeatureFlags Class Not Used
**Location:** `AI/FeatureFlags.cs`

This class has settings persistence (`ai-settings.json`) but the AI tracking on/off state isn't actually persisted between app restarts.

**Options:**
- [ ] Wire it up to persist AI tracking preference across restarts
- [ ] Remove if persistence isn't needed

---

## Minor Issues

### 3. Async/Await Mismatch
**Location:** `ControlWindow.xaml.cs` line 94

```csharp
private async void AITracking_Click(object sender, RoutedEventArgs e)
```

The method is marked `async` but `ToggleAIFaceTracking()` isn't awaited.

**Fix:** Either make `ToggleAIFaceTracking` async and await it, or remove `async` keyword.

### 4. Commented-Out Code
**Location:** `ControlWindow.xaml.cs`

```csharp
// using WindowsEdgeLight.AI;  // Temporarily disabled for debugging
// UpdateAIButtonVisibility();  // Temporarily disabled
```

**Fix:** Remove or uncomment once debugging is complete.

---

## Refactoring Opportunities

### 5. MainWindow is Large (~1400 lines)
Not critical, but could be cleaner if extracted:

- [ ] Monitor management → `MonitorManager.cs`
- [ ] Geometry/frame creation → `EdgeLightGeometry.cs`
- [ ] DPI handling → `DpiHelper.cs`

Low priority - only do if MainWindow becomes harder to navigate.

---

## Not Issues (Reviewed and OK)

- ✅ AI tracking code cleanly separated in `AI/` folder
- ✅ Proper `IDisposable` on `NativeFaceTracker`
- ✅ Thread-safe `FileLogger`
- ✅ Multi-monitor DPI handling
- ✅ WFO0003 warning suppression documented in csproj
