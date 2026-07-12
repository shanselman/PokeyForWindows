using System.Diagnostics;

namespace PokeyForWindows;

internal static class CursorRestoreWatchdog
{
    private const string WatchdogArgument = "--cursor-watchdog";

    internal static bool IsWatchdogInvocation(string[] arguments) =>
        arguments.Length == 3 &&
        string.Equals(arguments[0], WatchdogArgument, StringComparison.Ordinal);

    internal static void Run(string[] arguments, Action completed)
    {
        if (!int.TryParse(arguments[1], out int parentProcessId))
        {
            SystemCursorController.RestoreSystemCursors();
            completed();
            return;
        }

        string stopEventName = arguments[2];
        _ = Task.Run(() =>
        {
            try
            {
                using EventWaitHandle stopEvent =
                    EventWaitHandle.OpenExisting(stopEventName);
                using Process parent = Process.GetProcessById(parentProcessId);

                while (!stopEvent.WaitOne(250))
                {
                    if (parent.HasExited)
                    {
                        SystemCursorController.RestoreSystemCursors();
                        break;
                    }
                }
            }
            catch (ArgumentException)
            {
                SystemCursorController.RestoreSystemCursors();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                SystemCursorController.RestoreSystemCursors();
            }
            finally
            {
                completed();
            }
        });
    }

    internal static Process Start(int parentProcessId, string stopEventName)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is unavailable.");

        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(WatchdogArgument);
        startInfo.ArgumentList.Add(parentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(stopEventName);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The cursor watchdog did not start.");
    }
}
