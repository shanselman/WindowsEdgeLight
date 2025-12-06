# AI Face Tracking Dynamic Edge Lighting - Implementation Plan

## Executive Summary

This feature will add intelligent, dynamic edge lighting that responds to the user's face position on screen using NPU/GPU-accelerated machine learning. The lighting will adapt in real-time to highlight the area where the user is looking, creating a natural "spotlight" effect during video calls, streaming, or general computer use.

## Technology Stack

### Windows ML & DirectML
- **Windows.AI.MachineLearning** - Built-in Windows 11 API for ONNX model inference
- **DirectML** - Hardware acceleration layer supporting:
  - NPU (Neural Processing Units) on Copilot+ PCs (Snapdragon X Elite, Intel Core Ultra)
  - GPU acceleration (AMD, Intel, NVIDIA via DirectX 12)
  - CPU fallback for older machines

### Face Detection Models
- **ONNX Format**: Industry-standard model format
- **Recommended Models**:
  1. **FaceONNX** - Open-source face detection library with pre-built models
  2. **YOLO-Face** - Fast, lightweight face detection
  3. **MediaPipe Face Detection** - Google's efficient model
  4. **Ultra-Light-Fast-Generic-Face-Detector** - Optimized for real-time performance

### Camera Access
- **Windows.Media.Capture** - Modern UWP API for camera access
- **MediaCapture API** - Frame-by-frame access for ML inference
- Fallback to DirectShow for legacy support

## Feature Overview

### Core Functionality
1. **Real-time Face Detection**: Detect user's face position using webcam
2. **Dynamic Lighting**: Brighten edge lighting sections closest to face position
3. **Smooth Transitions**: Animate lighting changes for natural feel
4. **Multi-Face Support**: Prioritize primary face (largest/closest)
5. **Privacy First**: All processing happens locally on device

### User Experience
- **Opt-in Feature**: Disabled by default, requires user permission
- **Hardware Detection**: Only visible on NPU/GPU capable devices
- **Graceful Degradation**: Falls back to standard mode if:
  - No camera available
  - No face detected
  - Performance issues detected
  - User denies camera permission

## Implementation Architecture

### Phase 1: Foundation (Week 1-2)
**Goal**: Set up ML infrastructure and hardware detection

#### 1.1 NuGet Packages
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime.DirectML" Version="1.20.0" />
<PackageReference Include="Microsoft.Windows.SDK.Contracts" Version="10.0.22621.3233" />
```

#### 1.2 Hardware Capability Detection
```csharp
// New class: HardwareCapabilityDetector.cs
public class HardwareCapabilityDetector
{
    public bool HasNPU { get; }
    public bool HasGPU { get; }
    public bool HasDirectML { get; }
    public string[] AvailableDevices { get; }
    
    // Detect NPU via WMI/Registry
    // Check DirectML device enumeration
    // GPU capability check
}
```

#### 1.3 Feature Flag System
```csharp
// New class: FeatureFlags.cs
public static class FeatureFlags
{
    public static bool IsAIFaceTrackingAvailable { get; }
    public static bool IsAIFaceTrackingEnabled { get; set; }
    public static AIDeviceType PreferredDevice { get; set; }
}
```

### Phase 2: ONNX Model Integration (Week 2-3)
**Goal**: Load and run face detection models

#### 2.1 Model Selection & Download
- Bundle lightweight model (~5MB) with application
- Download larger, more accurate models on-demand
- Model storage: `%LocalAppData%\WindowsEdgeLight\Models\`

#### 2.2 Model Wrapper
```csharp
// New class: FaceDetectionModel.cs
public class FaceDetectionModel : IDisposable
{
    private InferenceSession _session;
    
    public Task<FaceDetectionResult[]> DetectFacesAsync(byte[] imageData);
    public void LoadModel(string modelPath, ExecutionProvider provider);
}

