using System.Runtime.InteropServices;

namespace LumaDim;

public sealed class TrayIcon : IDisposable
{
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private IntPtr _icon;
    private bool _added;

    public TrayIcon(IntPtr hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
        _icon = CreateIcon();

        var data = BuildData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.szTip = "LumaDim";
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void SetTooltip(string tooltip)
    {
        if (!_added)
            return;
        var data = BuildData();
        data.uFlags = NIF_TIP;
        data.szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATA BuildData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uCallbackMessage = _callbackMessage,
        hIcon = _icon
    };

    private static IntPtr CreateIcon()
    {
        try
        {
            var assembly = typeof(TrayIcon).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("tray32.png", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var bytes = buffer.ToArray();
                    var handle = CreateIconFromResourceEx(bytes, bytes.Length, true, 0x00030000, 32, 32, 0);
                    if (handle != IntPtr.Zero)
                        return handle;
                }
            }
        }
        catch { }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, int size, bool icon, uint version, int desiredWidth, int desiredHeight, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
