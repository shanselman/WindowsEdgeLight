using System;
using System.Runtime.InteropServices;

namespace WindowsEdgeLight;

/// <summary>
/// Manages HDR (High Dynamic Range) and Advanced Color detection and settings.
/// Ensures the application works properly with Windows Advanced Color Management without affecting other apps.
/// </summary>
public class HdrColorManager
{
    // HDR color temperature recommendation
    private const double HDR_RECOMMENDED_COLOR_TEMP = 0.55;
    
    // Default fallback capability for non-HDR displays
    private static readonly HdrCapability DefaultCapability = new HdrCapability 
    { 
        IsSupported = false, 
        IsEnabled = false 
    };
    // Display configuration structures and flags
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    // P/Invoke declarations for display configuration APIs
    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, IntPtr modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 8;
    
    // Bit flags for advanced color info
    private const uint DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_SUPPORTED = 0x01;
    private const uint DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_ENABLED = 0x02;

    /// <summary>
    /// Detects if HDR / Advanced Color is supported and enabled on the primary display.
    /// </summary>
    /// <returns>HDRCapability containing support and enablement status</returns>
    public static HdrCapability DetectHdrCapability()
    {
        try
        {
            // Get display configuration buffer sizes
            uint pathCount = 0;
            uint modeCount = 0;
            int error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
            
            if (error != 0 || pathCount == 0)
            {
                return DefaultCapability;
            }

            // Query active display paths
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, IntPtr.Zero, IntPtr.Zero);
            
            if (error != 0)
            {
                return DefaultCapability;
            }

            // Check the first active display (typically the primary)
            // In a multi-monitor setup, we'd check the specific monitor the window is on
            foreach (var path in paths)
            {
                if (path.targetInfo.targetAvailable)
                {
                    var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                            size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                            adapterId = path.targetInfo.adapterId,
                            id = path.targetInfo.id
                        }
                    };

                    error = DisplayConfigGetDeviceInfo(ref colorInfo);
                    
                    if (error == 0)
                    {
                        bool isSupported = (colorInfo.value & DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_SUPPORTED) != 0;
                        bool isEnabled = (colorInfo.value & DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_ENABLED) != 0;
                        
                        return new HdrCapability 
                        { 
                            IsSupported = isSupported, 
                            IsEnabled = isEnabled,
                            BitsPerColor = colorInfo.bitsPerColorChannel
                        };
                    }
                    
                    // Return result for first available display
                    break;
                }
            }
        }
        catch (Exception)
        {
            // If any error occurs, fall back to SDR mode
        }

        return DefaultCapability;
    }

    /// <summary>
    /// Gets the recommended color temperature adjustment for HDR displays.
    /// HDR displays often benefit from slightly warmer color temperatures.
    /// </summary>
    public static double GetRecommendedColorTemperatureForHdr()
    {
        // For HDR displays, a slightly warmer default provides better visual comfort
        return HDR_RECOMMENDED_COLOR_TEMP;
    }

    /// <summary>
    /// Checks if Windows Auto Color Management is available (Windows 11+).
    /// When ACM is active, the system automatically manages color spaces.
    /// </summary>
    public static bool IsAutoColorManagementAvailable()
    {
        try
        {
            // Check Windows version - ACM is Windows 11 and later
            var version = Environment.OSVersion.Version;
            
            // Windows 11 is version 10.0.22000 and higher
            // Note: Full Auto Color Management with all features is in 22H2 (build 22621+)
            // but basic advanced color support exists from initial Windows 11 (build 22000)
            if (version.Major >= 10 && version.Build >= 22000)
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Fall back to false if version check fails
        }

        return false;
    }
}

/// <summary>
/// Contains information about HDR/Advanced Color capability of a display.
/// </summary>
public class HdrCapability
{
    /// <summary>
    /// True if the display hardware supports HDR/Advanced Color
    /// </summary>
    public bool IsSupported { get; set; }
    
    /// <summary>
    /// True if HDR/Advanced Color is currently enabled in Windows settings
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// Bits per color channel (8 for SDR, 10+ for HDR)
    /// </summary>
    public uint BitsPerColor { get; set; }
    
    /// <summary>
    /// True if HDR is both supported and enabled
    /// </summary>
    public bool IsHdrActive => IsSupported && IsEnabled;
}