public record FaceDetectionResult(
    Rectangle BoundingBox,
    float Confidence,
    Point[] Landmarks // Optional: eyes, nose, mouth
);
```

#### 2.3 Execution Providers
```csharp
public enum ExecutionProvider
{
    NPU,      // NPU via DirectML
    GPU,      // GPU via DirectML
    CPU,      // CPU fallback
    Auto      // Let DirectML choose
}
```

### Phase 3: Camera Integration (Week 3-4)
**Goal**: Access webcam and process frames

#### 3.1 Camera Manager
```csharp
// New class: CameraManager.cs
public class CameraManager : IDisposable
{
    public event EventHandler<CameraFrameEventArgs> FrameReceived;
    
    public async Task<bool> RequestCameraPermissionAsync();
    public async Task StartCameraAsync();
    public void StopCamera();
    public Task<byte[]> CaptureFrameAsync();
}
```

#### 3.2 Frame Processing Pipeline
```csharp
// New class: FaceTrackingService.cs
public class FaceTrackingService
{
    private Timer _processingTimer;
    private const int FrameProcessingInterval = 100; // 10 FPS
    
    public event EventHandler<FacePositionEventArgs> FacePositionUpdated;
    
    public async Task StartTrackingAsync();
    public void StopTracking();
    
    private async Task ProcessFrameAsync()
    {
        // 1. Capture frame from camera
        // 2. Resize/preprocess for model
        // 3. Run inference
        // 4. Post-process results
        // 5. Emit face position event
    }
}
```

### Phase 4: Dynamic Lighting Logic (Week 4-5)
**Goal**: Modify edge lighting based on face position

#### 4.1 Screen Position Mapping
```csharp
// New class: FaceToLightingMapper.cs
public class FaceToLightingMapper
{
    // Map face bounding box to screen quadrant/edge
    public EdgeSection GetPrimaryEdgeSection(FaceDetectionResult face, Size screenSize);
    
    // Calculate lighting intensity based on proximity
    public double CalculateIntensity(Point faceCenter, EdgeSection section);
}

public enum EdgeSection
{
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    TopLeft
}
```

#### 4.2 Adaptive Lighting Controller
```csharp
// Extend MainWindow.xaml.cs
public class AdaptiveLightingController
{
    private FaceTrackingService _faceTracker;
    private readonly Dictionary<EdgeSection, double> _sectionIntensities = new();
    
    public void OnFacePositionChanged(FacePositionEventArgs e)
    {
        // Update section intensities
        // Trigger smooth animation
        // Update gradient stops
    }
    
    private void UpdateEdgeLightingGradient()
    {
        // Modify LinearGradientBrush based on face position
        // Increase opacity/brightness on relevant edge sections
    }
}
```

#### 4.3 Animation System
```csharp
// Smooth transitions between lighting states
public class LightingAnimator
{
    private const double TransitionDuration = 300; // ms
    
    public void AnimateSectionIntensity(
        EdgeSection section, 
        double fromIntensity, 
        double toIntensity);
}
```

### Phase 5: UI Integration (Week 5-6)
**Goal**: Add user controls and settings

#### 5.1 Control Window Updates
Add to ControlWindow.xaml:
```xml
<!-- AI Features Section -->
<StackPanel x:Name="AIFeaturesPanel" Visibility="Collapsed">
    <TextBlock Text="🤖 AI Features" FontWeight="Bold" Margin="0,10,0,5"/>
    
    <CheckBox x:Name="EnableFaceTrackingCheckBox" 
              Content="Face-Responsive Lighting"
              Checked="EnableFaceTracking_Checked"
              Unchecked="EnableFaceTracking_Unchecked"/>
    
    <TextBlock x:Name="AIStatusText" 
               FontSize="10" 
               Foreground="Gray"
               Text="NPU Accelerated"/>
    
    <Slider x:Name="AIIntensitySlider"
            Minimum="0" Maximum="1" Value="0.7"
            ToolTip="AI Lighting Intensity"
            ValueChanged="AIIntensity_Changed"/>
