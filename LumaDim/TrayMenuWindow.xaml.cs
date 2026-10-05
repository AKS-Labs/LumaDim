using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace LumaDim;

public sealed partial class TrayMenuWindow : Window
{
    private const double WidthDip = 244;
    private const double HeightDip = 196;

    private const int GWL_WNDPROC = -4;
    private const uint WM_KEYDOWN = 0x0100;

    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcher;
    private WndProcDelegate? _wndProc;
    private IntPtr _oldProc;
    private Storyboard? _runningAnimation;
    private bool _visible;

    public event EventHandler? OpenRequested;
    public event EventHandler? DimOffRequested;
    public event EventHandler? StartupToggled;
    public event EventHandler? IndicatorToggled;
    public event EventHandler? ExitRequested;

    public TrayMenuWindow()
    {
        InitializeComponent();

        var prepared = WindowHelpers.PrepareFlyout(this);
        _appWindow = prepared.appWindow;
        _hwnd = prepared.hwnd;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _wndProc = WndProc;
        _oldProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && _visible)
                HideMenu();
        };
    }

    public bool IsMenuVisible => _visible;

    public void ShowAt(int cursorX, int cursorY, bool startupOn, bool indicatorOn)
    {
        StartupCheck.Visibility = startupOn ? Visibility.Visible : Visibility.Collapsed;
        IndicatorCheck.Visibility = indicatorOn ? Visibility.Visible : Visibility.Collapsed;

        var monitor = MonitorFromPoint(new POINT { x = cursorX, y = cursorY }, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        var scale = Math.Max(0.5, dpiX / 96.0);

        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var offset = (int)Math.Round(8 * scale);
        var inset = (int)Math.Round(6 * scale);

        var x = cursorX - width + offset;
        var y = cursorY - height - offset;
        if (x + width > info.rcWork.right - inset)
            x = info.rcWork.right - width - inset;
        if (x < info.rcWork.left + inset)
            x = info.rcWork.left + inset;
        if (y < info.rcWork.top + inset)
            y = Math.Min(cursorY + offset, info.rcWork.bottom - height - inset);

        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(x, y));

        if (!_visible)
        {
            Root.Opacity = 0;
            _appWindow.Show();
            _visible = true;
            _dispatcher.TryEnqueue(FadeIn);
        }
        else
        {
            FadeIn();
        }
        Activate();
    }

    public void HideMenu()
    {
        if (!_visible)
            return;
        _visible = false;
        _runningAnimation?.Stop();
        _runningAnimation = null;
        Root.Opacity = 0;
        _appWindow.Hide();
    }

    private void FadeIn()
    {
        _runningAnimation?.Stop();
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(110)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, Root);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fadeIn);
        _runningAnimation = storyboard;
        storyboard.Begin();
    }

    private void CloseAfterAction(Action action)
    {
        HideMenu();
        action();
    }

    private void OpenRow_Tapped(object sender, TappedRoutedEventArgs e) => CloseAfterAction(() => OpenRequested?.Invoke(this, EventArgs.Empty));

    private void DimOffRow_Tapped(object sender, TappedRoutedEventArgs e) => CloseAfterAction(() => DimOffRequested?.Invoke(this, EventArgs.Empty));

    private void StartupRow_Tapped(object sender, TappedRoutedEventArgs e) => CloseAfterAction(() => StartupToggled?.Invoke(this, EventArgs.Empty));

    private void IndicatorRow_Tapped(object sender, TappedRoutedEventArgs e) => CloseAfterAction(() => IndicatorToggled?.Invoke(this, EventArgs.Empty));

    private void ExitRow_Tapped(object sender, TappedRoutedEventArgs e) => CloseAfterAction(() => ExitRequested?.Invoke(this, EventArgs.Empty));

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border row)
            row.Style = (Style)Application.Current.Resources["MenuRowHoverStyle"];
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border row)
            row.Style = (Style)Application.Current.Resources["MenuRowStyle"];
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_KEYDOWN && wParam.ToInt64() == (long)Windows.System.VirtualKey.Escape && _visible)
        {
            HideMenu();
            return IntPtr.Zero;
        }
        return CallWindowProc(_oldProc, hwnd, message, wParam, lParam);
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
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
