using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace LumaDim;

public enum OsdPosition
{
    BottomCenter = 0,
    TopLeft = 1,
    TopCenter = 2
}

public sealed partial class BrightnessOsdWindow : Window
{
    private const double WidthDip = 284;
    private const double HeightDip = 60;
    private const double TrackWidthDip = 140;
    private const int HideAfterMilliseconds = 1600;

    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _hideTimer;
    private Storyboard? _runningAnimation;
    private OsdPosition _position;
    private bool _visible;
    private int _showToken;

    public BrightnessOsdWindow()
    {
        InitializeComponent();

        var prepared = WindowHelpers.PrepareFlyout(this, WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        _appWindow = prepared.appWindow;
        _hwnd = prepared.hwnd;

        SystemBackdrop = new DesktopAcrylicBackdrop();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _hideTimer = _dispatcher.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(HideAfterMilliseconds);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideAnimated();
    }

    public void Show(double brightness, OsdPosition position)
    {
        _position = position;
        ValueText.Text = $"{brightness:0}%";
        Fill.Width = TrackWidthDip * Math.Clamp(brightness, 0, 100) / 100.0;

        var (x, y, width, height) = ComputePlacement();
        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(x, y));

        var token = ++_showToken;
        if (!_visible)
        {
            Root.Opacity = 0;
            _appWindow.Show();
            _visible = true;
            _dispatcher.TryEnqueue(() =>
            {
                if (token == _showToken)
                    StartFadeIn();
            });
        }
        else
        {
            StartFadeIn();
        }

        WindowHelpers.SetTopmost(_hwnd);
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        _runningAnimation?.Stop();
        _runningAnimation = null;
        Root.Opacity = 0;
        if (_visible)
        {
            _appWindow.Hide();
            _visible = false;
        }
    }

    private void HideAnimated()
    {
        if (!_visible)
            return;

        var token = _showToken;
        _runningAnimation?.Stop();

        var fadeOut = new DoubleAnimation { To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(150)) };
        Storyboard.SetTarget(fadeOut, Root);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeOut);
        storyboard.Completed += (_, _) =>
        {
            if (token != _showToken)
                return;
            Root.Opacity = 0;
            _appWindow.Hide();
            _visible = false;
        };
        _runningAnimation = storyboard;
        storyboard.Begin();
    }

    private void StartFadeIn()
    {
        _hideTimer.Stop();
        _runningAnimation?.Stop();

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, Root);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeIn);
        _runningAnimation = storyboard;
        storyboard.Begin();

        _hideTimer.Start();
    }

    private (int x, int y, int width, int height) ComputePlacement()
    {
        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        var scale = Math.Max(0.5, dpiX / 96.0);

        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var workWidth = info.rcWork.right - info.rcWork.left;
        var workHeight = info.rcWork.bottom - info.rcWork.top;

        var x = info.rcWork.left + (workWidth - width) / 2;
        var topMargin = (int)Math.Round(24 * scale);
        var y = _position switch
        {
            OsdPosition.TopLeft or OsdPosition.TopCenter => info.rcWork.top + topMargin,
            _ => info.rcWork.bottom - height - (int)Math.Round(20 * scale)
        };

        if (_position == OsdPosition.TopLeft)
            x = info.rcWork.left + topMargin;
        if (workHeight < height + 8)
            y = info.rcWork.top;
        return (x, y, width, height);
    }

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