</StackPanel>
```

#### 5.2 Settings Persistence
```csharp
// New class: AppSettings.cs
public class AppSettings
{
    public bool AIFaceTrackingEnabled { get; set; }
    public double AIIntensityMultiplier { get; set; } = 0.7;
    public ExecutionProvider PreferredProvider { get; set; }
    
    public void Save();
    public static AppSettings Load();
}
```

#### 5.3 Permission Dialog
```csharp
// New window: CameraPermissionDialog.xaml
// Request camera access with explanation
// Show privacy policy
// Option to decline and use standard mode
```

### Phase 6: Performance Optimization (Week 6-7)
**Goal**: Ensure smooth performance across devices

#### 6.1 Performance Monitoring
```csharp
public class PerformanceMonitor
{
    public double AverageInferenceTime { get; }
    public double FrameRate { get; }
    public int DroppedFrames { get; }
    
    // Auto-adjust processing rate based on performance
    public void AutoTunePerformance();
}
```

#### 6.2 Adaptive Quality
- Start at 10 FPS (100ms interval)
- Reduce to 5 FPS if inference > 80ms
- Increase to 15 FPS if inference < 30ms consistently
- Pause processing if CPU/GPU load > 90%

#### 6.3 Memory Management
- Reuse byte buffers for frames
- Dispose model properly
- Release camera when app minimized
- Clear model cache on exit

### Phase 7: Testing & Refinement (Week 7-8)
**Goal**: Ensure quality and stability

#### 7.1 Test Scenarios
- ✅ Copilot+ PC with NPU (Snapdragon X Elite)
- ✅ Standard PC with modern GPU (RTX/AMD)
- ✅ Integrated GPU (Intel UHD)
- ✅ No camera available
- ✅ Multiple monitors
- ✅ DPI scaling (150%, 200%)
- ✅ Dark room / bright room
- ✅ Multiple faces in frame

#### 7.2 Fallback Testing
- Camera permission denied
- Model load failure
- Inference timeout
- NPU/GPU unavailable
- Low performance degradation

## Code Organization

### New Files
```
WindowsEdgeLight/
├── AI/
│   ├── HardwareCapabilityDetector.cs
│   ├── FaceDetectionModel.cs
│   ├── FaceTrackingService.cs
│   ├── CameraManager.cs
│   ├── FaceToLightingMapper.cs
│   ├── AdaptiveLightingController.cs
│   ├── LightingAnimator.cs
│   └── PerformanceMonitor.cs
├── Settings/
│   ├── AppSettings.cs
│   └── FeatureFlags.cs
├── Dialogs/
│   ├── CameraPermissionDialog.xaml
│   └── CameraPermissionDialog.xaml.cs
└── Models/
    └── face-detection-light.onnx (bundled model)
