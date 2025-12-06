# Face Tracking Implementation Notes

## Overview

WindowsEdgeLight uses Windows native face detection to track the user's face position and adjust edge lighting accordingly. This document captures the implementation journey, challenges encountered, and solutions found.

## Final Working Solution

**API Used:** `Windows.Media.FaceAnalysis.FaceDetector`

The working implementation uses:
- `MediaCapture` with `SharedReadOnly` mode for camera access (works alongside Teams/Zoom)
- `MediaFrameReader` for receiving camera frames
- `FaceDetector.DetectFacesAsync()` for face detection (single image API)
- Processing rate: **once per second** (sufficient for lighting adjustments)
- Processing time: **~20-40ms per frame**

### Key Code Path
```csharp
// Create detector
_faceDetector = await FaceDetector.CreateAsync();

// Get frame from camera
var bitmap = frameRef.VideoMediaFrame.SoftwareBitmap;

// Convert to Gray8 if needed
if (!FaceDetector.IsBitmapPixelFormatSupported(bitmap.BitmapPixelFormat))
{
    convertedBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Gray8);
}

// Detect faces
var faces = await _faceDetector.DetectFacesAsync(bitmap);
```

## What Didn't Work

### 1. FaceTracker API (Video-based)

**API:** `Windows.Media.FaceAnalysis.FaceTracker.ProcessNextFrameAsync()`

**Problem:** Consistently returned `COMException` with `HResult=0x80004005` (E_FAIL)

**What we tried:**
- Different pixel formats: Gray8, Nv12, Bgra8 (all reported as supported)
- Different resolutions: 640x480, 1920x1080
- Different alpha modes: `BitmapAlphaMode.Ignore`, `BitmapAlphaMode.Premultiplied`
- Creating `VideoFrame` with `VideoFrame.CreateWithSoftwareBitmap()`

**Logs showing the issue:**
```
NativeFaceTracker: Supported - Gray8=True, Nv12=True, Bgra8=True
NativeFaceTracker: Frame 640x480 converted to Gray8
NativeFaceTracker: Frame processing error - COMException HResult=0x80004005:
```

**Theory:** The `FaceTracker` API may have specific requirements for video stream initialization or may not work well with `SharedReadOnly` camera mode. The documentation is sparse on exact requirements.

### 2. Original ONNX + OpenCV Approach

**Problem:** Considerable lag when enabling face tracking

**Dependencies removed:**
- `OpenCvSharp4` (~large native binaries)
- `OpenCvSharp4.runtime.win`
- `Microsoft.ML.OnnxRuntime.DirectML`
- Bundled ONNX model file (`face-detection-ultra-light.onnx`)

**Why we moved away:** 
- Heavy dependencies (~50MB+)
- Complex MediaFoundation interop code for camera access
- Performance issues with model loading and inference

### 3. GetPreviewFrameAsync Approach

**Problem:** "The request is invalid in the current state" error

**Cause:** `MediaCapture.GetPreviewFrameAsync()` requires the preview stream to be running, but in `SharedReadOnly` mode we can't start our own preview - we can only read frames that another app is producing.

**Solution:** Use `MediaFrameReader` instead, which properly works with shared camera access.

## Camera Access: SharedReadOnly Mode

Critical for working alongside video calls:

```csharp
var settings = new MediaCaptureInitializationSettings
{
    SourceGroup = selectedGroup,
    SharingMode = MediaCaptureSharingMode.SharedReadOnly,  // Key!
    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
    StreamingCaptureMode = StreamingCaptureMode.Video
};
```

**Limitations of SharedReadOnly:**
- Cannot change camera format/resolution (must use what the primary app sets)
- Cannot start preview stream
- Must use `MediaFrameReader` for frame access

## Format Conversion

Cameras typically output `Yuy2` format. Face detection APIs need `Gray8` or `Nv12`:

```csharp
if (!FaceDetector.IsBitmapPixelFormatSupported(bitmap.BitmapPixelFormat))
{
    convertedBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Gray8);
}
```

Direct `Yuy2` → `Gray8` conversion works. If it doesn't, convert via `Bgra8` as intermediate.

## Lighting Effect Implementation

### Radial Gradient Approach
The face position controls a `RadialGradientBrush` center point:
- Face on left → gradient center shifts left → left edge brighter
- Face on right → gradient center shifts right → right edge brighter

### Animation
- Uses WPF `PointAnimation` for smooth transitions
- 800ms duration with `QuadraticEase` for natural movement
- Only animates when position change exceeds 5% threshold (prevents jitter)
- `FillBehavior.HoldEnd` to maintain final position

### State Management
- Saves original brush when face tracking starts
- Restores original brush when face tracking stops
- Tracks last position to animate FROM current TO new position

## Performance Notes

- **FaceDetector:** 20-40ms per detection at 1920x1080
- **Processing rate:** Once per second is plenty for lighting adjustments
- **CPU impact:** Minimal with 1Hz processing rate
- **Animation:** Smooth 800ms transitions between positions

## Files Added/Modified

### New Files:
- `WindowsEdgeLight/AI/NativeFaceTracker.cs` - Face detection using Windows APIs
- `WindowsEdgeLight/AI/FaceToLightingMapper.cs` - Maps face position to lighting parameters
- `WindowsEdgeLight/CameraSelectionDialog.xaml/.cs` - Camera picker UI
- `WindowsEdgeLight/FileLogger.cs` - Debug logging to file
- `docs/face-tracking-implementation.md` - This document

### Modified Files:
- `MainWindow.xaml.cs` - Face tracking integration, lighting effects
- `ControlWindow.xaml/.cs` - AI tracking toggle button
- `App.xaml.cs` - Logger initialization
- `WindowsEdgeLight.csproj` - Target framework for WinRT APIs

## Future Improvements to Consider

1. **Retry FaceTracker:** May work better in non-SharedReadOnly mode or with specific initialization
2. **Resolution scaling:** Scale down large frames before detection for speed
3. **Adjustable sensitivity:** Let user control how much face position affects lighting
4. **Multiple faces:** Average position of multiple detected faces
5. **Vertical tracking:** Currently focuses on horizontal; could enhance vertical response
6. **Processing rate slider:** Let user choose 0.5Hz to 5Hz based on preference
7. **Edge-specific control:** Instead of radial gradient, control each edge independently
8. **Smooth return to neutral:** When no face detected, gradually return to center

## Dependencies

Current (lightweight):
- Target framework: `net10.0-windows10.0.19041.0` (for WinRT APIs)
- No additional NuGet packages needed for face detection

Removed:
- OpenCvSharp4
- Microsoft.ML.OnnxRuntime.DirectML
- Bundled ONNX model

## References

- [FaceDetector Class](https://learn.microsoft.com/en-us/uwp/api/windows.media.faceanalysis.facedetector)
- [FaceTracker Class](https://learn.microsoft.com/en-us/uwp/api/windows.media.faceanalysis.facetracker)
- [MediaCapture SharedReadOnly](https://learn.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapturesharingmode)
- [Basic Face Tracking Sample](https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/BasicFaceTracking)
