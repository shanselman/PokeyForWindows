using System.ComponentModel;
using System.Diagnostics;

namespace PokeyForWindows;

internal sealed class SystemCursorController : IDisposable
{
    private const int CursorWidth = 32;
    private const int CursorHeight = 32;

    private static readonly uint[] SystemCursorIds =
    [
        32512, // Normal
        32513, // I-beam
        32514, // Wait
        32515, // Cross
        32516, // Up arrow
        32640, // Size
        32641, // Icon
        32642, // NW/SE
        32643, // NE/SW
        32644, // W/E
        32645, // N/S
        32646, // Move
        32648, // Unavailable
        32649, // Hand
        32650, // App starting
        32651, // Help
        32671, // Pin
        32672  // Person
    ];

    // Resize and move cursors: the hand pinches while one of these would be shown.
    private static readonly uint[] GrabCursorIds =
    [
        32640, // Size
        32642, // NW/SE
        32643, // NE/SW
        32644, // W/E
        32645, // N/S
        32646  // Move
    ];

    private readonly HashSet<IntPtr> _grabHandles = [];
    private EventWaitHandle? _watchdogStopEvent;
    private Process? _watchdogProcess;
    private bool _hidden;

    internal bool IsHidden => _hidden;

    internal void Hide()
    {
        if (_hidden)
        {
            return;
        }

        string stopEventName =
            $@"Local\PokeyForWindows.CursorWatchdog.{Environment.ProcessId}.{Guid.NewGuid():N}";
        _watchdogStopEvent = new EventWaitHandle(
            false,
            EventResetMode.ManualReset,
            stopEventName);
        _watchdogProcess = CursorRestoreWatchdog.Start(
            Environment.ProcessId,
            stopEventName);

        try
        {
            byte[] andMask = Enumerable.Repeat(
                (byte)0xFF,
                CursorWidth * CursorHeight / 8).ToArray();
            byte[] xorMask = new byte[CursorWidth * CursorHeight / 8];

            foreach (uint cursorId in SystemCursorIds)
            {
                if (NativeMethods.LoadCursor(
                        IntPtr.Zero,
                        new IntPtr(unchecked((int)cursorId))) == IntPtr.Zero)
                {
                    AppDiagnostics.Info(
                        $"System cursor {cursorId} is unavailable and was skipped.");
                    continue;
                }

                IntPtr transparentCursor = NativeMethods.CreateCursor(
                    IntPtr.Zero,
                    0,
                    0,
                    CursorWidth,
                    CursorHeight,
                    andMask,
                    xorMask);

                if (transparentCursor == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "Could not create a transparent cursor.");
                }

                if (!NativeMethods.SetSystemCursor(transparentCursor, cursorId))
                {
                    _ = NativeMethods.DestroyCursor(transparentCursor);
                    throw new Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        $"Could not replace system cursor {cursorId}.");
                }
            }

            _grabHandles.Clear();
            foreach (uint cursorId in GrabCursorIds)
            {
                IntPtr handle = NativeMethods.LoadCursor(
                    IntPtr.Zero,
                    new IntPtr(unchecked((int)cursorId)));
                if (handle != IntPtr.Zero)
                {
                    _grabHandles.Add(handle);
                }
            }

            _hidden = true;
        }
        catch
        {
            Restore();
            throw;
        }
    }

    /// <summary>True while Windows is showing a resize or move cursor.</summary>
    internal bool IsGrabCursorActive()
    {
        if (!_hidden || _grabHandles.Count == 0)
        {
            return false;
        }

        NativeMethods.CursorInfo info = new()
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CursorInfo>()
        };
        return NativeMethods.GetCursorInfo(ref info) && _grabHandles.Contains(info.Cursor);
    }

    internal void Restore()
    {
        _grabHandles.Clear();
        _watchdogStopEvent?.Set();
        _watchdogStopEvent?.Dispose();
        _watchdogStopEvent = null;

        _watchdogProcess?.Dispose();
        _watchdogProcess = null;

        if (!RestoreSystemCursors())
        {
            AppDiagnostics.Error(
                $"Could not restore the system cursor scheme. Win32 error " +
                $"{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
        }

        _hidden = false;
    }

    internal static bool RestoreSystemCursors() =>
        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetCursors,
            0,
            IntPtr.Zero,
            0);

    public void Dispose()
    {
        Restore();
        GC.SuppressFinalize(this);
    }
}
