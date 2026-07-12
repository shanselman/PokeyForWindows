using System.Threading;
using System.Windows;

namespace PokeyForWindows;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\PokeyForWindows.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (CursorRestoreWatchdog.IsWatchdogInvocation(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            CursorRestoreWatchdog.Run(e.Args, () => Dispatcher.Invoke(Shutdown));
            return;
        }

        if (UpdateRecoveryWatchdog.IsWatchdogInvocation(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            UpdateRecoveryWatchdog.Run(e.Args, () => Dispatcher.Invoke(Shutdown));
            return;
        }

        bool restarting = e.Args.Contains("--restarting", StringComparer.Ordinal);
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);
        _ownsSingleInstanceMutex = TryAcquireSingleInstanceMutex(restarting);
        if (!_ownsSingleInstanceMutex)
        {
            System.Windows.MessageBox.Show(
                "Pokey for Windows is already running in the notification area.",
                "Pokey for Windows",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _controller = new AppController();
            _controller.Start();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Startup failed.", ex);
            System.Windows.MessageBox.Show(
                $"Pokey for Windows could not start.\n\n{ex.Message}",
                "Pokey for Windows",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppDiagnostics.Error("Unhandled dispatcher exception.", e.Exception);
        SystemCursorController.RestoreSystemCursors();
        e.Handled = false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppDiagnostics.Error("Unhandled application exception.", ex);
        }

        SystemCursorController.RestoreSystemCursors();
    }

    private bool TryAcquireSingleInstanceMutex(bool restarting)
    {
        int attempts = restarting ? 40 : 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (_singleInstanceMutex!.WaitOne(restarting ? 250 : 0))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        return false;
    }
}
