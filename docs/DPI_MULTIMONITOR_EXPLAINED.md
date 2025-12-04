# Multi-Monitor and DPI Scaling Explained for Code Review

**Created**: December 2025  
**For**: Code review with management  
**Audience**: Junior engineers learning Win32 + WPF integration

---

## Table of Contents
1. [The Problem We're Solving](#the-problem-were-solving)
2. [Understanding DPI Basics](#understanding-dpi-basics)
3. [How Windows Handles Multiple Monitors](#how-windows-handles-multiple-monitors)
4. [The WPF Challenge](#the-wpf-challenge)
5. [Our Solution Deep Dive](#our-solution-deep-dive)
6. [The Recent Bug Fix](#the-recent-bug-fix)
7. [Code Walkthrough](#code-walkthrough)
8. [Common Pitfalls & How We Avoid Them](#common-pitfalls--how-we-avoid-them)

---

## The Problem We're Solving

### What Users Want
"I want a glowing border around **my monitor(s)** that looks correct regardless of:
- How many monitors I have (1, 2, 3, 4+)
- What resolution each monitor is (1080p, 4K, 5K)
- What DPI scaling Windows uses (100%, 125%, 150%, 200%)
- Whether monitors have different DPI settings (mixed-DPI)"

### Why This Is Hard
Windows has evolved over 30+ years with different display technologies:
- **1990s**: Everyone had 800x600 or 1024x768 monitors at 96 DPI
- **2000s**: Higher resolutions (1920x1080) but still 96 DPI
- **2010s**: 4K monitors (3840x2160) at 96 DPI = text too small to read!
- **Today**: Per-monitor DPI scaling, mixed-DPI setups, 5K/8K displays

Each monitor can now have **different physical sizes, resolutions, AND scaling** simultaneously.

---

## Understanding DPI Basics

### What is DPI?

**DPI = Dots Per Inch** - how many pixels fit in one physical inch of screen.

```
Low DPI (96):    ████████  8 pixels per inch = BIGGER pixels (easier to see)
High DPI (192):  ████████████████  16 pixels per inch = SMALLER pixels (sharper)
```

### Why DPI Matters

A **24-inch 1080p monitor** has **bigger pixels** than a **15-inch 4K laptop screen**.

```
24" 1080p Monitor:          15" 4K Laptop:
1920x1080 pixels            3840x2160 pixels
92 DPI                      294 DPI
Pixel size: 0.28mm          Pixel size: 0.09mm (3x smaller!)
```

**Problem**: If you draw text at "12 pixels tall" on both:
- 1080p monitor: Readable (12 × 0.28mm = 3.4mm tall)
- 4K laptop: Tiny! (12 × 0.09mm = 1.1mm tall = can't read!)

### DPI Scaling to the Rescue

Windows applies **scaling** to make things the same physical size:

```
                  Physical Pixels    DPI Scale    Logical Pixels
1080p @ 100%:     1920x1080         ÷ 1.0   =    1920x1080
4K @ 150%:        3840x2160         ÷ 1.5   =    2560x1440
4K @ 200%:        3840x2160         ÷ 2.0   =    1920x1080
```

**Key Insight**: After scaling, 4K @ 200% looks the same size as 1080p @ 100%!

### The DPI Scale Formula

```
DPI Scale Factor = Monitor DPI / 96

Examples:
96 DPI   ÷ 96 = 1.0   (100% scaling - no scaling)
120 DPI  ÷ 96 = 1.25  (125% scaling)
144 DPI  ÷ 96 = 1.5   (150% scaling)
192 DPI  ÷ 96 = 2.0   (200% scaling)
```

**Why 96?** Historical: Windows assumed 96 DPI as the baseline in the 1990s.

---

## How Windows Handles Multiple Monitors

### Virtual Screen Space

Windows creates one big "virtual desktop" combining all monitors:

```
Example: 3 monitors

Monitor 3 (Left)        Monitor 2 (Primary)      Monitor 1 (Right)
2560x1440 @ 150%        2560x1440 @ 150%         1920x1080 @ 100%
Physical position:      Physical position:       Physical position:
X: -2560 to 0          X: 0 to 2560             X: 2560 to 4480
Y: 0 to 1440           Y: 0 to 1440             Y: 0 to 1080

Virtual Screen Coordinates (in physical pixels):
       -2560              0                2560              4480
         ↓                ↓                  ↓                ↓
    ┌────────┐       ┌────────┐         ┌────────┐
    │ Mon 3  │       │ Mon 2  │         │ Mon 1  │
    │ 2560px │       │ 2560px │         │ 1920px │
    │ @150%  │       │ @150%  │         │ @100%  │
    └────────┘       └────────┘         └────────┘
```

**Critical Concept**: Coordinates are in **physical pixels**, but each monitor has **different DPI scaling**.

### Taskbar Working Area

Each monitor has a "working area" that **excludes** the taskbar:

```
Full Screen Bounds:     0, 0, 1920, 1080
Working Area (taskbar   0, 0, 1920, 1040  ← 40 pixels shorter (taskbar height)
at bottom):
```

**Why we use `WorkingArea`**: So our edge light doesn't cover the taskbar!

---

## The WPF Challenge

### WPF Uses "Device Independent Pixels" (DIPs)

WPF doesn't work directly with physical pixels. It uses **DIPs** (also called "logical pixels"):

```
Physical Pixels (what monitor actually displays)
        ↓
    DPI Scaling (Windows applies this)
        ↓
Device Independent Pixels (what WPF uses)
```

**The conversion**:
```csharp
Physical Pixels ÷ DPI Scale = DIPs

Example on 4K @ 150%:
3840 pixels ÷ 1.5 = 2560 DIPs
```

### Why This Matters for Our App

When we position a WPF window, we MUST use DIPs:

```csharp
// ❌ WRONG - Using physical pixels directly
window.Left = 2560;   // Will appear at WRONG position on 150% DPI!

// ✅ RIGHT - Convert to DIPs first  
window.Left = 2560 / 1.5;  // = 1706.67 DIPs (appears correctly)
```

### The Mixed-DPI Nightmare

When you have monitors with **different DPI settings**, each needs **different conversion**:

```
Monitor 2 @ 150%:  Physical 2560 ÷ 1.5 = 1706.67 DIPs
Monitor 1 @ 100%:  Physical 1920 ÷ 1.0 = 1920 DIPs
```

**If you use the wrong DPI scale for a monitor, the window appears in the wrong place!**

---

## Our Solution Deep Dive

### Step 1: Get DPI for EACH Monitor (Not Just Primary)

We use the Windows API `GetDpiForMonitor`:

```csharp
[DllImport("shcore.dll")]
private static extern int GetDpiForMonitor(
    IntPtr hmonitor,      // Handle to the monitor
    int dpiType,          // Type of DPI (we use MDT_EFFECTIVE_DPI)
    out uint dpiX,        // Returns DPI for X axis
    out uint dpiY         // Returns DPI for Y axis
);
```

**What `MDT_EFFECTIVE_DPI` means**: The DPI that Windows actually uses for scaling (matches what user sees in Settings).

### Step 2: Get Monitor Handle from Position

To call `GetDpiForMonitor`, we need a monitor handle (HMONITOR). We get it from screen coordinates:

```csharp
[DllImport("user32.dll")]
private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

// Find which monitor contains this point
var centerPoint = new POINT
{
    x = screen.Bounds.X + screen.Bounds.Width / 2,   // Center of screen
    y = screen.Bounds.Y + screen.Bounds.Height / 2
};

IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);
```

**Why center point?** Ensures we get the correct monitor even if bounds overlap slightly.

### Step 3: Calculate DPI Scale Factor

```csharp
private (double dpiScaleX, double dpiScaleY) GetDpiForScreen(Screen screen)
{
    try
    {
        // Get monitor handle
        var centerPoint = new POINT
        {
            x = screen.Bounds.X + screen.Bounds.Width / 2,
            y = screen.Bounds.Y + screen.Bounds.Height / 2
        };
        
        IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);
        
        if (hMonitor != IntPtr.Zero)
        {
            // Get DPI values
            int result = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, 
                out uint dpiX, out uint dpiY);
                
            if (result == 0) // S_OK (success)
            {
                // Convert from DPI to scale factor
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
    }
    catch
    {
        // Fall through to default
    }
    
    // Fallback: assume 100% scaling
    return (1.0, 1.0);
}
```

**Example outputs**:
- Monitor @ 96 DPI → (1.0, 1.0) = 100% scaling
- Monitor @ 144 DPI → (1.5, 1.5) = 150% scaling
- Monitor @ 192 DPI → (2.0, 2.0) = 200% scaling

### Step 4: Position Windows Correctly

Now we can position each monitor's window using its **specific DPI scale**:

```csharp
private MonitorWindowContext CreateMonitorWindow(Screen screen)
{
    var window = new Window { /* ... */ };
    
    // Get the correct DPI scale for THIS specific screen
    var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);
    
    // Get physical pixel coordinates
    var workingArea = screen.WorkingArea;
    
    // Convert physical pixels to WPF DIPs using the correct per-monitor DPI
    window.Left = workingArea.X / screenDpiX;
    window.Top = workingArea.Y / screenDpiY;
    window.Width = workingArea.Width / screenDpiX;
    window.Height = workingArea.Height / screenDpiY;
    
    // Store DPI in context for later use (hole punch effect)
    var ctx = new MonitorWindowContext
    {
        Window = window,
        Screen = screen,
        DpiScaleX = screenDpiX,
        DpiScaleY = screenDpiY,
        // ...
    };
    
    return ctx;
}
```

### Visual Example of Positioning

```
Setup: 2 monitors with different DPI

Monitor 2 (Primary) @ 150%      Monitor 1 @ 100%
Physical: 0, 0, 2560, 1440      Physical: 2560, 0, 1920, 1080
DPI Scale: 1.5                  DPI Scale: 1.0

Conversion to DIPs:
Monitor 2:                      Monitor 1:
Left:   0 ÷ 1.5 = 0            Left:   2560 ÷ 1.0 = 2560
Top:    0 ÷ 1.5 = 0            Top:    0 ÷ 1.0 = 0
Width:  2560 ÷ 1.5 = 1706.67   Width:  1920 ÷ 1.0 = 1920
Height: 1440 ÷ 1.5 = 960       Height: 1080 ÷ 1.0 = 1080
```

**Result**: Each window appears **exactly** where it should on its monitor!

---

## The Recent Bug Fix

### What Was Broken (v1.10.1 and earlier)

The code was using the **primary monitor's DPI** for **all monitors**:

```csharp
// BAD CODE (before fix):
window.Left = workingArea.X / _dpiScaleX;    // ❌ Uses PRIMARY monitor DPI!
window.Top = workingArea.Y / _dpiScaleY;     // ❌ Wrong for other monitors!
```

Where `_dpiScaleX` was set once from the primary monitor:
```csharp
_dpiScaleX = (dpiScaleX + dpiScaleY) / 2.0;  // Cached from primary monitor only
```

### Real User Scenario That Failed

**Setup**:
- Monitor 2 (Primary): 4K @ 150% DPI (scale = 1.5)
- Monitor 1: 1080p @ 100% DPI (scale = 1.0)

**What Happened**:
```
Monitor 1 should appear at X = 3840 (physical pixels)

Bug calculation:
3840 ÷ 1.5 = 2560 DIPs  ❌ WRONG! Used primary monitor's 1.5 scale

Correct calculation:
3840 ÷ 1.0 = 3840 DIPs  ✅ Should use Monitor 1's 1.0 scale

Result: Window appeared 1280 pixels to the LEFT (3840 - 2560 = 1280)
This caused it to span across TWO monitors!
```

### The Fix (v1.10.2)

Calculate DPI **per monitor** before positioning:

```csharp
// GOOD CODE (after fix):
// Get the correct DPI scale for THIS specific screen
var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);

// Now use the correct scale
window.Left = workingArea.X / screenDpiX;   // ✅ Correct DPI!
window.Top = workingArea.Y / screenDpiY;    // ✅ Correct DPI!
```

**Before vs After**:

```
BEFORE (v1.10.1):
┌─────────┐     ┌─────────┐     ┌─────────┐
│         │     │    Li|g │ht  │         │  ← Edge light spanning 2 monitors!
│         │     │  Main  │     │         │
└─────────┘     └─────────┘     └─────────┘
  Monitor 3       Monitor 2       Monitor 1
  @ 150%          @ 150%          @ 100%
                                  
AFTER (v1.10.2):
┌─────────┐     ┌─────────┐     ┌─────────┐
│  Light  │     │  Light  │     │  Light  │  ← Each monitor has correct border!
│         │     │  Main   │     │         │
└─────────┘     └─────────┘     └─────────┘
  Monitor 3       Monitor 2       Monitor 1
```

---

## Code Walkthrough

Let's trace through the code for a real scenario.

### Scenario: User with 2 monitors enables "Show on All Monitors"

**Setup**:
- Monitor 2 (Primary): 2560x1440 @ 150% DPI at position (0, 0)
- Monitor 1: 1920x1080 @ 100% DPI at position (2560, 0)

### Flow:

#### 1. User Clicks "All Monitors" Button

```csharp
// In ControlWindow.xaml.cs
private void AllMonitors_Click(object sender, RoutedEventArgs e)
{
    mainWindow.ToggleAllMonitors();  // Call main window method
}
```

#### 2. Toggle All Monitors Mode

```csharp
// In MainWindow.xaml.cs
public void ToggleAllMonitors()
{
    showOnAllMonitors = !showOnAllMonitors;
    
    if (showOnAllMonitors)
    {
        ShowOnAllMonitors();  // Create windows for other monitors
    }
    // ...
}
```

#### 3. Create Windows for Each Monitor

```csharp
private void ShowOnAllMonitors()
{
    // Get all connected monitors
    availableMonitors = Screen.AllScreens;  // Returns array of Screen objects
    
    // Create a window for each monitor except the main one
    for (int i = 0; i < availableMonitors.Length; i++)
    {
        if (i != currentMonitorIndex)  // Skip the primary (already exists)
        {
            var monitorCtx = CreateMonitorWindow(availableMonitors[i]);
            additionalMonitorWindows.Add(monitorCtx);
            monitorCtx.Window.Show();
        }
    }
}
```

**At this point**: `availableMonitors[0]` = Monitor 1, `availableMonitors[1]` = Monitor 2

#### 4. Create Monitor Window (The Critical Part!)

```csharp
private MonitorWindowContext CreateMonitorWindow(Screen screen)
{
    // Create transparent WPF window
    var window = new Window
    {
        AllowsTransparency = true,
        Background = Brushes.Transparent,
        WindowStyle = WindowStyle.None,
        Topmost = true,
        // ...
    };

    // Get working area (excludes taskbar)
    var workingArea = screen.WorkingArea;
    // For Monitor 1: X=2560, Y=0, Width=1920, Height=1040 (physical pixels)
    
    // ⭐ KEY FIX: Get DPI for THIS monitor
    var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);
    // For Monitor 1 @ 100%: returns (1.0, 1.0)
    // For Monitor 2 @ 150%: returns (1.5, 1.5)
    
    // Convert physical pixels to WPF DIPs
    window.Left = workingArea.X / screenDpiX;
    // Monitor 1: 2560 / 1.0 = 2560 DIPs ✅
    // (Old bug: 2560 / 1.5 = 1706 DIPs ❌ - wrong position!)
    
    window.Top = workingArea.Y / screenDpiY;
    // Monitor 1: 0 / 1.0 = 0 DIPs
    
    window.Width = workingArea.Width / screenDpiX;
    // Monitor 1: 1920 / 1.0 = 1920 DIPs
    
    window.Height = workingArea.Height / screenDpiY;
    // Monitor 1: 1040 / 1.0 = 1040 DIPs
    
    // Create the visual elements (Path with geometry, hover ring, etc.)
    // ... (code for creating UI elements)
    
    // Store everything in context
    var ctx = new MonitorWindowContext
    {
        Window = window,
        Screen = screen,
        DpiScaleX = screenDpiX,  // Save for later use
        DpiScaleY = screenDpiY,
        BorderPath = path,
        HoverRing = hoverRing,
        // ...
    };
    
    return ctx;
}
```

#### 5. Get DPI For Screen (Win32 API Calls)

```csharp
private (double dpiScaleX, double dpiScaleY) GetDpiForScreen(Screen screen)
{
    try
    {
        // Step 5a: Get center point of the screen in physical pixels
        var centerPoint = new POINT
        {
            x = screen.Bounds.X + screen.Bounds.Width / 2,
            y = screen.Bounds.Y + screen.Bounds.Height / 2
        };
        // For Monitor 1: x = 2560 + 1920/2 = 3520, y = 0 + 1080/2 = 540
        
        // Step 5b: Ask Windows "which monitor contains this point?"
        IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);
        // Returns a handle (HMONITOR) - like a pointer to the monitor
        
        if (hMonitor != IntPtr.Zero)  // Valid handle?
        {
            // Step 5c: Ask Windows "what's the DPI of this monitor?"
            int result = GetDpiForMonitor(
                hMonitor,               // The monitor handle
                MDT_EFFECTIVE_DPI,      // Type: effective DPI (what user sees)
                out uint dpiX,          // Receives X DPI (e.g., 96, 144, 192)
                out uint dpiY           // Receives Y DPI
            );
            
            if (result == 0)  // S_OK = success
            {
                // Step 5d: Convert DPI to scale factor
                return (dpiX / 96.0, dpiY / 96.0);
                // Monitor 1 @ 96 DPI: (96/96, 96/96) = (1.0, 1.0)
                // Monitor 2 @ 144 DPI: (144/96, 144/96) = (1.5, 1.5)
            }
        }
    }
    catch
    {
        // If anything fails, fall through
    }
    
    // Fallback: assume 100% scaling
    return (1.0, 1.0);
}
```

#### 6. Later: Hole Punch Effect Uses Saved DPI

When the mouse moves near the edge light, we need to calculate if the cursor is near the border:

```csharp
private void HandleMouseMove(int screenX, int screenY)
{
    // For each monitor window
    foreach (var ctx in additionalMonitorWindows)
    {
        ApplyHolePunchEffect(
            screenX, screenY,    // Mouse position in physical screen pixels
            ctx.Screen,           // Which monitor
            ctx.DpiScaleX,        // ⭐ Use saved DPI scale
            ctx.DpiScaleY,
            // ... other params
        );
    }
}

private void ApplyHolePunchEffect(
    int screenX, int screenY,
    Screen screen,
    double dpiScaleX, double dpiScaleY,
    // ...
)
{
    // Convert mouse position from physical screen pixels to window DIPs
    double relX = (screenX - screen.WorkingArea.X) / dpiScaleX;
    double relY = (screenY - screen.WorkingArea.Y) / dpiScaleY;
    
    // Example: Mouse at physical (3520, 540) on Monitor 1
    // relX = (3520 - 2560) / 1.0 = 960 DIPs
    // relY = (540 - 0) / 1.0 = 540 DIPs
    
    // Now we can check if cursor is near the frame border
    var windowPt = new Point(relX, relY);
    bool nearFrame = /* geometry calculations */;
    
    if (nearFrame)
    {
        // Create circular hole in the edge light at cursor position
        // ...
    }
}
```

---

## Common Pitfalls & How We Avoid Them

### Pitfall 1: Using Primary Monitor DPI for All Monitors

**Wrong**:
```csharp
// Cache DPI once from primary monitor
_dpiScaleX = 1.5;

// Use it for all monitors
foreach (var screen in screens)
{
    window.Left = screen.Bounds.X / _dpiScaleX;  // ❌ Wrong for non-primary!
}
```

**Right**:
```csharp
foreach (var screen in screens)
{
    var (dpiX, dpiY) = GetDpiForScreen(screen);  // ✅ Get DPI for each monitor
    window.Left = screen.Bounds.X / dpiX;
}
```

### Pitfall 2: Using Bounds Instead of WorkingArea

**Wrong**:
```csharp
window.Width = screen.Bounds.Width / dpiScale;  // ❌ Covers taskbar!
```

**Right**:
```csharp
window.Width = screen.WorkingArea.Width / dpiScale;  // ✅ Excludes taskbar
```

### Pitfall 3: Forgetting to Convert Coordinates

**Wrong**:
```csharp
// Mouse hook gives physical screen pixels
int screenX = 3520, screenY = 540;

// Directly compare to window coordinates (which are in DIPs)
if (screenX > window.Left) // ❌ Comparing physical pixels to DIPs!
```

**Right**:
```csharp
int screenX = 3520, screenY = 540;

// Convert to window-relative DIPs first
double relX = (screenX - screen.WorkingArea.X) / dpiScaleX;
double relY = (screenY - screen.WorkingArea.Y) / dpiScaleY;

if (relX > 0) // ✅ Comparing DIPs to DIPs
```

### Pitfall 4: Assuming All Monitors Have Same Resolution

**Wrong**:
```csharp
// Hard-code window size
window.Width = 1920;  // ❌ What if monitor is 2560 wide?
```

**Right**:
```csharp
// Use actual monitor size
window.Width = screen.WorkingArea.Width / dpiScale;  // ✅ Adapts to monitor
```

### Pitfall 5: Not Handling DPI Changes at Runtime

**Scenario**: User changes DPI scaling in Windows Settings while app is running.

**What we do**:
```csharp
// In Window.Loaded event (fires after DPI is applied)
window.Loaded += (s, e) =>
{
    // WPF now knows the actual DPI, recalculate if needed
    var source = PresentationSource.FromVisual(window);
    if (source != null)
    {
        double wpfDpiX = source.CompositionTarget.TransformToDevice.M11;
        double wpfDpiY = source.CompositionTarget.TransformToDevice.M22;
        
        // If different from our calculation, update
        if (Math.Abs(wpfDpiX - ctx.DpiScaleX) > 0.01)
        {
            ctx.DpiScaleX = wpfDpiX;
            ctx.DpiScaleY = wpfDpiY;
            // Reposition window
        }
    }
};
```

---

## Summary for Your Boss

### What We Built
A multi-monitor edge light application that correctly handles:
- ✅ 1-4+ monitors simultaneously
- ✅ Different resolutions per monitor (1080p, 4K, 5K)
- ✅ Different DPI scaling per monitor (100%, 125%, 150%, 200%)
- ✅ Mixed-DPI scenarios (e.g., laptop @ 150% + external @ 100%)
- ✅ Hot-plug/unplug monitors while running
- ✅ Taskbar exclusion on all monitors

### The Technical Challenge
Windows provides monitor coordinates in **physical pixels**, but WPF requires **Device Independent Pixels (DIPs)**.

The conversion factor (**DPI scale**) is **different for each monitor**.

### Our Solution
1. Use Win32 API `GetDpiForMonitor` to get DPI for each monitor individually
2. Convert physical pixel coordinates to DIPs using the correct per-monitor DPI
3. Cache DPI scale in each window's context for coordinate transformations
4. Handle runtime DPI changes gracefully

### The Recent Bug We Fixed
In v1.10.1, we incorrectly used the primary monitor's DPI for all monitors, causing windows to appear in wrong positions on mixed-DPI setups.

In v1.10.2, we fixed this by calculating DPI **per monitor** before creating each window.

### Why This Matters
Users increasingly have mixed-DPI setups (laptop + external monitor, or 4K + 1080p). Our fix ensures the app works correctly in all scenarios.

---

## Additional Resources

**Microsoft Documentation**:
- [High DPI Desktop Application Development on Windows](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
- [GetDpiForMonitor function](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)
- [MonitorFromPoint function](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfrompoint)

**WPF DPI**:
- [WPF and DPI Scaling](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/wpf-graphics-rendering-overview?view=netframeworkdesktop-4.8#dpi-and-device-independent-pixels)

**Code References**:
- `MainWindow.xaml.cs` lines 78-84: Win32 DPI API declarations
- `MainWindow.xaml.cs` lines 909-933: `CreateMonitorWindow()` method
- `MainWindow.xaml.cs` lines 1164-1195: `GetDpiForScreen()` helper method

---

**Questions to Anticipate from Your Boss**:

**Q**: "Why not just use one DPI scale for everything?"  
**A**: Because Windows allows each monitor to have different DPI. Using the wrong DPI causes windows to appear in wrong positions (1000+ pixel offsets).

**Q**: "Can't WPF handle this automatically?"  
**A**: WPF handles DPI for a single window well, but when creating multiple windows on different monitors, we must explicitly calculate DPI per monitor using Win32 APIs.

**Q**: "What happens if we don't do this?"  
**A**: Edge lights appear offset (sometimes spanning two monitors), cursor hole punch appears in wrong place, and buttons are not clickable where users expect.

**Q**: "Is this tested?"  
**A**: Yes, verified on 4-monitor setup with mixed DPI (4K @ 150%, 1080p @ 100%). User confirmed: "that fixed it very nice job".

**Q**: "What if monitors are hot-plugged?"  
**A**: We refresh the monitor list (`Screen.AllScreens`) before creating windows, so newly connected monitors are detected.

---

*Document created for code review - explains multi-monitor DPI handling in Windows Edge Light v1.10.2*
