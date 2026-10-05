using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LumaDim;

public sealed class OverlayWindow : IDisposable
{
    public const double MaxDimPercent = 55;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private static readonly string WindowClassName = $"LumaDim.BlackOverlay.{Environment.ProcessId}";
    private static readonly WindowProcedure WindowProcedureCallback = OverlayWindowProcedure;
    private static IntPtr _classInstance;
    private static IntPtr _blackBrush;
    private static bool _windowClassRegistered;

    private readonly IntPtr _hwnd;

    public OverlayWindow()
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        EnsureWindowClass();

        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            WindowClassName,
            string.Empty,
            WS_POPUP,
            left,
            top,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandleW(IntPtr.Zero),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the dim overlay window.");

        if (!SetLayeredWindowAttributes(_hwnd, 0, 0, LWA_ALPHA))
        {
            var error = Marshal.GetLastWin32Error();
            DestroyWindow(_hwnd);
            throw new Win32Exception(error, "Unable to initialize the dim overlay transparency.");
        }

        if (!SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW))
        {
            var error = Marshal.GetLastWin32Error();
            DestroyWindow(_hwnd);
            throw new Win32Exception(error, "Unable to show the dim overlay window.");
        }
    }

    public void SetDim(double value)
    {
        var opacity = Math.Clamp(value, 0, MaxDimPercent) / 100.0;
        var alpha = (byte)Math.Round(opacity * byte.MaxValue);
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
    }

    public void SetTopmost(bool topmost)
    {
        SetWindowPos(_hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    public void Dispose() => DestroyWindow(_hwnd);

    private static void EnsureWindowClass()
    {
        if (_windowClassRegistered)
            return;

        _classInstance = GetModuleHandleW(IntPtr.Zero);
        _blackBrush = CreateSolidBrush(0);
        if (_blackBrush == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the black overlay brush.");

        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureCallback),
            Instance = _classInstance,
            BackgroundBrush = _blackBrush,
            ClassName = WindowClassName
        };

        if (RegisterClassExW(ref windowClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            DeleteObject(_blackBrush);
            _blackBrush = IntPtr.Zero;
            throw new Win32Exception(error, "Unable to register the black overlay window class.");
        }

        _windowClassRegistered = true;
    }

    private static IntPtr OverlayWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(IntPtr moduleName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

}
