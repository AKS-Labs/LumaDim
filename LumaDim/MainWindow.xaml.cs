using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace LumaDim;

public sealed partial class MainWindow : Window
{
    private const double MinBrightness = 45;
    private const double MaxBrightness = 100;
    private const int HotkeyStep = 5;
    private const int PrimaryHotkeyCount = 6;

    private const int IdBrighterUp = 1;
    private const int IdDarkerDown = 2;
    private const int IdBrighterOemPlus = 3;
    private const int IdBrighterNumpadPlus = 4;
    private const int IdDarkerOemMinus = 5;
    private const int IdDarkerNumpadMinus = 6;
    private const int IdLegacyBrighterOemMinus = 7;
    private const int IdLegacyDarkerOemPlus = 8;
    private const int IdLegacyBrighterNumpadMinus = 9;
    private const int IdLegacyDarkerNumpadPlus = 10;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_TRAY = 0x8001;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryValue = "LumaDim";

    private readonly IntPtr _hwnd;
    private readonly AppSettings _settings;
    private readonly Button[] _presetButtons;
    private readonly DispatcherQueue _dispatcher;
    private readonly HashSet<int> _registeredHotkeys = new();

    private OverlayWindow? _overlay;
    private readonly BrightnessOsdWindow _osd;
    private QuickControlWindow? _quickControl;
    private TrayMenuWindow? _trayMenu;
    private TrayIcon? _trayIcon;
    private WndProcDelegate? _wndProc;
    private IntPtr _oldProc;
    private DispatcherQueueTimer? _saveTimer;
    private bool _suppressSlider;
    private bool _loading = true;
    private bool _exiting;
    private bool _cleanedUp;

    public MainWindow()
    {
        Log.Write("ctor start");
        InitializeComponent();
        Log.Write("initialized");

        SystemBackdrop = new MicaBackdrop();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _hwnd = WindowNative.GetWindowHandle(this);
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(36, 255, 255, 255);
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(56, 255, 255, 255);
        SetTitleBar(TitleBarDragRegion);
        AppWindow.Resize(new SizeInt32(560, 740));
        AppWindow.Closing += OnWindowClosing;
        Log.Write("titlebar set");

        _settings = SettingsStore.Load();
        _presetButtons = new[] { Preset80Button, Preset65Button, Preset55Button, Preset45Button, DimOffButton };

        _overlay = new OverlayWindow();
        Log.Write("overlay ok");
        _osd = new BrightnessOsdWindow();
        Log.Write("osd ok");

        InstallTrayIcon();
        Log.Write("tray ok");

        _saveTimer = _dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(700);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SettingsStore.Save(_settings);

        ApplyStoredSettings();
        Log.Write("settings ok");
        RegisterAllHotkeys();
        Log.Write("hotkeys ok");

        Closed += (_, _) => Cleanup();
        Log.Write("ctor end");
    }

    public void ShowSettings()
    {
        _trayMenu?.HideMenu();
        _quickControl?.HideHud();
        Activate();
    }

