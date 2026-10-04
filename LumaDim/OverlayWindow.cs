using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace LumaDim;

// Minimal click-through, topmost overlay. This initial version covers the primary display;
// multi-monitor coverage can be added after validating behavior on the target machine.
public sealed class OverlayWindow : Window
{
    private readonly Grid _root;
    private readonly IntPtr _hwnd;
    public OverlayWindow()
    {
        _root = new Grid { Background = new SolidColorBrush(Colors.Black) };
        Content = _root;
        _hwnd = WindowNative.GetWindowHandle(this);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1920,1080));
        appWindow.Move(new Windows.Graphics.PointInt32(0,0));
        appWindow.IsShownInSwitchers = false;
        SetWindowLongPtr(_hwnd, -20, new IntPtr(unchecked((int)0x08000020))); // layered + transparent + toolwindow
        SetWindowPos(_hwnd, new IntPtr(-1), 0,0,0,0, 0x0001|0x0002|0x0010);
        Activated += (_,_) => SetWindowPos(_hwnd,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);
    }
    public void SetDim(double value)
    {
        _root.Opacity = Math.Clamp(value,0,90)/100.0;
        _root.IsHitTestVisible = false;
    }
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
}
