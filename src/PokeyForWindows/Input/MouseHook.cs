using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;

namespace PokeyForWindows;

internal sealed class MouseHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _hook;

    internal MouseHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = HookCallback;
    }

    internal event Action<long>? PrimaryButtonPressed;

    internal void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        IntPtr module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _callback,
            module,
            0);

        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "Could not install the mouse observation hook.");
        }
    }

    internal void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        IntPtr hook = _hook;
        _hook = IntPtr.Zero;
        if (!NativeMethods.UnhookWindowsHookEx(hook))
        {
            AppDiagnostics.Error(
                $"Could not remove the mouse hook. Win32 error " +
                $"{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt64() == NativeMethods.WmLButtonDown)
        {
            long timestamp = Stopwatch.GetTimestamp();
            _ = _dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () => PrimaryButtonPressed?.Invoke(timestamp));
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