    private void OnWindowClosing(object sender, AppWindowClosingEventArgs args)
    {
        if (_exiting)
            return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    private void ApplyStoredSettings()
    {
        _loading = true;
        BrightnessSlider.Value = Math.Clamp(_settings.Brightness, MinBrightness, MaxBrightness);
        ShortcutsSwitch.IsOn = _settings.ShortcutsEnabled;
        IndicatorSwitch.IsOn = _settings.ShowIndicator;
        PositionCombo.SelectedIndex = Math.Clamp(_settings.IndicatorPosition, 0, 2);
        StartupSwitch.IsOn = IsStartupConfigured();
        _settings.StartWithWindows = StartupSwitch.IsOn;
        _loading = false;

        UpdateLevelUi(BrightnessSlider.Value);
        UpdateShortcutStatus();
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSlider || _loading)
            return;
        ApplyLevel(e.NewValue, showIndicator: false);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out var level))
            ApplyLevel(level, showIndicator: false);
    }

    private void ApplyLevel(double level, bool showIndicator)
    {
        level = Math.Clamp(level, MinBrightness, MaxBrightness);
        _suppressSlider = true;
        if (Math.Abs(BrightnessSlider.Value - level) > 0.01)
            BrightnessSlider.Value = level;
        _suppressSlider = false;

        UpdateLevelUi(level);

        if (showIndicator && _settings.ShowIndicator)
            _osd.Show(level, (OsdPosition)_settings.IndicatorPosition);
    }

    private void UpdateLevelUi(double level)
    {
        var dim = 100 - level;
        BrightnessValueText.Text = $"{level:0}%";
        DimStatusText.Text = dim <= 0 ? "Dimming is off" : $"Screen darkened by {dim:0}%";
        StatusText.Text = dim <= 0 ? "Running Â· screen undimmed" : $"Running Â· screen dimmed by {dim:0}%";

        foreach (var button in _presetButtons)
        {
            var matches = double.TryParse(button.Tag as string, out var value) && Math.Abs(value - level) < 0.01;
            button.FontWeight = matches ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }

        _overlay?.SetDim(dim);
        _trayIcon?.SetTooltip($"LumaDim Â· Brightness {level:0}%");
        _quickControl?.SyncLevel(level);
        _settings.Brightness = level;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_saveTimer == null)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ShortcutsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _settings.ShortcutsEnabled = ShortcutsSwitch.IsOn;
        if (_settings.ShortcutsEnabled)
            RegisterAllHotkeys();
        else
            UnregisterAllHotkeys();
        UpdateShortcutStatus();
        ScheduleSave();
    }

    private void IndicatorSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _settings.ShowIndicator = IndicatorSwitch.IsOn;
        ScheduleSave();
    }

    private void PositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        _settings.IndicatorPosition = PositionCombo.SelectedIndex;
        ScheduleSave();
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _settings.StartWithWindows = StartupSwitch.IsOn;
        SetStartup(StartupSwitch.IsOn);
        ScheduleSave();
    }

    private static bool IsStartupConfigured()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
            return key?.GetValue(StartupRegistryValue) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupRegistryKey);
            if (key == null)
                return;
            if (enable)
                key.SetValue(StartupRegistryValue, $"\"{Environment.ProcessPath}\" --background");
            else
                key.DeleteValue(StartupRegistryValue, false);
        }
        catch { }
    }

    private void RegisterAllHotkeys()
    {
        UnregisterAllHotkeys();
        if (!_settings.ShortcutsEnabled)
            return;

        var ctrlAlt = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;
        Register(IdBrighterUp, ctrlAlt, 0x26);
        Register(IdDarkerDown, ctrlAlt, 0x28);
        Register(IdBrighterOemPlus, ctrlAlt, 0xBB);
        Register(IdBrighterNumpadPlus, ctrlAlt, 0x6B);
        Register(IdDarkerOemMinus, ctrlAlt, 0xBD);
        Register(IdDarkerNumpadMinus, ctrlAlt, 0x6D);
        Register(IdLegacyBrighterOemMinus, MOD_CONTROL | MOD_NOREPEAT, 0xBD);
        Register(IdLegacyDarkerOemPlus, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0xBB);
        Register(IdLegacyBrighterNumpadMinus, MOD_CONTROL | MOD_NOREPEAT, 0x6D);
        Register(IdLegacyDarkerNumpadPlus, MOD_CONTROL | MOD_NOREPEAT, 0x6B);
    }

    private void Register(int id, uint modifiers, uint virtualKey)
    {
        if (RegisterHotKey(_hwnd, id, modifiers, virtualKey))
            _registeredHotkeys.Add(id);
    }

    private void UnregisterAllHotkeys()
    {
        foreach (var id in _registeredHotkeys)
            UnregisterHotKey(_hwnd, id);
        _registeredHotkeys.Clear();
    }

    private void UpdateShortcutStatus()
    {
        if (!_settings.ShortcutsEnabled)
        {
            ShortcutStatusDot.Fill = (Brush)Application.Current.Resources["StatusNeutralBrush"];
            ShortcutStatusText.Text = "Shortcuts are turned off";
            return;
        }

        var allActive = true;
        for (var id = IdBrighterUp; id <= PrimaryHotkeyCount; id++)
            allActive &= _registeredHotkeys.Contains(id);

        if (allActive)
        {
            ShortcutStatusDot.Fill = (Brush)Application.Current.Resources["StatusSuccessBrush"];
            ShortcutStatusText.Text = "All shortcuts active";
        }
        else
        {
            ShortcutStatusDot.Fill = (Brush)Application.Current.Resources["StatusAttentionBrush"];
            ShortcutStatusText.Text = "Some shortcuts are used by another app";
        }
    }

    private void HandleHotkey(int id)
    {
        var increase = id is IdBrighterUp or IdBrighterOemPlus or IdBrighterNumpadPlus
            or IdLegacyBrighterOemMinus or IdLegacyBrighterNumpadMinus;
        ApplyLevel(BrightnessSlider.Value + (increase ? HotkeyStep : -HotkeyStep), showIndicator: true);
    }

    private void InstallTrayIcon()
    {
        _wndProc = WndProc;
        _oldProc = SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc));
        _trayIcon = new TrayIcon(_hwnd, WM_TRAY);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAY:
                var notification = (uint)(lParam.ToInt64() & 0xFFFF);
                if (notification == WM_LBUTTONUP)
                    ShowQuickControl();
                else if (notification == WM_LBUTTONDBLCLK)
                    ShowSettings();
                else if (notification == WM_RBUTTONUP)
                    ToggleTrayMenu();
                return IntPtr.Zero;
            case WM_HOTKEY:
                HandleHotkey((int)wParam.ToInt64());
                return IntPtr.Zero;
            case WM_CLOSE:
                if (!_exiting)
                {
                    AppWindow.Hide();
                    return IntPtr.Zero;
                }
                break;
        }
        return CallWindowProc(_oldProc, hwnd, msg, wParam, lParam);
    }

    private void ShowQuickControl()
    {
        if (_quickControl == null)
        {
            _quickControl = new QuickControlWindow();
            _quickControl.LevelChanged += (_, level) => ApplyLevel(level, showIndicator: false);
            _quickControl.SettingsRequested += (_, _) => ShowSettings();
        }
        _trayMenu?.HideMenu();
        _quickControl.ShowAt(BrightnessSlider.Value);
    }

    private void ToggleTrayMenu()
    {
        if (_trayMenu != null && _trayMenu.IsMenuVisible)
        {
            _trayMenu.HideMenu();
            return;
        }

        _quickControl?.HideHud();

        if (_trayMenu == null)
        {
            _trayMenu = new TrayMenuWindow();
            _trayMenu.OpenRequested += (_, _) => ShowSettings();
            _trayMenu.DimOffRequested += (_, _) => ApplyLevel(MaxBrightness, showIndicator: true);
            _trayMenu.StartupToggled += (_, _) => StartupSwitch.IsOn = !StartupSwitch.IsOn;
            _trayMenu.IndicatorToggled += (_, _) => IndicatorSwitch.IsOn = !IndicatorSwitch.IsOn;
            _trayMenu.ExitRequested += (_, _) => ExitApplication();
        }

        GetCursorPos(out var point);
        _trayMenu.ShowAt(point.x, point.y, StartupSwitch.IsOn, IndicatorSwitch.IsOn);
    }

    private void ExitApplication()
    {
        _exiting = true;
        Cleanup();
        Application.Current.Exit();
    }

    private void Cleanup()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;

        _saveTimer?.Stop();
        UnregisterAllHotkeys();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _osd.HideNow();
        _overlay?.Dispose();
        _overlay = null;
        SettingsStore.Save(_settings);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}



