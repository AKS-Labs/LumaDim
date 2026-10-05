using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Threading;

namespace LumaDim;

public partial class App : Application
{
    private const string MutexName = @"Local\LumaDim.SingleInstance";
    private const string ShowEventName = @"Local\LumaDim.ShowRequest";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEvent;
    private readonly bool _isFirstInstance;
    private DispatcherQueue? _dispatcherQueue;
    private Window? _window;

    public App()
    {
        Log.Write("app ctor");
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("domain unhandled: " + args.ExceptionObject);
        _instanceMutex = new Mutex(true, MutexName, out _isFirstInstance);
        InitializeComponent();
        Log.Write("app initialized first=" + _isFirstInstance);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Write("unhandled: " + e.Exception);
        e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Write("onLaunched");
        if (!_isFirstInstance)
        {
            try
            {
                using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                showEvent.Set();
            }
            catch { }
            Application.Current.Exit();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            static (state, _) =>
            {
                var app = (App)state!;
                app._dispatcherQueue?.TryEnqueue(() => (app._window as MainWindow)?.ShowSettings());
            },
            this,
            -1,
            false);

        Log.Write("creating window");
        try
        {
            _window = new MainWindow();
        }
        catch (System.Exception ex)
        {
            Log.Write("window ctor threw: " + ex);
            return;
        }
        Log.Write("window created");
        _window.Activate();
        Log.Write("activated");
        if (Array.Exists(Environment.GetCommandLineArgs(), argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)))
            _window.AppWindow.Hide();
    }
}
