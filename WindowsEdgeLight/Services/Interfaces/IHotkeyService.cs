namespace WindowsEdgeLight.Services.Interfaces;

/// <summary>
/// Service for managing global hotkeys.
/// </summary>
public interface IHotkeyService
{
    /// <summary>
    /// Registers a global hotkey.
    /// </summary>
    /// <param name="hwnd">Window handle to receive hotkey messages.</param>
    /// <param name="id">Unique identifier for the hotkey.</param>
    /// <param name="modifiers">Modifier keys (Ctrl, Shift, Alt).</param>
    /// <param name="vk">Virtual key code.</param>
    /// <returns>True if registration succeeded.</returns>
    bool RegisterHotkey(IntPtr hwnd, int id, uint modifiers, uint vk);

    /// <summary>
    /// Unregisters a global hotkey.
    /// </summary>
    /// <param name="hwnd">Window handle that registered the hotkey.</param>
    /// <param name="id">Unique identifier of the hotkey to unregister.</param>
    /// <returns>True if unregistration succeeded.</returns>
    bool UnregisterHotkey(IntPtr hwnd, int id);
}
