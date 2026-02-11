using System.Runtime.InteropServices;
using WindowsEdgeLight.Services.Interfaces;

namespace WindowsEdgeLight.Services;

/// <summary>
/// Service for managing global hotkeys.
/// </summary>
public class HotkeyService : IHotkeyService
{
    #region P/Invoke Declarations

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    #endregion

    /// <inheritdoc/>
    public bool RegisterHotkey(IntPtr hwnd, int id, uint modifiers, uint vk)
    {
        return RegisterHotKey(hwnd, id, modifiers, vk);
    }

    /// <inheritdoc/>
    public bool UnregisterHotkey(IntPtr hwnd, int id)
    {
        return UnregisterHotKey(hwnd, id);
    }
}
