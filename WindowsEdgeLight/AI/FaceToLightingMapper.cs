using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace WindowsEdgeLight.AI;

/// <summary>
/// Parameters for edge lighting based on face position.
/// </summary>
public record LightingParameters
{
    /// <summary>
    /// Overall brightness multiplier (0.0 to 1.0).
    /// </summary>
    public double Brightness { get; init; } = 1.0;
    
    /// <summary>
    /// Left edge brightness multiplier (0.0 to 1.0).
    /// </summary>
    public double LeftBrightness { get; init; } = 1.0;
    
    /// <summary>
    /// Right edge brightness multiplier (0.0 to 1.0).
    /// </summary>
    public double RightBrightness { get; init; } = 1.0;
    
    /// <summary>
    /// Top edge brightness multiplier (0.0 to 1.0).
    /// </summary>
    public double TopBrightness { get; init; } = 1.0;
    
    /// <summary>
    /// Bottom edge brightness multiplier (0.0 to 1.0).
    /// </summary>
    public double BottomBrightness { get; init; } = 1.0;
    
    /// <summary>
    /// Whether face tracking is currently active.
    /// </summary>
    public bool IsTracking { get; init; }
    
    /// <summary>
    /// Whether a face is currently detected.
    /// </summary>
    public bool FaceDetected { get; init; }

    /// <summary>
    /// Default neutral lighting (all edges equal).
    /// </summary>
    public static LightingParameters Default => new()
    {
        Brightness = 1.0,
        LeftBrightness = 1.0,
        RightBrightness = 1.0,
        TopBrightness = 1.0,
        BottomBrightness = 1.0,
        IsTracking = false,
        FaceDetected = false
    };
}

/// <summary>
/// Maps face position to edge lighting parameters.
/// Creates a "follow the user" effect where the light is brighter
/// on the side the user is looking from.
/// </summary>
public class FaceToLightingMapper
{
    private LightingParameters _currentParams = LightingParameters.Default;
    private LightingParameters _targetParams = LightingParameters.Default;
    
    // Smoothing factor - disabled since UI animation handles smoothing
    private const double SmoothingFactor = 0.0;  // Instant response, let animation smooth it
    
    // How much face position affects brightness (0 = none, 1 = full effect)
    private const double PositionInfluence = 1.0;  // Maximum effect
    
    // Minimum brightness for any edge (prevents going too dark)
    private const double MinEdgeBrightness = 0.3;  // Allow much dimmer edges
    
    // How many frames without face before returning to neutral
    private const int FramesToNeutral = 30;
    
    private int _framesWithoutFace;

    /// <summary>
    /// Current smoothed lighting parameters.
    /// </summary>
    public LightingParameters CurrentParameters => _currentParams;

    /// <summary>
    /// Event raised when lighting parameters change significantly.
    /// </summary>
    public event EventHandler<LightingParameters>? ParametersChanged;

    /// <summary>
    /// Updates lighting based on face position event.
    /// </summary>
    public void UpdateFromFacePosition(FaceTrackingEventArgs e)
    {
        if (e.FaceDetected)
        {
            _framesWithoutFace = 0;
            _targetParams = CalculateLightingFromFace(e.NormalizedX, e.NormalizedY);
        }
        else
        {
            _framesWithoutFace++;
            
            // Gradually return to neutral when no face detected
            if (_framesWithoutFace > FramesToNeutral)
            {
                _targetParams = LightingParameters.Default with
                {
                    IsTracking = true,
                    FaceDetected = false
                };
            }
        }
        
        // Apply smoothing
        _currentParams = SmoothTransition(_currentParams, _targetParams);
        
        ParametersChanged?.Invoke(this, _currentParams);
    }

