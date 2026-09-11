using System.Runtime.InteropServices;

namespace WindowsEdgeLight;

// A raw Win32 layered window, created and managed entirely outside WPF's own window
// infrastructure - not a System.Windows.Window, no AllowsTransparency involved.
//
// Why: WPF's AllowsTransparency windows re-render and re-transfer their ENTIRE
// monitor-sized per-pixel-alpha surface via UpdateLayeredWindow on every visual change,
// however small (even just Opacity). This class instead renders the glow to a bitmap
// once and pushes it via UpdateLayeredWindow ourselves, using its SourceConstantAlpha
// blend parameter for brightness - that path re-pushes the *same* cached bitmap with a
// new alpha value, no re-render involved.
//
// IMPORTANT: this hwnd must never also be managed by WPF's own per-pixel-alpha machinery.
// An earlier attempt called SetLayeredWindowAttributes on a WPF AllowsTransparency
// window's own hwnd (i.e. two different mechanisms fighting over the same window's
// layered state) and it froze the whole app. This class owns its hwnd exclusively - no
// WPF Window ever wraps or touches it - to avoid that failure mode entirely.
internal sealed class NativeLayeredWindow : IDisposable
{
    private const string ClassName = "WindowsEdgeLightGlowWindow";

    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_POPUP = 0x80000000;

    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    private static ushort _classAtom;
    private static WndProc? _wndProcDelegate; // kept alive for the process lifetime

    private IntPtr _hwnd;
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _bitmapBits = IntPtr.Zero;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _left;
    private int _top;
    // The DC's own default (stock) bitmap - must be reselected before deleting ours.
    private IntPtr _originalBitmap = IntPtr.Zero;

    public IntPtr Handle => _hwnd;

    public NativeLayeredWindow()
    {
        EnsureClassRegistered();

        _hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            ClassName, null, WS_POPUP,
            0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"NativeLayeredWindow: CreateWindowEx failed, error={error}");
            throw new InvalidOperationException($"CreateWindowEx failed with error {error}");
        }
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;

        _wndProcDelegate = (hWnd, msg, wParam, lParam) => DefWindowProc(hWnd, msg, wParam, lParam);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProcDelegate,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = ClassName,
            hIconSm = IntPtr.Zero
        };

        _classAtom = RegisterClassEx(ref wc);
        if (_classAtom == 0)
        {
            int error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"NativeLayeredWindow: RegisterClassEx failed, error={error}");
            throw new InvalidOperationException($"RegisterClassEx failed with error {error}");
        }
    }

    public void SetBounds(int left, int top, int width, int height)
    {
        _left = left;
        _top = top;
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
    }

    public void Show() => ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

    public void Hide() => ShowWindow(_hwnd, SW_HIDE);

    // Copies the given premultiplied-BGRA32 pixel bytes into the window and pushes it
    // with the given alpha. The caller owns rendering (WPF or otherwise) - this class only
    // ever deals in raw pixels, so callers can cheaply reuse/mutate a cached buffer (see
    // MainWindow's GlowWindowContext.BaseFramePixels/PushCurrentState) instead of paying
    // for a full re-render on every call.
    public void RenderPixels(byte[] pixels, int width, int height, byte alpha)
    {
        EnsureDibSection(width, height);
        if (_hBitmap == IntPtr.Zero) return;

        Marshal.Copy(pixels, 0, _bitmapBits, pixels.Length);

        Push(alpha);
    }

    // Re-pushes the last rendered bitmap with a new alpha, without touching its pixel
    // content at all. This is the cheap path used for brightness changes.
    public void SetAlpha(byte alpha)
    {
        if (_hBitmap == IntPtr.Zero) return;
        Push(alpha);
    }

    private void EnsureDibSection(int width, int height)
    {
        if (_hBitmap != IntPtr.Zero && width == _bitmapWidth && height == _bitmapHeight)
        {
            return;
        }

        if (_memDc == IntPtr.Zero)
        {
            var screenDcForCreate = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screenDcForCreate);
            ReleaseDC(IntPtr.Zero, screenDcForCreate);
        }

        // A GDI bitmap must be deselected from its DC before it can be deleted - deleting
        // one while still selected is invalid and silently leaks the handle instead of
        // freeing it. Restore the DC's own original bitmap first.
        FreeBitmap();

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down, matching WPF's pixel row order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            }
        };

        _hBitmap = CreateDIBSection(_memDc, ref bmi, DIB_RGB_COLORS, out _bitmapBits, IntPtr.Zero, 0);
        if (_hBitmap == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"NativeLayeredWindow: CreateDIBSection failed, error={error}");
            return;
        }

        var previous = SelectObject(_memDc, _hBitmap);
        if (_originalBitmap == IntPtr.Zero)
        {
            // The DC's own default (stock) bitmap, saved once so it can be restored before
            // any of our bitmaps are deleted.
            _originalBitmap = previous;
        }

        _bitmapWidth = width;
        _bitmapHeight = height;
    }

    private void Push(byte alpha)
    {
        if (_hBitmap == IntPtr.Zero || _memDc == IntPtr.Zero) return;

        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            var ptSrc = new NativePoint { X = 0, Y = 0 };
            var size = new NativeSize { cx = _bitmapWidth, cy = _bitmapHeight };
            var ptDst = new NativePoint { X = _left, Y = _top };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = AC_SRC_ALPHA
            };

            if (!UpdateLayeredWindow(_hwnd, screenDc, ref ptDst, ref size, _memDc, ref ptSrc, 0, ref blend, ULW_ALPHA))
            {
                int error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"NativeLayeredWindow: UpdateLayeredWindow failed, error={error}");
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void FreeBitmap()
    {
        if (_hBitmap != IntPtr.Zero)
        {
            // Deselect our bitmap (restore the DC's own default one) before deleting it -
            // deleting a bitmap while it's still selected into a DC is invalid and leaks
            // the handle instead of freeing it.
            if (_memDc != IntPtr.Zero && _originalBitmap != IntPtr.Zero)
            {
                SelectObject(_memDc, _originalBitmap);
            }
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
            _bitmapBits = IntPtr.Zero;
            _bitmapWidth = 0;
            _bitmapHeight = 0;
        }
    }

    public void Dispose()
    {
        FreeBitmap();
        if (_memDc != IntPtr.Zero)
        {
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
