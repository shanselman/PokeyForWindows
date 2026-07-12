using System.ComponentModel;
using System.Windows.Interop;

namespace PokeyForWindows;

internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x504B;

    private readonly HwndSource _source;
    private bool _registered;

    internal GlobalHotkey()
    {
        HwndSourceParameters parameters = new("PokeyForWindows.Hotkey")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0,
            ExtendedWindowStyle = (int)NativeMethods.WsExToolWindow
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    internal event Action? Pressed;

    internal bool IsRegistered => _registered;

    internal bool TryRegister(out string? error)
    {
        if (_registered)
        {
            error = null;
            return true;
        }

        uint modifiers =
            NativeMethods.ModControl |
            NativeMethods.ModAlt |
            NativeMethods.ModNoRepeat;

        _registered = NativeMethods.RegisterHotKey(
            _source.Handle,
            HotkeyId,
            modifiers,
            NativeMethods.VkP);

        if (_registered)
        {
            error = null;
            return true;
        }

        int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        error = new Win32Exception(code).Message;
        return false;
    }

    public void Dispose()
    {
        if (_registered)
        {
            _ = NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        GC.SuppressFinalize(this);
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmHotKey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }
}