    /// <summary>
    /// Calculates lighting parameters from normalized face position.
    /// </summary>
    /// <param name="normalizedX">Face X position (0 = left of frame, 1 = right of frame)</param>
    /// <param name="normalizedY">Face Y position (0 = top of frame, 1 = bottom of frame)</param>
    private LightingParameters CalculateLightingFromFace(float normalizedX, float normalizedY)
    {
        // Note: Camera typically shows mirrored view, so:
        // - normalizedX close to 0 means user is on RIGHT side of screen (looking left)
        // - normalizedX close to 1 means user is on LEFT side of screen (looking right)
        
        // Convert to screen-relative position (mirror the X axis)
        double screenX = 1.0 - normalizedX;  // Now: 0 = left of screen, 1 = right of screen
        double screenY = normalizedY;         // 0 = top, 1 = bottom
        
        // Calculate how far from center the face is (-0.5 to +0.5)
        double xOffset = screenX - 0.5;  // Negative = left, Positive = right
        double yOffset = screenY - 0.5;  // Negative = top, Positive = bottom
        
        // Calculate edge brightnesses based on position
        // The idea: brighten the edge the user is closest to
        
        // Horizontal: if user is on left (negative offset), brighten left edge
        double leftBoost = Math.Max(0, -xOffset * 2 * PositionInfluence);
        double rightBoost = Math.Max(0, xOffset * 2 * PositionInfluence);
        
        // Vertical: if user is at top (negative offset), brighten top edge
        double topBoost = Math.Max(0, -yOffset * 2 * PositionInfluence);
        double bottomBoost = Math.Max(0, yOffset * 2 * PositionInfluence);
        
        // Apply boosts with minimum brightness
        return new LightingParameters
        {
            Brightness = 1.0,
            LeftBrightness = Math.Clamp(MinEdgeBrightness + leftBoost + (1 - MinEdgeBrightness) * (1 - rightBoost), MinEdgeBrightness, 1.0),
            RightBrightness = Math.Clamp(MinEdgeBrightness + rightBoost + (1 - MinEdgeBrightness) * (1 - leftBoost), MinEdgeBrightness, 1.0),
            TopBrightness = Math.Clamp(MinEdgeBrightness + topBoost + (1 - MinEdgeBrightness) * (1 - bottomBoost), MinEdgeBrightness, 1.0),
            BottomBrightness = Math.Clamp(MinEdgeBrightness + bottomBoost + (1 - MinEdgeBrightness) * (1 - topBoost), MinEdgeBrightness, 1.0),
            IsTracking = true,
            FaceDetected = true
        };
    }

    /// <summary>
    /// Smoothly transitions between current and target parameters.
    /// </summary>
    private LightingParameters SmoothTransition(LightingParameters current, LightingParameters target)
    {
        // When smoothing is disabled, just return target directly
        if (SmoothingFactor <= 0.01)
            return target;
            
        // Note: This code is currently unreachable since SmoothingFactor = 0.0
        // Kept for future use if we re-enable smoothing
#pragma warning disable CS0162
        return new LightingParameters
        {
            Brightness = Lerp(current.Brightness, target.Brightness, 1 - SmoothingFactor),
            LeftBrightness = Lerp(current.LeftBrightness, target.LeftBrightness, 1 - SmoothingFactor),
            RightBrightness = Lerp(current.RightBrightness, target.RightBrightness, 1 - SmoothingFactor),
            TopBrightness = Lerp(current.TopBrightness, target.TopBrightness, 1 - SmoothingFactor),
            BottomBrightness = Lerp(current.BottomBrightness, target.BottomBrightness, 1 - SmoothingFactor),
            IsTracking = target.IsTracking,
            FaceDetected = target.FaceDetected
        };
#pragma warning restore CS0162
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    /// Resets to default neutral lighting.
    /// </summary>
    public void Reset()
    {
        _currentParams = LightingParameters.Default;
        _targetParams = LightingParameters.Default;
        _framesWithoutFace = 0;
        ParametersChanged?.Invoke(this, _currentParams);
    }

    /// <summary>
    /// Gets a color with brightness applied.
    /// </summary>
    public static Color ApplyBrightness(Color baseColor, double brightness)
    {
        return Color.FromArgb(
            baseColor.A,
            (byte)(baseColor.R * brightness),
            (byte)(baseColor.G * brightness),
            (byte)(baseColor.B * brightness));
    }

    /// <summary>
    /// Creates a gradient brush for the edge light frame with position-based brightness.
    /// </summary>
    public LinearGradientBrush CreateEdgeLightBrush(LightingParameters parameters, Color baseColor)
    {
        // Create a radial-like effect using linear gradient stops
        // This is a simplified version - full implementation would need custom geometry
        
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        
        // Average the edge brightnesses for a simple gradient
        var topLeft = (parameters.TopBrightness + parameters.LeftBrightness) / 2;
        var topRight = (parameters.TopBrightness + parameters.RightBrightness) / 2;
        var bottomLeft = (parameters.BottomBrightness + parameters.LeftBrightness) / 2;
        var bottomRight = (parameters.BottomBrightness + parameters.RightBrightness) / 2;
        
        brush.GradientStops.Add(new GradientStop(ApplyBrightness(baseColor, topLeft), 0.0));
        brush.GradientStops.Add(new GradientStop(ApplyBrightness(baseColor, topRight), 0.5));
        brush.GradientStops.Add(new GradientStop(ApplyBrightness(baseColor, bottomRight), 1.0));
        
        return brush;
    }
}
