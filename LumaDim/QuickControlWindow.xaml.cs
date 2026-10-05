using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace LumaDim;

public sealed partial class QuickControlWindow : Window
{
    private const double WidthDip = 300;
    private const double HeightDip = 96;
    private const int AutoHideMilliseconds = 4000;

    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _syncing;
    private bool _pointerInside;
    private bool _visible;

    public event EventHandler<double>? LevelChanged;
    public event EventHandler? SettingsRequested;

    public QuickControlWindow()
    {
        InitializeComponent();

        var prepared = WindowHelpers.PrepareFlyout(this);
        _appWindow = prepared.appWindow;
        _hwnd = prepared.hwnd;

        SystemBackdrop = new DesktopAcrylicBackdrop();

        LevelSlider.ValueChanged += LevelSlider_ValueChanged;

        _hideTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(AutoHideMilliseconds);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) =>
        {
            if (!_pointerInside)
                HideHud();
        };

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _visible)
                HideHud();
        };
    }

    public void ShowAt(double level)
    {
        SyncLevel(level);

        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(new POINT { x = point.x, y = point.y }, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        var scale = Math.Max(0.5, dpiX / 96.0);

        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var gap = (int)Math.Round(12 * scale);
        var x = info.rcWork.right - width - gap;
        var y = info.rcWork.bottom - height - gap;

        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(x, y));
        _visible = true;
        Activate();
        WindowHelpers.SetTopmost(_hwnd);
        LevelSlider.Focus(FocusState.Programmatic);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideHud()
    {
        if (!_visible)
            return;
        _visible = false;
        _hideTimer.Stop();
        _appWindow.Hide();
    }

    public void SyncLevel(double level)
    {
        _syncing = true;
        LevelSlider.Value = level;
        ValueText.Text = $"{level:0}%";
        _syncing = false;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        _hideTimer.Stop();
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void LevelSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing)
            return;
        ValueText.Text = $"{e.NewValue:0}%";
        LevelChanged?.Invoke(this, e.NewValue);
    }

    private void DimOffButton_Click(object sender, RoutedEventArgs e) => LevelSlider.Value = 100;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}