```

### Modified Files
- `MainWindow.xaml.cs` - Integrate AdaptiveLightingController
- `ControlWindow.xaml` - Add AI controls
- `ControlWindow.xaml.cs` - Handle AI feature toggles
- `WindowsEdgeLight.csproj` - Add NuGet packages
- `App.xaml.cs` - Initialize FeatureFlags on startup

## Privacy & Security Considerations

### Privacy-First Design
1. **Local Processing Only**: All ML inference runs on device
2. **No Cloud Connection**: No face data sent to internet
3. **No Storage**: Frames discarded immediately after processing
4. **Camera Indicator**: Windows shows camera-in-use indicator
5. **User Control**: Easy on/off toggle, respects permissions

### Security
- Camera access only when feature enabled
- Dispose camera resources when app minimized/closed
- Model integrity check (verify hash)
- Sandboxed execution (no file system access from model)

## Performance Targets

### Inference Performance
- **NPU Target**: < 20ms per frame (50+ FPS capability)
- **GPU Target**: < 30ms per frame (30+ FPS capability)
- **CPU Fallback**: < 100ms per frame (10 FPS minimum)

### Resource Usage
- **Memory**: < 150MB additional (with model loaded)
- **CPU**: < 5% on NPU, < 10% on GPU, < 15% on CPU
- **Battery Impact**: < 2% per hour (NPU is highly efficient)

### User Experience
- **Startup Time**: < 2 seconds to enable feature
- **Face Detection Latency**: < 200ms from face move to light change
- **Smooth Animations**: 60 FPS lighting transitions

## Backward Compatibility

### Conditional Compilation
All AI features wrapped in capability checks:
```csharp
if (FeatureFlags.IsAIFaceTrackingAvailable)
{
    // Show AI controls
    // Enable AI features
}
else
{
    // Hide AI controls
    // Standard mode only
}
```

### Graceful Degradation Path
1. **Modern Hardware (NPU/GPU)**: Full AI features, hardware accelerated
2. **Older GPU**: AI features with CPU acceleration (reduced FPS)
3. **No DirectML**: AI features hidden, standard mode only
4. **User Choice**: Can disable AI even on capable hardware

## Future Enhancements (Post-V1)

### V2 Features
- **Eye Gaze Tracking**: More precise lighting based on eye direction
- **Emotion Detection**: Color temperature based on detected emotion
- **Voice Activity**: Pulse lighting when speaking (for streamers)
- **Multi-Face Modes**: Different lighting patterns for multiple people

### V3 Features
- **Body Posture**: Adjust lighting based on sitting/standing
- **Hand Gestures**: Control lighting with hand movements
- **Background Blur Integration**: Coordinate with Windows background effects
- **Copilot Integration**: Natural language lighting control

## Success Metrics

### Adoption
- 40%+ of users on capable hardware enable feature
- 4.5+ star feature rating
- < 1% disable due to performance issues

### Performance
- 95%+ achieve 10+ FPS inference
- 80%+ achieve 20+ FPS on NPU
- < 0.1% crash rate
- < 5% fallback to disabled state

### User Experience
- "Natural" and "responsive" mentioned in feedback
- Used during > 50% of video calls
- Recommended by streamers/content creators

## Development Timeline

- **Week 1-2**: Hardware detection, DirectML setup
- **Week 2-3**: Model integration, inference testing
- **Week 3-4**: Camera access, frame processing
- **Week 4-5**: Dynamic lighting logic
- **Week 5-6**: UI, settings, polish
- **Week 6-7**: Performance optimization
- **Week 7-8**: Testing, bug fixes, documentation

**Total**: ~8 weeks for V1 release

## Resources & References

### Documentation
- [Windows ML Documentation](https://learn.microsoft.com/en-us/windows/ai/windows-ml/)
- [DirectML NPU Support](https://blogs.windows.com/windowsdeveloper/2024/08/29/directml-expands-npu-support-to-copilot-pcs-and-webnn/)
- [Copilot+ PC Developer Guide](https://learn.microsoft.com/en-us/windows/ai/npu-devices/)
- [ONNX Runtime DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)

### Model Repositories
- [FaceONNX GitHub](https://github.com/FaceONNX/FaceONNX)
- [ONNX Model Zoo](https://github.com/onnx/models)
- [Hugging Face ONNX Models](https://huggingface.co/onnxmodelzoo)

### Sample Code
- [Windows ML Samples](https://github.com/microsoft/Windows-Machine-Learning)
- [DirectML Examples](https://github.com/microsoft/DirectML)
- [MediaCapture Samples](https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/CameraStarterKit)

## Conclusion

This feature represents a significant upgrade to Windows Edge Light, positioning it as a cutting-edge tool for Copilot+ PC users. By leveraging local NPU/GPU acceleration, we deliver a premium, privacy-respecting AI experience that enhances the core product without compromising on performance or user trust.

The implementation is designed for:
- **Modern Hardware**: Showcases NPU capabilities on latest Windows 11 devices
- **Backward Compatibility**: Works on older hardware with graceful degradation
- **User Control**: Easy to enable/disable, respects privacy
- **Professional Quality**: Smooth, responsive, battery-efficient

This positions Windows Edge Light as a flagship example of Windows 11 AI PC capabilities for consumer desktop applications.
