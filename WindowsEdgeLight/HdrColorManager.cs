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

    // Mode info structure - we need to allocate space for this even if we don't use it
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfo; // Union of different mode types, 64 bytes should be enough
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    // The advanced color info struct - must match Windows SDK exactly
    // See: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_get_advanced_color_info
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;  // Bit flags for advanced color support
        public uint colorEncoding;  // DISPLAYCONFIG_COLOR_ENCODING
        public uint bitsPerColorChannel;
    }

    // P/Invoke declarations for display configuration APIs
    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    
    // DISPLAYCONFIG_DEVICE_INFO_TYPE values
    // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9 (not 8!)
    // See: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ne-wingdi-displayconfig_device_info_type
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    
    // Bit flags for advanced color info
    private const uint DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_SUPPORTED = 0x01;
    private const uint DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_ENABLED = 0x02;

    /// <summary>
    /// Detects if HDR / Advanced Color is supported and enabled on any active display.
    /// Prioritizes HDR-enabled displays over HDR-supported-but-not-enabled displays.
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

            // Query active display paths - must allocate mode array even if we don't use it
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            
            if (error != 0)
            {
                return DefaultCapability;
            }

            // Check ALL displays and find the best HDR capability
            // Priority: HDR enabled > HDR supported > SDR
            HdrCapability? bestCapability = null;
            
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
                        
                        var capability = new HdrCapability 
                        { 
                            IsSupported = isSupported, 
                            IsEnabled = isEnabled,
                            BitsPerColor = colorInfo.bitsPerColorChannel
                        };
                        
                        // If this display has HDR enabled, use it immediately
                        if (isEnabled)
                        {
                            return capability;
                        }
                        
                        // Otherwise, keep track of the best capability found
                        if (bestCapability == null || 
                            (isSupported && !bestCapability.IsSupported))
                        {
                            bestCapability = capability;
                        }
                    }
                }
            }
            
            return bestCapability ?? DefaultCapability;
        }
        catch (Exception ex)
        {
            // If any error occurs, fall back to SDR mode
            System.Diagnostics.Debug.WriteLine($"HDR Detection Error: {ex.Message}");
        }

        return DefaultCapability;
    }
    
    /// <summary>
    /// Gets diagnostic information about HDR detection for all monitors.
    /// </summary>
    public static string GetHdrDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== HDR Diagnostics ===");
        
        try
        {
            uint pathCount = 0;
            uint modeCount = 0;
            int error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
            
            sb.AppendLine($"GetDisplayConfigBufferSizes: error={error}, pathCount={pathCount}, modeCount={modeCount}");
            
            if (error != 0 || pathCount == 0)
            {
                sb.AppendLine("ERROR: Failed to get display config buffer sizes");
                return sb.ToString();
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            
            sb.AppendLine($"QueryDisplayConfig: error={error}");
            
            if (error != 0)
            {
                sb.AppendLine($"ERROR: Failed to query display config (Win32 error {error})");
                return sb.ToString();
            }

            int displayIndex = 0;
            
            // Show struct sizes for debugging
            sb.AppendLine($"\nStruct sizes: Header={Marshal.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>()}, ColorInfo={Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>()}");
            sb.AppendLine($"Type constant: {DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO}");
            
            foreach (var path in paths)
            {
                sb.AppendLine($"\n--- Display {displayIndex} ---");
                sb.AppendLine($"  targetAvailable: {path.targetInfo.targetAvailable}");
                sb.AppendLine($"  adapterId: {path.targetInfo.adapterId.LowPart}-{path.targetInfo.adapterId.HighPart}");
                sb.AppendLine($"  targetId: {path.targetInfo.id}");
                
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
                    
                    sb.AppendLine($"  DisplayConfigGetDeviceInfo: error={error}");
                    sb.AppendLine($"  colorInfo.value: 0x{colorInfo.value:X8}");
                    sb.AppendLine($"  bitsPerColorChannel: {colorInfo.bitsPerColorChannel}");
                    
                    bool isSupported = (colorInfo.value & DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_SUPPORTED) != 0;
                    bool isEnabled = (colorInfo.value & DISPLAYCONFIG_ADVANCED_COLOR_INFO_VALUE_ADVANCED_COLOR_ENABLED) != 0;
                    
                    sb.AppendLine($"  HDR Supported: {isSupported}");
                    sb.AppendLine($"  HDR Enabled: {isEnabled}");
                }
                
                displayIndex++;
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Exception: {ex.Message}");
        }
        
        return sb.ToString();
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
