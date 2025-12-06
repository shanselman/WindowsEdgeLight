using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;

namespace WindowsEdgeLight.AI;

/// <summary>
/// Event args for face tracking updates.
/// </summary>
public class FaceTrackingEventArgs : EventArgs
{
    public float NormalizedX { get; }
    public float NormalizedY { get; }
    public bool FaceDetected { get; }
    public double ProcessingTimeMs { get; }

    public FaceTrackingEventArgs(float x, float y, bool detected, double processingMs)
    {
        NormalizedX = x;
        NormalizedY = y;
        FaceDetected = detected;
        ProcessingTimeMs = processingMs;
    }
    
    public static FaceTrackingEventArgs NoFace(double processingMs) 
        => new(0.5f, 0.5f, false, processingMs);
}

/// <summary>
/// Lightweight face detection using Windows built-in FaceDetector API.
/// Uses MediaCapture with SharedReadOnly mode to work alongside Teams/Zoom.
/// Processes one frame per second - sufficient for lighting adjustments.
/// </summary>
public class NativeFaceTracker : IDisposable
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private FaceDetector? _faceDetector;
    private bool _isTracking;
    private bool _disposed;
    private bool _isProcessingFrame;
    private readonly object _lock = new();
    
    private DateTime _lastProcessTime = DateTime.MinValue;
    private readonly TimeSpan _processInterval = TimeSpan.FromSeconds(1); // Once per second

    public event EventHandler<FaceTrackingEventArgs>? FacePositionChanged;
    public bool IsTracking => _isTracking;

    /// <summary>
    /// Represents an available camera for face tracking.
    /// </summary>
    public record CameraInfo(string Id, string DisplayName, MediaFrameSourceGroup SourceGroup, MediaFrameSourceInfo SourceInfo);

    /// <summary>
    /// Gets a list of available cameras that support video capture.
    /// </summary>
    public static async Task<List<CameraInfo>> GetAvailableCamerasAsync()
    {
        var cameras = new List<CameraInfo>();
        var frameSourceGroups = await MediaFrameSourceGroup.FindAllAsync();

        foreach (var group in frameSourceGroups)
        {
            foreach (var sourceInfo in group.SourceInfos)
            {
                if (sourceInfo.MediaStreamType == MediaStreamType.VideoPreview ||
                    sourceInfo.MediaStreamType == MediaStreamType.VideoRecord)
                {
                    cameras.Add(new CameraInfo(group.Id, group.DisplayName, group, sourceInfo));
                    break;
                }
            }
        }

        return cameras;
    }

    /// <summary>
    /// Starts face detection using the specified camera in shared mode.
    /// </summary>
    public async Task<bool> StartAsync(CameraInfo? camera = null, int frameRate = 5)
    {
        if (_isTracking)
            return true;

        try
        {
            Debug.WriteLine("NativeFaceTracker: Starting with FaceDetector...");
            
            // Create FaceDetector (for single images, not video)
            if (!FaceDetector.IsSupported)
            {
                Debug.WriteLine("NativeFaceTracker: FaceDetector not supported on this device");
                return false;
            }
            
            _faceDetector = await FaceDetector.CreateAsync();
            Debug.WriteLine("NativeFaceTracker: FaceDetector created");

            // Use provided camera or find first available
            MediaFrameSourceGroup? selectedGroup = camera?.SourceGroup;
            MediaFrameSourceInfo? selectedSourceInfo = camera?.SourceInfo;

            if (selectedGroup == null || selectedSourceInfo == null)
            {
                var cameras = await GetAvailableCamerasAsync();
                if (cameras.Count == 0)
                {
                    Debug.WriteLine("NativeFaceTracker: No video source found");
                    return false;
                }
                selectedGroup = cameras[0].SourceGroup;
                selectedSourceInfo = cameras[0].SourceInfo;
            }
            
            Debug.WriteLine($"NativeFaceTracker: Using camera '{selectedGroup.DisplayName}'");

            // Initialize MediaCapture with SharedReadOnly for multi-app support
            _mediaCapture = new MediaCapture();
            
            var settings = new MediaCaptureInitializationSettings
            {
                SourceGroup = selectedGroup,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            await _mediaCapture.InitializeAsync(settings);
            Debug.WriteLine("NativeFaceTracker: MediaCapture initialized in SharedReadOnly mode");

            // Get the frame source
            var frameSource = _mediaCapture.FrameSources[selectedSourceInfo.Id];

            // Create frame reader
            _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource);
            _frameReader.FrameArrived += FrameReader_FrameArrived;
            
            var status = await _frameReader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                Debug.WriteLine($"NativeFaceTracker: Failed to start frame reader: {status}");
                return false;
            }
            
            _isTracking = true;
            Debug.WriteLine("NativeFaceTracker: Started (processing once per second)");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"NativeFaceTracker: Camera access denied - {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeFaceTracker: Failed to start - {ex.Message}");
            Cleanup();
            return false;
        }
    }

    public void Stop()
    {
        if (!_isTracking)
            return;

        Debug.WriteLine("NativeFaceTracker: Stopping...");
        _isTracking = false;
        Cleanup();
        Debug.WriteLine("NativeFaceTracker: Stopped");
    }

    private async void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Only process once per second
        var now = DateTime.UtcNow;
        if (now - _lastProcessTime < _processInterval)
            return;

        if (!_isTracking || _disposed || _isProcessingFrame)
            return;

        lock (_lock)
        {
            if (_isProcessingFrame)
                return;
            _isProcessingFrame = true;
        }

        _lastProcessTime = now;
        var sw = Stopwatch.StartNew();
        
        try
        {
            using var frameRef = sender.TryAcquireLatestFrame();
            if (frameRef?.VideoMediaFrame?.SoftwareBitmap == null)
            {
                _isProcessingFrame = false;
                return;
            }

            var bitmap = frameRef.VideoMediaFrame.SoftwareBitmap;
            var actualWidth = (uint)bitmap.PixelWidth;
            var actualHeight = (uint)bitmap.PixelHeight;
            
            // FaceDetector needs Gray8 or Nv12
            SoftwareBitmap? convertedBitmap = null;
            
            if (!FaceDetector.IsBitmapPixelFormatSupported(bitmap.BitmapPixelFormat))
            {
                // Convert to Gray8
                convertedBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Gray8);
                bitmap = convertedBitmap;
            }

            try
            {
                // Use FaceDetector (single image) instead of FaceTracker (video)
                var faces = await _faceDetector!.DetectFacesAsync(bitmap);
                
                sw.Stop();
                Debug.WriteLine($"NativeFaceTracker: Processed frame in {sw.ElapsedMilliseconds}ms, found {faces.Count} faces");

                if (faces.Count > 0)
                {
                    var face = faces[0];
                    var box = face.FaceBox;
                    
                    float centerX = (box.X + box.Width / 2f) / actualWidth;
                    float centerY = (box.Y + box.Height / 2f) / actualHeight;
                    
                    Debug.WriteLine($"Face at ({centerX:F2}, {centerY:F2})");
                    RaiseEvent(new FaceTrackingEventArgs(centerX, centerY, true, sw.Elapsed.TotalMilliseconds));
                }
                else
                {
                    RaiseEvent(FaceTrackingEventArgs.NoFace(sw.Elapsed.TotalMilliseconds));
                }
            }
            finally
            {
                convertedBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeFaceTracker: Frame processing error - {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _isProcessingFrame = false;
        }
    }

    private void RaiseEvent(FaceTrackingEventArgs args)
    {
        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                FacePositionChanged?.Invoke(this, args);
            });
        }
        else
        {
            FacePositionChanged?.Invoke(this, args);
        }
    }

    private void Cleanup()
    {
        if (_frameReader != null)
        {
            _frameReader.FrameArrived -= FrameReader_FrameArrived;
            _frameReader.Dispose();
            _frameReader = null;
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;

        _faceDetector = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Stop();
        }
        GC.SuppressFinalize(this);
    }
}
