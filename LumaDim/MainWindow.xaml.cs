using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Management;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace LumaDim;

public sealed partial class MainWindow : Window
{
    private OverlayWindow? _overlay;
    private bool _ready;
    private const int WM_APP = 0x8000;
    private const int WM_TRAY = WM_APP + 1;
    private const uint NIF_MESSAGE=1, NIF_ICON=2, NIF_TIP=4, NIM_ADD=0, NIM_DELETE=2;
    private const uint TPM_RIGHTBUTTON=2, MF_STRING=0, MF_SEPARATOR=0x800;
    private IntPtr _trayIcon;
    private readonly IntPtr _hwnd;
    private WndProcDelegate? _wndProc;
    private IntPtr _oldProc;

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        AddTrayIcon();
        _overlay = new OverlayWindow();
        _overlay.Activate();
        _overlay.SetDim(35);
        _ready = true;
        Activated += (_, _) => { };
        Closed += (_, _) => RemoveTrayIcon();
    }

    private void DimSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (LevelText != null) LevelText.Text = $"Extra dim · {e.NewValue:0}%";
        _overlay?.SetDim(e.NewValue);
        if (_ready && HardwareSwitch.IsOn) SetHardwareBrightness(100 - (int)e.NewValue);
    }

    private void HardwareSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_ready && HardwareSwitch.IsOn) SetHardwareBrightness(100 - (int)DimSlider.Value);
    }
    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (StartupSwitch.IsOn) key?.SetValue("LumaDim", $"\"{Environment.ProcessPath}\" --background");
            else key?.DeleteValue("LumaDim", false);
            key?.Dispose();
        }
        catch { }
    }
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Exit();

    private void SetHardwareBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject item in searcher.Get())
                item.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)Math.Clamp(percent, 0, 100) });
        }
        catch { }
    }

    private void AddTrayIcon()
    {
        _wndProc = WndProc;
        _oldProc = SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));
        _trayIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        var data = new NOTIFYICONDATA { cbSize=(uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd=_hwnd, uID=1, uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP, uCallbackMessage=WM_TRAY, hIcon=_trayIcon, szTip="LumaDim" };
        Shell_NotifyIcon(NIM_ADD, ref data);
    }
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY && (uint)lParam.ToInt64() == 0x0205) ShowWindow();
        return CallWindowProc(_oldProc, hwnd, msg, wParam, lParam);
    }
    private void ShowWindow()
    {
        AppWindow.Resize(new SizeInt32(380, 330));
        Activate();
    }
    private void RemoveTrayIcon()
    {
        var data = new NOTIFYICONDATA { cbSize=(uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd=_hwnd, uID=1 };
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct NOTIFYICONDATA { public uint cbSize; public IntPtr hWnd; public uint uID,uFlags,uCallbackMessage; public IntPtr hIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string szTip; }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProcDelegate(IntPtr hwnd,uint msg,IntPtr wParam,IntPtr lParam);
}
