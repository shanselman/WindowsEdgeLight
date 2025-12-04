# Junior Engineer Catch-Up Guide
## Windows Edge Light - Complete Architecture & Learning Guide

**Created:** November 2025  
**For:** Junior engineers inheriting or reviewing this codebase  
**Purpose:** Understand the architecture, Win32 interop, and WPF concepts used in this application

---

## Table of Contents
1. [What This App Does](#what-this-app-does)
2. [Architecture Overview](#architecture-overview)
3. [Key Components Explained](#key-components-explained)
4. [Win32 Interop Deep Dive](#win32-interop-deep-dive)
5. [WPF Concepts Used](#wpf-concepts-used)
6. [Code Flow Diagrams](#code-flow-diagrams)
7. [Code Review Checklist](#code-review-checklist)
8. [Common Pitfalls & Gotchas](#common-pitfalls--gotchas)
9. [Learning Resources](#learning-resources)
10. [Testing & Debugging Tips](#testing--debugging-tips)

---

## What This App Does

Windows Edge Light creates a **glowing border overlay** around your primary monitor - like a ring light for video calls. The key challenge: it needs to be **visible** but **not interfere** with your work (clicks pass through it).

**Use cases:**
- Professional lighting for video conferences
- Ambient lighting for streaming
- Visual focus aid for multi-monitor setups

---

## Architecture Overview

### High-Level View
```
┌─────────────────────────────────────────────────────────────┐
│                         USER'S VIEW                          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                                                       │  │
│  │  ╔═══════════════════════════════════════════════╗  │  │
│  │  ║  Glowing Border (MainWindow - transparent)    ║  │  │
│  │  ║                                               ║  │  │
│  │  ║   Your apps work here (click-through)        ║  │  │
│  │  ║                                               ║  │  │
│  │  ╚═══════════════════════════════════════════════╝  │  │
│  │                                                       │  │
│  │           [🔅 🔆 💡 🖥️ ✖] ← ControlWindow          │  │
│  └───────────────────────────────────────────────────────┘  │
│                    System Tray Icon 🔔                      │
└─────────────────────────────────────────────────────────────┘
```

### Component Layers
```
┌─────────────────────────────────────────────────────┐
│ Application Layer (App.xaml.cs)                     │
│ - Startup logic                                     │
│ - Update checking (Updatum)                         │
└─────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────┐
│ UI Layer                                            │
│ ┌───────────────────┐  ┌─────────────────────────┐ │
│ │ MainWindow        │  │ ControlWindow           │ │
│ │ (Overlay)         │←→│ (Button Panel)          │ │
│ └───────────────────┘  └─────────────────────────┘ │
│ ┌───────────────────┐  ┌─────────────────────────┐ │
│ │ UpdateDialog      │  │ DownloadProgressDialog  │ │
│ └───────────────────┘  └─────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────┐
│ Win32 Interop Layer                                 │
│ - Click-through window styles                      │
│ - Global hotkey registration                       │
│ - System-wide mouse hooks                          │
└─────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────┐
│ Windows OS (user32.dll, kernel32.dll)              │
└─────────────────────────────────────────────────────┘
```

---

## Key Components Explained

### 1. App.xaml.cs - The Application Controller

**File:** `WindowsEdgeLight/App.xaml.cs`

**Responsibilities:**
- Application entry point (`OnStartup`)
- Manages automatic updates via **Updatum** library
- Shows update dialogs when new versions available

**Key Code:**
```csharp
internal static readonly UpdatumManager AppUpdater = new("shanselman", "WindowsEdgeLight")
{
    FetchOnlyLatestRelease = true,
    InstallUpdateSingleFileExecutableName = "WindowsEdgeLight",
};
```

**What happens on startup:**
1. App launches
2. MainWindow is created and shown
3. Background task checks GitHub releases for updates
4. If update found, shows `UpdateDialog`
5. User can download and install update

**Update Flow:**
```
OnStartup()
    ↓
CheckForUpdatesAsync() (after 2 second delay)
    ↓
AppUpdater.CheckForUpdatesAsync()
    ↓
If update found → Show UpdateDialog
    ↓
User clicks Download → DownloadAndInstallUpdateAsync()
    ↓
Show DownloadProgressDialog
    ↓
Download complete → AppUpdater.InstallUpdateAsync()
    ↓
App closes and installer runs
```

---

### 2. MainWindow.xaml.cs - The Core Overlay

**File:** `WindowsEdgeLight/MainWindow.xaml.cs` (629 lines)

This is the **star** of the show. It's complex because it does **a lot**:

#### Responsibilities:
1. **Window Positioning** - Places itself on primary monitor
2. **Geometry Creation** - Draws the glowing border using WPF Path
3. **Click-through Magic** - Makes window transparent to mouse clicks
4. **Global Hotkeys** - Responds to Ctrl+Shift+L anywhere on Windows
5. **Mouse Tracking** - Monitors cursor position system-wide
6. **Proximity Effects** - Creates "hole" around cursor when nearby
7. **Multi-Monitor Support** - Can move between displays
8. **DPI Scaling** - Handles high-DPI displays (4K, 150% scaling, etc.)

#### Key Fields:
```csharp
private bool isLightOn = true;              // Is the effect currently visible?
private double currentOpacity = 1.0;        // Brightness level (0.2 to 1.0)
private IntPtr mouseHookHandle = IntPtr.Zero; // Handle to mouse hook
private Geometry? baseFrameGeometry;        // The border shape
private Rect? frameOuterRect;               // Outer border bounds
private Rect? frameInnerRect;               // Inner border bounds
private Screen[] availableMonitors;         // List of connected displays
```

#### The Window Setup Process:
```csharp
Window_Loaded()
    ↓
SetupWindow() → Positions on primary monitor with DPI scaling
    ↓
CreateFrameGeometry() → Draws the border using combined rectangles
    ↓
CreateControlWindow() → Shows the button panel
    ↓
Apply Win32 magic → WS_EX_TRANSPARENT | WS_EX_LAYERED
    ↓
RegisterHotKey() × 3 → Ctrl+Shift+L, Up, Down
    ↓
HwndSource.AddHook() → Listen for Windows messages
    ↓
InstallMouseHook() → Track mouse everywhere
```

---

### 3. ControlWindow.xaml.cs - The Button Panel

**File:** `WindowsEdgeLight/ControlWindow.xaml.cs` (48 lines)

**Purpose:** Floating control panel with 5 buttons

**Why a separate window?**
- MainWindow is click-through (you can't click it!)
- ControlWindow is **clickable** and always positioned at bottom-center
- Fades in on hover using WPF styles

**Buttons:**
1. 🔅 Decrease Brightness → `mainWindow.DecreaseBrightness()`
2. 🔆 Increase Brightness → `mainWindow.IncreaseBrightness()`
3. 💡 Toggle Light → `mainWindow.HandleToggle()`
4. 🖥️ Switch Monitor → `mainWindow.MoveToNextMonitor()`
5. ✖ Exit → `Application.Current.Shutdown()`

**Positioning Logic:**
```csharp
// In MainWindow.RepositionControlWindow()
controlWindow.Left = this.Left + (this.Width - controlWindow.Width) / 2;
controlWindow.Top = this.Top + this.Height - controlWindow.Height - 124;
```

---

### 4. Update Dialogs

**Files:**
- `UpdateDialog.xaml.cs` - Shows release notes and prompts to download
- `DownloadProgressDialog.xaml.cs` - Shows progress bar during download

**Update System Flow:**
```
GitHub Release detected
    ↓
UpdateDialog shows:
    - Version number (e.g., "v1.9")
    - Release notes/changelog
    - [Download] [Skip] buttons
    ↓
User clicks Download
    ↓
DownloadProgressDialog shows:
    - Progress bar (0-100%)
    - "Downloading update..."
    ↓
Download completes
    ↓
Confirmation dialog:
    "Install now and restart?"
    ↓
App closes, installer runs, new version launches
```

---

## Win32 Interop Deep Dive

### What is Win32 Interop?

**Simple Definition:**
- **Win32** = The old-school Windows API from the 1990s (written in C)
- **Interop** = "Interoperability" - making C# talk to that old C code
- **Why?** Some powerful Windows features aren't available in modern WPF

**Think of it as:** WPF is a fancy modern car, but sometimes you need to pop the hood and use old-school tools.

---

### How DllImport Works

```csharp
[DllImport("user32.dll")]
private static extern int GetWindowLong(IntPtr hwnd, int index);
```

**Breaking it down:**

| Part | Meaning |
|------|---------|
| `[DllImport("user32.dll")]` | "Import a function from user32.dll" |
| `user32.dll` | Windows system file with UI functions |
| `extern` | "This function exists outside my C# code" |
| `static` | Called on the class, not an instance |
| The method signature | Tells C# what parameters to expect |

**What happens at runtime:**
```
Your C# code calls GetWindowLong(hwnd, -20)
    ↓
.NET finds user32.dll on your system (C:\Windows\System32\user32.dll)
    ↓
.NET looks up the GetWindowLong function inside that DLL
    ↓
.NET converts your C# parameters to C format (marshaling)
    ↓
Calls the actual Windows C function
    ↓
.NET converts the result back to C# (unmarshaling)
    ↓
Returns the value to your C# code
```

---

### Interop Technique #1: Click-Through Window

**Location:** `MainWindow.xaml.cs`, lines 227-229

```csharp
var hwnd = new WindowInteropHelper(this).Handle;
int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
```

#### Understanding Window Handles (HWND)

Every window in Windows has a unique identifier called a **handle** (HWND).

```
Your WPF Window
    ↓
new WindowInteropHelper(this).Handle
    ↓
Returns: IntPtr (e.g., 0x00012345)
    ↓
This is the window's HWND - like a unique ID card
```

**Why handles matter:** Win32 functions need the HWND to know which window you're talking about.

#### The Window Style Flags

Windows has style "flags" that control behavior:

```csharp
const int GWL_EXSTYLE = -20;              // "Get Extended Style"
const int WS_EX_TRANSPARENT = 0x00000020; // Clicks pass through
const int WS_EX_LAYERED = 0x00080000;     // Allows transparency
```

These are **bit flags**. You combine them with bitwise OR (`|`):

```
Current style:  00000000 00000000 (no flags)
                    |
                    | (bitwise OR)
                    ↓
WS_EX_TRANSPARENT:  00000000 00100000
WS_EX_LAYERED:      10000000 00000000
                    ↓
Result:             10000000 00100000 (both flags set)
```

#### The Complete Flow:

```csharp
// Step 1: Get the window's handle
var hwnd = new WindowInteropHelper(this).Handle;

// Step 2: Read current window style flags
int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

// Step 3: Add the transparent and layered flags
int newStyle = extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED;

// Step 4: Apply the new style
SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
```

**Result:** Your window is now **visible** but **click-through**! Clicks go to whatever's behind it.

---

### Interop Technique #2: Global Hotkeys

**Location:** `MainWindow.xaml.cs`, lines 38-41, 232-234

```csharp
// The P/Invoke declarations
[DllImport("user32.dll")]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

// Usage
RegisterHotKey(hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_L);
```

#### Understanding Virtual Key Codes

**Virtual Key (VK)** codes are numbers representing keyboard keys:

```csharp
const uint VK_L = 0x4C;      // Letter L
const uint VK_UP = 0x26;     // Up arrow
const uint VK_DOWN = 0x28;   // Down arrow
```

[Full list: Virtual Key Codes on Microsoft Docs](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes)

#### Understanding Modifier Keys

```csharp
const uint MOD_CONTROL = 0x0002;  // Ctrl key
const uint MOD_SHIFT = 0x0004;    // Shift key
const uint MOD_ALT = 0x0001;      // Alt key (not used here)
```

Combine with bitwise OR:
```csharp
MOD_CONTROL | MOD_SHIFT  // Means "Ctrl + Shift"
```

#### The Registration Process:

```csharp
RegisterHotKey(
    hwnd,                        // Your window's handle
    HOTKEY_TOGGLE,               // ID = 1 (you choose this)
    MOD_CONTROL | MOD_SHIFT,     // Ctrl+Shift
    VK_L                         // L key
);
```

**What this tells Windows:**
> "Hey Windows, whenever someone presses Ctrl+Shift+L **anywhere** on the system (even in Chrome, Notepad, etc.), send message #1 to my window."

#### Receiving the Hotkey Event:

```csharp
private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    const int WM_HOTKEY = 0x0312;  // Windows message code for hotkey
    
    if (msg == WM_HOTKEY)
    {
        int hotkeyId = wParam.ToInt32();  // Which hotkey? (1, 2, or 3)
        
        switch (hotkeyId)
        {
            case HOTKEY_TOGGLE:           // 1
                ToggleLight();
                handled = true;
                break;
            case HOTKEY_BRIGHTNESS_UP:    // 2
                IncreaseBrightness();
                handled = true;
                break;
            case HOTKEY_BRIGHTNESS_DOWN:  // 3
                DecreaseBrightness();
                handled = true;
                break;
        }
    }
    
    return IntPtr.Zero;
}
```

#### Complete Hotkey Flow Diagram:

```
User presses Ctrl+Shift+L in Chrome
    ↓
Windows OS detects the key combination
    ↓
Windows checks: "Who registered this hotkey?"
    ↓
Finds: WindowsEdgeLight registered it with ID=1
    ↓
Windows sends WM_HOTKEY message to your window
    ↓
HwndSource hook intercepts the message
    ↓
HwndHook method is called
    ↓
Checks: msg == WM_HOTKEY? Yes!
    ↓
Checks: hotkeyId == 1? Yes!
    ↓
Calls ToggleLight()
    ↓
Border appears/disappears
```

**Critical:** Always unregister hotkeys when closing:
```csharp
protected override void OnClosed(EventArgs e)
{
    UnregisterHotKey(hwnd, HOTKEY_TOGGLE);
    UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_UP);
    UnregisterHotKey(hwnd, HOTKEY_BRIGHTNESS_DOWN);
    // ...
}
```

---

### Interop Technique #3: System-Wide Mouse Hook

**Location:** `MainWindow.xaml.cs`, lines 44-56, 248-257, 267-281

This is the **most complex** interop technique in the app.

#### What is a Windows Hook?

A **hook** lets your app "spy" on system events before other apps see them.

**Types of hooks:**
- `WH_KEYBOARD_LL` - Low-level keyboard events
- `WH_MOUSE_LL` - Low-level mouse events (used here)
- `WH_CBT` - Computer-based training events
- Many more...

#### The P/Invoke Declarations:

```csharp
[DllImport("user32.dll", SetLastError = true)]
private static extern IntPtr SetWindowsHookEx(
    int idHook,                    // Type of hook (WH_MOUSE_LL = 14)
    LowLevelMouseProc lpfn,        // Your callback function
    IntPtr hMod,                   // Module handle (your .exe)
    uint dwThreadId                // 0 = all threads (system-wide)
);

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool UnhookWindowsHookEx(IntPtr hhk);

[DllImport("user32.dll")]
private static extern IntPtr CallNextHookEx(
    IntPtr hhk, 
    int nCode, 
    IntPtr wParam, 
    IntPtr lParam
);
```

#### The Callback Delegate:

```csharp
private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
```

This defines the **signature** of your callback function.

#### Installing the Hook:

```csharp
private void InstallMouseHook()
{
    // Store callback to prevent garbage collection!
    mouseHookCallback = MouseHookProc;
    
    using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
    using var curModule = curProcess.MainModule;
    
    if (curModule != null)
    {
        mouseHookHandle = SetWindowsHookEx(
            WH_MOUSE_LL,                          // Low-level mouse hook
            mouseHookCallback,                    // Your function
            GetModuleHandle(curModule.ModuleName), // Your .exe
            0                                     // All threads
        );
    }
}
```

**Critical:** Store `mouseHookCallback` in a field! If you pass a local variable, C# garbage collector might delete it while Windows still needs it → **crash**.

#### The Callback Function:

```csharp
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
    {
        // lParam is a pointer to MSLLHOOKSTRUCT
        var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        
        // Can't update UI from hook thread! Must dispatch to UI thread
        Dispatcher.BeginInvoke(new Action(() => 
        {
            HandleMouseMove(hookStruct.pt.x, hookStruct.pt.y);
        }), DispatcherPriority.Input);
    }

    // CRITICAL: Always call this! It passes the event to the next hook
    return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
}
```

#### Understanding the Parameters:

| Parameter | Type | Meaning |
|-----------|------|---------|
| `nCode` | `int` | If < 0, pass to next hook immediately |
| `wParam` | `IntPtr` | Message type (WM_MOUSEMOVE, WM_LBUTTONDOWN, etc.) |
| `lParam` | `IntPtr` | Pointer to struct with mouse data |

#### Data Marshaling with Structs:

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct MSLLHOOKSTRUCT
{
    public POINT pt;           // Mouse position
    public uint mouseData;     // Wheel delta, etc.
    public uint flags;         // Event flags
    public uint time;          // Timestamp
    public IntPtr dwExtraInfo; // Extra info
}

[StructLayout(LayoutKind.Sequential)]
private struct POINT
{
    public int x;  // Screen X coordinate
    public int y;  // Screen Y coordinate
}
```

**What `[StructLayout(LayoutKind.Sequential)]` does:**
- Tells C# to lay out fields in memory **exactly** like C would
- Without this, C# might reorder fields for optimization
- Result: You can read C-formatted memory correctly

#### Extracting Data from Pointer:

```csharp
var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
```

**What happens:**
1. Windows writes mouse data to memory in C format
2. `lParam` is a pointer (memory address) to that data
3. `Marshal.PtrToStructure` reads that memory location
4. Converts raw bytes to C# `MSLLHOOKSTRUCT` object
5. Now you can access `hookStruct.pt.x` and `hookStruct.pt.y`

#### Thread Safety - The Critical Part!

```csharp
Dispatcher.BeginInvoke(new Action(() => 
{
    HandleMouseMove(hookStruct.pt.x, hookStruct.pt.y);
}), DispatcherPriority.Input);
```

**Why this is necessary:**
- The hook callback runs on **Windows' hook thread**, not your UI thread
- WPF UI elements can **only** be updated from the UI thread
- Trying to update UI from hook thread → **cross-thread exception**
- `Dispatcher.BeginInvoke` schedules the work on the UI thread

#### The Complete Mouse Hook Flow:

```
Mouse moves anywhere on screen
    ↓
Windows detects movement
    ↓
Windows calls ALL registered mouse hooks (chain pattern)
    ↓
Your MouseHookProc is called (on Windows hook thread)
    ↓
Extract mouse coordinates from lParam
    ↓
Dispatch to UI thread via Dispatcher
    ↓
HandleMouseMove() executes (on UI thread)
    ↓
Calculate distance to border
    ↓
If close enough: draw "hole" around cursor
    ↓
Return from MouseHookProc
    ↓
MUST call CallNextHookEx() to continue the chain
    ↓
Next app's hook gets the event
```

#### Uninstalling the Hook:

```csharp
private void UninstallMouseHook()
{
    if (mouseHookHandle != IntPtr.Zero)
    {
        UnhookWindowsHookEx(mouseHookHandle);
        mouseHookHandle = IntPtr.Zero;
    }
}
```

**When to uninstall:**
- When window closes (`OnClosed` method)
- Before app exits
- **Critical:** Forgetting to unhook = hook stays active even after app closes → memory leak and potential crashes

---

### Why Not Use Managed Alternatives?

| Feature | Managed (C#/WPF) Way | Win32 Way | Why Win32 Wins |
|---------|---------------------|-----------|----------------|
| **Click-through** | ❌ Not possible | ✅ `WS_EX_TRANSPARENT` | WPF has no API for this |
| **Global hotkeys** | ❌ Only when focused | ✅ `RegisterHotKey` | Works system-wide |
| **Mouse tracking** | ❌ Only in your window | ✅ `SetWindowsHookEx` | Tracks everywhere |
| **Window positioning** | ⚠️ DPI issues | ✅ `GetSystemMetrics` | More accurate |

**Rule of thumb:** Use Win32 only when WPF can't do it. Win32 is more powerful but also more dangerous.

---

## WPF Concepts Used

### 1. Transparent Windows

```xml
<Window AllowsTransparency="True"
        Background="Transparent"
        WindowStyle="None">
```

- `AllowsTransparency="True"` - Enables per-pixel transparency
- `Background="Transparent"` - Makes background fully transparent
- `WindowStyle="None"` - Removes title bar and borders

### 2. Path Geometry (The Border Drawing)

**Location:** `MainWindow.xaml.cs`, `CreateFrameGeometry()` method

```csharp
// Create outer rounded rectangle
var outerRect = new RectangleGeometry(
    new Rect(0, 0, width, height), 
    outerRadius,  // 100px corner radius
    outerRadius
);

// Create inner rounded rectangle (smaller)
var innerRect = new RectangleGeometry(
    new Rect(frameThickness, frameThickness, 
            width - (frameThickness * 2), 
            height - (frameThickness * 2)), 
    innerRadius,  // 60px corner radius
    innerRadius
);

// Subtract inner from outer = border frame!
var frameGeometry = new CombinedGeometry(
    GeometryCombineMode.Exclude,  // Subtract operation
    outerRect, 
    innerRect
);

EdgeLightBorder.Data = frameGeometry;
```

**Visual representation:**
```
Outer Rectangle (1920x1080):
┌─────────────────────────────────────┐
│                                     │
│  Inner Rectangle (1760x920):       │
│  ┌───────────────────────────┐     │
│  │                           │     │
│  │    (Empty space)          │     │
│  │                           │     │
│  └───────────────────────────┘     │
│                                     │
└─────────────────────────────────────┘

After CombinedGeometry.Exclude:
┌─────────────────────────────────────┐
█████████████████████████████████████████ ← This is the frame!
███┌───────────────────────────┐███████
███│                           │███████
███│    (Transparent)          │███████
███│                           │███████
███└───────────────────────────┘███████
█████████████████████████████████████████
└─────────────────────────────────────┘
```

### 3. Gradient Brush & Effects

**Location:** `MainWindow.xaml`

```xml
<Path.Fill>
    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
        <GradientStop Offset="0.0" Color="#FFFFFF" />
        <GradientStop Offset="0.3" Color="#F0F0F0" />
        <GradientStop Offset="0.5" Color="#FFFFFF" />
        <GradientStop Offset="0.7" Color="#F0F0F0" />
        <GradientStop Offset="1.0" Color="#FFFFFF" />
    </LinearGradientBrush>
</Path.Fill>
<Path.Effect>
    <DropShadowEffect
        BlurRadius="76"
        Opacity="1"
        ShadowDepth="0"
        Color="#FFFFFF" />
</Path.Effect>
```

**Breakdown:**
- `LinearGradientBrush` - Creates smooth color transitions
- `StartPoint="0,0"` - Top-left corner
- `EndPoint="1,1"` - Bottom-right corner (diagonal gradient)
- `GradientStop` - Color at specific position (0.0 = start, 1.0 = end)
- `DropShadowEffect` with `ShadowDepth="0"` = **glow** effect (not a shadow!)
- `BlurRadius="76"` - Large blur = soft glow

### 4. DPI Scaling

**Location:** `MainWindow.xaml.cs`, `SetupWindowForScreen()` method

```csharp
var source = PresentationSource.FromVisual(this);
double dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
double dpiScaleY = source.CompositionTarget.TransformToDevice.M22;

// Convert physical pixels to WPF Device Independent Pixels (DIPs)
this.Left = workingArea.X / dpiScaleX;
this.Top = workingArea.Y / dpiScaleY;
this.Width = workingArea.Width / dpiScaleX;
this.Height = workingArea.Height / dpiScaleY;
```

**Understanding DPI:**
- **Physical pixels** - Actual pixels on screen (e.g., 3840×2160 for 4K)
- **Device Independent Pixels (DIPs)** - WPF's logical units (96 DPI baseline)
- **DPI scale factor** - Ratio between physical and logical (150% scaling = 1.5)

**Example calculation:**
```
Monitor: 3840×2160 pixels with 150% scaling
Physical width: 3840 pixels
DPI scale: 1.5
WPF logical width: 3840 / 1.5 = 2560 DIPs
```

**Why this matters:** Without DPI scaling, your window would be too small on high-DPI displays.

### 5. XAML Data Binding (Not Used Much Here)

This app mostly uses code-behind instead of MVVM pattern. This is fine for small utilities.

**Where binding could be used:**
```csharp
// Current approach (code-behind):
EdgeLightBorder.Opacity = currentOpacity;

// MVVM approach (binding):
// <Path Opacity="{Binding CurrentOpacity}" />
// public double CurrentOpacity { get; set; }
```

**For your learning:** This app is simple enough that code-behind is acceptable. Larger apps benefit from MVVM separation.

---

## Code Flow Diagrams

### Application Startup Sequence

```
User double-clicks WindowsEdgeLight.exe
    ↓
App.xaml.cs → OnStartup()
    ↓
    ├─→ Create MainWindow
    ↓   └─→ Shows immediately
    ↓
    └─→ Task.Delay(2000) → CheckForUpdatesAsync()
        └─→ Background thread (doesn't block UI)
            ↓
            AppUpdater.CheckForUpdatesAsync()
            ↓
            If update found:
                ↓
                Show UpdateDialog (user choice)
                ↓
                If user clicks Download:
                    ↓
                    DownloadProgressDialog
                    ↓
                    Download ZIP from GitHub
                    ↓
                    Extract and replace .exe
                    ↓
                    App closes, new version launches
```

### MainWindow Initialization

```
MainWindow constructor
    ↓
InitializeComponent() (XAML parsing)
    ↓
SetupNotifyIcon() (system tray icon)
    ↓
Window_Loaded event fires
    ↓
    ├─→ SetupWindow()
    │       ↓
    │       Get primary monitor
    │       ↓
    │       Get DPI scale factor
    │       ↓
    │       Position window on monitor (with DPI correction)
    │
    ├─→ CreateFrameGeometry()
    │       ↓
    │       Calculate outer rectangle (full window size)
    │       ↓
    │       Calculate inner rectangle (with frame thickness)
    │       ↓
    │       Combine: outer - inner = frame
    │       ↓
    │       Apply to EdgeLightBorder.Data
    │
    ├─→ CreateControlWindow()
    │       ↓
    │       New ControlWindow(this)
    │       ↓
    │       Position at bottom-center
    │       ↓
    │       Show()
    │
    ├─→ Apply Win32 click-through style
    │       ↓
    │       GetWindowLong() → read current style
    │       ↓
    │       OR with WS_EX_TRANSPARENT | WS_EX_LAYERED
    │       ↓
    │       SetWindowLong() → apply new style
    │
    ├─→ RegisterHotKey() × 3
    │       ↓
    │       Ctrl+Shift+L → Toggle
    │       Ctrl+Shift+Up → Brightness Up
    │       Ctrl+Shift+Down → Brightness Down
    │
    ├─→ HwndSource.AddHook(HwndHook)
    │       ↓
    │       Listen for Windows messages (WM_HOTKEY, etc.)
    │
    └─→ InstallMouseHook()
            ↓
            SetWindowsHookEx(WH_MOUSE_LL, ...)
            ↓
            Now tracking all mouse movements
```

### Mouse Movement → Cursor Hole Effect

```
Mouse moves anywhere on screen
    ↓
Windows calls MouseHookProc()
    ↓
Check: Is it a WM_MOUSEMOVE event? → Yes
    ↓
Extract MSLLHOOKSTRUCT from lParam
    ↓
Get screen coordinates: (screenX, screenY)
    ↓
Dispatcher.BeginInvoke → Switch to UI thread
    ↓
HandleMouseMove(screenX, screenY)
    ↓
    ├─→ Convert screen coords to window coords
    │   (PointFromScreen)
    │
    ├─→ Calculate distance to nearest frame edge
    │       ↓
    │       If inside inner rect:
    │           → Distance to nearest inner edge
    │       If outside outer rect:
    │           → Distance to nearest outer edge
    │       Otherwise:
    │           → 0 (cursor is ON the frame)
    │
    ├─→ Is distance ≤ 100 pixels? → Yes (proximity triggered)
    │       ↓
    │       Calculate cursor position on frame
    │       ↓
    │       Show HoverCursorRing at that position
    │       ↓
    │       Create hole geometry:
    │           baseFrameGeometry - circleGeometry
    │       ↓
    │       Apply to EdgeLightBorder.Data
    │       ↓
    │       Result: Circular "hole" follows cursor!
    │
    └─→ Is distance > 100 pixels? → No proximity
            ↓
            Hide HoverCursorRing
            ↓
            Restore baseFrameGeometry (no hole)
```

### Hotkey Press Flow

```
User presses Ctrl+Shift+L (anywhere on Windows)
    ↓
Windows OS intercepts the key combination
    ↓
Checks registered hotkeys: Who registered Ctrl+Shift+L?
    ↓
Finds: WindowsEdgeLight, ID=1
    ↓
Windows sends WM_HOTKEY message to MainWindow
    ↓
HwndSource hook intercepts message
    ↓
Calls HwndHook(hwnd, msg=0x0312, wParam=1, lParam, ...)
    ↓
Check: msg == WM_HOTKEY? → Yes
    ↓
Extract hotkeyId from wParam → 1
    ↓
Switch (hotkeyId):
    case 1: ToggleLight()
        ↓
        isLightOn = !isLightOn
        ↓
        If ON: EdgeLightBorder.Visibility = Visible
        If OFF: EdgeLightBorder.Visibility = Collapsed
```

### Multi-Monitor Switching

```
User clicks "Switch Monitor" button
    ↓
ControlWindow.SwitchMonitor_Click()
    ↓
mainWindow.MoveToNextMonitor()
    ↓
    ├─→ Refresh availableMonitors = Screen.AllScreens
    │   (handles hot-plug/unplug of monitors)
    │
    ├─→ Check: More than 1 monitor? → Yes
    │
    ├─→ Cycle index: (currentMonitorIndex + 1) % monitorCount
    │   Example: Monitor 0 → Monitor 1 → Monitor 2 → Monitor 0...
    │
    ├─→ Get targetScreen = availableMonitors[newIndex]
    │
    ├─→ SetupWindowForScreen(targetScreen)
    │       ↓
    │       Get workingArea (excludes taskbar)
    │       ↓
    │       Get DPI scale for that monitor
    │       ↓
    │       Reposition window: Left, Top, Width, Height
    │
    ├─→ CreateFrameGeometry()
    │       ↓
    │       Recalculate border for new dimensions
    │
    └─→ RepositionControlWindow()
            ↓
            Move button panel to new monitor
```

---

## Code Review Checklist

### ✅ What's Good

1. **Proper cleanup in `OnClosed()`**
   - Unhooks mouse hook ✓
   - Unregisters hotkeys ✓
   - Disposes notify icon ✓
   - Closes control window ✓

2. **DPI awareness**
   - Correctly scales for high-DPI displays
   - Uses `PresentationSource.FromVisual()`

3. **Thread safety**
   - Mouse hook uses `Dispatcher.BeginInvoke()`
   - Update checks run on background thread

4. **Error handling in update system**
   - Try-catch blocks around download/install
   - User-friendly error messages
   - Handles antivirus interference

5. **Resource management**
   - `using` statements for Process/Module
   - Proper disposal of NotifyIcon

6. **User experience**
   - Global hotkeys work everywhere
   - Control window fades in on hover
   - System tray icon with context menu
   - Keyboard shortcuts displayed in UI

### ⚠️ Potential Issues to Watch

1. **Mouse hook performance**
   ```csharp
   // Fires for EVERY mouse movement on entire system
   private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
   ```
   - Could cause lag if `HandleMouseMove()` is slow
   - Consider throttling updates (e.g., max 60 FPS)
   - **Test:** Move mouse rapidly, check CPU usage

2. **Dispatcher queue buildup**
   ```csharp
   Dispatcher.BeginInvoke(new Action(() => 
   {
       HandleMouseMove(hookStruct.pt.x, hookStruct.pt.y);
   }), DispatcherPriority.Input);
   ```
   - If mouse moves fast, could queue hundreds of pending calls
   - Potential improvement: Check if previous call still pending
   - **Test:** Rapid mouse movement, check memory usage

3. **No error checking on hook installation**
   ```csharp
   mouseHookHandle = SetWindowsHookEx(...);
   // What if this returns IntPtr.Zero? (failure)
   ```
   - Should check if hook installation failed
   - Potential crash if `UnhookWindowsHookEx` called on Zero handle
   - **Fix:** Add null check after installation

4. **Geometry recreation on every mouse move**
   ```csharp
   var hole = new EllipseGeometry(...);
   EdgeLightBorder.Data = new CombinedGeometry(..., baseFrameGeometry, hole);
   ```
   - Creates new geometry objects frequently
   - May cause GC pressure
   - **Test:** Monitor GC collections during use

5. **Hard-coded proximity distance**
   ```csharp
   double proximityDist = 100.0 * dpiScale;
   ```
   - Could be a user setting
   - Different users may prefer different ranges

6. **Update system file access**
   ```csharp
   if (!System.IO.File.Exists(downloadedAsset.FilePath))
   ```
   - Antivirus might quarantine downloaded .exe
   - Good that it checks, but could provide recovery options

7. **Single-instance checking**
   - App doesn't check if already running
   - User could accidentally launch multiple instances
   - **Potential fix:** Use Mutex for single-instance enforcement

### 🔍 Testing Recommendations

1. **Multi-monitor scenarios:**
   - Hot-plug/unplug monitors while running
   - Switch primary monitor in Windows settings
   - Different DPI scales on different monitors

2. **Performance testing:**
   - Run for extended periods (memory leaks?)
   - Rapid mouse movements near border
   - CPU/memory usage monitoring

3. **Update system:**
   - Simulate slow network
   - Test with antivirus enabled
   - Verify cleanup of temp files

4. **Edge cases:**
   - Only one monitor (switch button should disable)
   - Very small monitor resolution (< 1024×768?)
   - Very large monitor (8K displays?)

5. **Hotkey conflicts:**
   - What if another app uses Ctrl+Shift+L?
   - RegisterHotKey might fail (should handle this)

---

## Common Pitfalls & Gotchas

### Pitfall #1: IntPtr vs. int

```csharp
// WRONG
int hwnd = new WindowInteropHelper(this).Handle;  // Error!

// RIGHT
IntPtr hwnd = new WindowInteropHelper(this).Handle;  // Correct
```

**Why:** On 64-bit Windows, handles are 64-bit. `int` is only 32-bit. Use `IntPtr` for handles, it adapts to platform.

### Pitfall #2: Forgetting to Call CallNextHookEx

```csharp
// WRONG - Breaks other apps' hooks!
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    HandleMouseMove(...);
    return IntPtr.Zero;  // Oops! Didn't call CallNextHookEx
}

// RIGHT
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
    {
        HandleMouseMove(...);
    }
    return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);  // ✓
}
```

**Why:** Hooks form a chain. If you don't call next hook, you break the chain and other apps won't get mouse events!

### Pitfall #3: Updating UI from Non-UI Thread

```csharp
// WRONG - Cross-thread exception!
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    EdgeLightBorder.Opacity = 0.5;  // Crash! Hook thread ≠ UI thread
    return CallNextHookEx(...);
}

// RIGHT
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    Dispatcher.BeginInvoke(new Action(() => 
    {
        EdgeLightBorder.Opacity = 0.5;  // Safe! Now on UI thread
    }));
    return CallNextHookEx(...);
}
```

**Why:** WPF UI elements have thread affinity. Only the creating thread can modify them.

### Pitfall #4: Garbage Collection of Delegates

```csharp
// WRONG - Callback gets garbage collected!
private void InstallMouseHook()
{
    LowLevelMouseProc callback = MouseHookProc;  // Local variable!
    mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, callback, ...);
    // callback goes out of scope → GC collects it → crash when Windows calls it
}

// RIGHT
private LowLevelMouseProc? mouseHookCallback;  // Field! Won't be GC'd

private void InstallMouseHook()
{
    mouseHookCallback = MouseHookProc;  // Store in field
    mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseHookCallback, ...);
}
```

**Why:** Windows stores the callback pointer. If C# GC deletes the delegate, Windows calls invalid memory → crash.

### Pitfall #5: Not Handling DPI Changes

```csharp
// This code handles it correctly by recalculating on SizeChanged
private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
{
    CreateFrameGeometry();  // Recalculate for new size
}
```

**Without this:** If user drags window to monitor with different DPI, border would be wrong size.

### Pitfall #6: Bitwise Operations on Booleans

```csharp
// WRONG - Using logical OR on flags
int newStyle = extendedStyle || WS_EX_TRANSPARENT;  // Logical OR! Wrong!

// RIGHT - Using bitwise OR
int newStyle = extendedStyle | WS_EX_TRANSPARENT;  // Bitwise OR! Correct!
```

**Why:** Flags are bits. You need bitwise OR (`|`), not logical OR (`||`).

---

## Learning Resources

### Official Microsoft Documentation

1. **P/Invoke (Platform Invocation Services)**
   - https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke

2. **Windows Hooks**
   - https://learn.microsoft.com/en-us/windows/win32/winmsg/hooks

3. **Window Styles**
   - https://learn.microsoft.com/en-us/windows/win32/winmsg/window-styles

4. **WPF Graphics**
   - https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/

### Books

1. **"Windows Presentation Foundation Unleashed" by Adam Nathan**
   - Deep dive into WPF concepts
   - Geometry, visual trees, rendering

2. **"Programming Windows" by Charles Petzold**
   - Classic Win32 API reference
   - Understanding Windows internals

### Video Courses

1. **Pluralsight: "WPF MVVM in Depth"**
   - Pattern not used here, but good for larger apps

2. **YouTube: "Nick Chapsas - C# Performance Tips"**
   - Relevant for optimizing the mouse hook

### Key Concepts to Study

1. **Dispatcher and Threading in WPF**
   - `Dispatcher.Invoke` vs `BeginInvoke`
   - `DispatcherPriority` levels
   - Understanding the UI thread

2. **Geometries and Paths**
   - `RectangleGeometry`, `EllipseGeometry`
   - `CombinedGeometry` operations
   - `PathGeometry` for complex shapes

3. **Marshal Class**
   - `PtrToStructure` and `StructureToPtr`
   - Memory layouts and alignment
   - Pinning and GC interaction

4. **Windows Message Loop**
   - Understanding WM_* messages
   - `WndProc` and message filtering
   - `HwndSource` in WPF

### Debugging Tools

1. **Spy++** (included with Visual Studio)
   - View window hierarchy
   - Monitor Windows messages
   - Inspect window styles

2. **Process Explorer** (Sysinternals)
   - View threads
   - Check handle leaks
   - Monitor performance

3. **WPF Performance Suite** (Visual Studio)
   - Profile rendering performance
   - Identify bottlenecks

---

## Testing & Debugging Tips

### Debugging Win32 Interop

1. **Check return values:**
   ```csharp
   IntPtr hookHandle = SetWindowsHookEx(...);
   if (hookHandle == IntPtr.Zero)
   {
       int error = Marshal.GetLastWin32Error();
       Debug.WriteLine($"Hook failed: {error}");
   }
   ```

2. **Use SetLastError = true:**
   ```csharp
   [DllImport("user32.dll", SetLastError = true)]
   private static extern IntPtr SetWindowsHookEx(...);
   ```

3. **Spy++ to verify window styles:**
   - Launch Spy++ (Tools → Spy++)
   - Find your window (Ctrl+F)
   - Check "Styles" tab
   - Verify `WS_EX_TRANSPARENT` and `WS_EX_LAYERED` are set

### Testing Mouse Hook

```csharp
// Add diagnostic logging
private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
{
    Debug.WriteLine($"Hook called: nCode={nCode}, wParam={wParam}");
    // ... rest of code
}
```

**Check for:**
- Is hook being called at all?
- Excessive call frequency (should be ~60-120 times per second max)
- Exceptions in callback (won't be visible normally!)

### Testing Hotkeys

```csharp
// Add diagnostic logging
RegisterHotKey(hwnd, HOTKEY_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_L);
Debug.WriteLine($"Registered hotkey: {hwnd}, success={result}");
```

**Test scenarios:**
- Register hotkey, then try it in Notepad (should work)
- Launch second instance (should fail to register same hotkey)
- Close app, verify hotkey unregisters (try it, should not work)

### Memory Leak Detection

```csharp
// In Window_Loaded:
Debug.WriteLine($"Initial memory: {GC.GetTotalMemory(false)} bytes");

// In Window_Closed:
Debug.WriteLine($"Final memory: {GC.GetTotalMemory(false)} bytes");
```

**Run test:**
1. Launch app
2. Move mouse around border for 5 minutes
3. Close app
4. Check memory delta (should be small)

### Performance Profiling

```csharp
// Measure HandleMouseMove performance
private readonly Stopwatch _perfTimer = new Stopwatch();
private int _perfCallCount = 0;

private void HandleMouseMove(int screenX, int screenY)
{
    _perfTimer.Start();
    
    // ... existing code ...
    
    _perfTimer.Stop();
    _perfCallCount++;
    
    if (_perfCallCount % 100 == 0)
    {
        var avgMs = _perfTimer.ElapsedMilliseconds / (double)_perfCallCount;
        Debug.WriteLine($"Avg HandleMouseMove: {avgMs:F2}ms");
    }
}
```

**Goal:** Average should be < 1ms. If higher, optimize.

### DPI Testing

**Test on:**
- 100% DPI (1920×1080 monitor)
- 150% DPI (1920×1080 with scaling)
- 200% DPI (4K monitor with scaling)
- Multiple monitors with different DPI

**Check:**
- Border size correct?
- Control window positioned correctly?
- No clipping or overlap?

### Multi-Monitor Testing

**Scenarios:**
1. Start with 2 monitors
2. Unplug secondary while app running
3. Verify app doesn't crash
4. Plug monitor back in
5. Verify "Switch Monitor" button enables

**Expected behavior:**
- App stays on primary monitor
- No exceptions when monitor count changes
- Button enables/disables correctly

---

## Architecture Decision Records (ADRs)

These explain **why** certain choices were made:

### ADR 1: Why Two Separate Windows?

**Decision:** Use `MainWindow` for overlay and separate `ControlWindow` for buttons.

**Rationale:**
- `MainWindow` must be click-through (transparent to mouse)
- Can't have clickable buttons in click-through window
- Solution: Second window with normal hit-testing

**Alternatives considered:**
- Single window with non-transparent region → complex hit-testing
- Menu bar → disrupts fullscreen apps
- System tray only → less discoverable

### ADR 2: Why Global Mouse Hook Instead of MouseMove Event?

**Decision:** Use `SetWindowsHookEx(WH_MOUSE_LL, ...)` instead of WPF's `MouseMove`.

**Rationale:**
- MainWindow is click-through, so `MouseMove` never fires
- Need to track cursor even when it's over other apps
- Hook provides system-wide tracking

**Alternatives considered:**
- Timer + GetCursorPos() → less responsive, polling overhead
- Touch/Pen events → doesn't cover mouse
- Windows 10 Input APIs → more complex

### ADR 3: Why Updatum Library for Updates?

**Decision:** Use Updatum NuGet package instead of custom solution.

**Rationale:**
- Handles GitHub API authentication
- ZIP extraction and file replacement
- Progress reporting built-in
- Tested by community

**Alternatives considered:**
- ClickOnce deployment → not suitable for single-file EXE
- Custom HTTP download → reinventing the wheel
- Windows Store → requires packaging, certification

### ADR 4: Why WPF Instead of WinForms or UWP?

**Decision:** Use WPF (Windows Presentation Foundation).

**Rationale:**
- Modern vector graphics (Path, Geometry)
- Built-in transparency and effects
- XAML for declarative UI
- Good Win32 interop support

**Alternatives considered:**
- WinForms → dated, no vector graphics
- UWP → can't use Win32 hooks easily
- Electron → too heavy for simple utility

### ADR 5: Why Single-File Executable?

**Decision:** Publish as single-file EXE with embedded runtime.

**Rationale:**
- Easy distribution (one file to download)
- No .NET installation required
- Double-click to run

**Trade-offs:**
- Larger file size (~72 MB vs ~200 KB)
- Slower first launch (extraction)
- Can't use trimming (WPF/WinForms not trim-compatible)

---

## Glossary of Terms

**HWND (Handle to Window)**
- Unique identifier for a window
- Type: `IntPtr` in C#
- Used in Win32 API calls

**DPI (Dots Per Inch)**
- Screen pixel density
- Higher DPI = sharper display
- Windows uses 96 DPI as baseline

**DIP (Device Independent Pixel)**
- WPF's logical unit
- Always 1/96th of an inch
- Scales with DPI automatically

**P/Invoke (Platform Invocation Services)**
- C# mechanism to call native (C) functions
- Uses `[DllImport]` attribute

**Marshaling**
- Converting data between managed (C#) and unmanaged (C) formats
- `Marshal.PtrToStructure`, `Marshal.StringToHGlobalUni`, etc.

**Hook (Windows Hook)**
- Mechanism to intercept system events
- Types: keyboard, mouse, message, etc.
- Can be thread-local or system-wide

**Dispatcher**
- WPF's UI thread manager
- Schedules work on the UI thread
- `Invoke` (synchronous) vs `BeginInvoke` (asynchronous)

**Geometry**
- WPF's vector shape representation
- `RectangleGeometry`, `EllipseGeometry`, `PathGeometry`
- Combined with `CombinedGeometry`

**Extended Window Style**
- Additional flags beyond basic window styles
- Accessed via `GetWindowLong(hwnd, GWL_EXSTYLE)`
- Examples: `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_TOPMOST`

**Virtual Key Code**
- Numeric identifier for keyboard keys
- Example: `VK_L = 0x4C`
- Platform-independent key representation

**Modifier Keys**
- Special keys held during input
- `MOD_CONTROL`, `MOD_SHIFT`, `MOD_ALT`
- Combined with bitwise OR

---

## Summary for Your Boss

> **Executive Summary:**
> 
> Windows Edge Light is a WPF desktop application that provides a customizable glowing border overlay for video conferencing. The architecture consists of two primary windows: a transparent click-through overlay (MainWindow) for the visual effect, and a floating control panel (ControlWindow) for user interaction.
> 
> **Key Technical Highlights:**
> - Utilizes Win32 interop for advanced features not available in managed .NET (click-through windows, global hotkeys, system-wide mouse tracking)
> - Implements proper DPI scaling for high-resolution displays
> - Includes automatic update system via GitHub Releases
> - Performance-optimized geometry operations for smooth cursor proximity effects
> - Follows Windows UI conventions with system tray integration and keyboard shortcuts
> 
> **Code Quality:**
> - Proper resource cleanup and memory management
> - Thread-safe UI updates from system hooks
> - Defensive error handling in update system
> - Clear separation of concerns between UI and business logic
> 
> **Areas for Future Improvement:**
> - Consider throttling mouse hook events for performance
> - Add single-instance enforcement (Mutex)
> - Implement error checking on Win32 API return values
> - Potential for user-configurable proximity distance

---

## Final Notes

**Remember:**
- Win32 interop is powerful but dangerous - always check return values
- Hooks MUST call `CallNextHookEx` to avoid breaking other apps
- Always clean up: unhook, unregister, dispose
- Test on multiple monitors with different DPI settings
- When in doubt, consult Microsoft documentation

**Good luck with your code review!** 🚀

If you have questions, check:
1. This document
2. Inline code comments
3. Microsoft Docs (learn.microsoft.com)
4. Stack Overflow (for specific issues)

---

**Document Version:** 1.0  
**Last Updated:** November 2025  
**Maintained By:** Junior Engineering Team
