using System.Diagnostics;
using System.IO;

namespace PokeyForWindows;

internal static class UpdateRecoveryWatchdog
{
    private const string WatchdogArgument = "--update-watchdog";

    internal static bool IsWatchdogInvocation(string[] arguments) =>
        arguments.Length == 4 &&
        string.Equals(arguments[0], WatchdogArgument, StringComparison.Ordinal);

    internal static void Run(string[] arguments, Action completed)
    {
        if (!int.TryParse(arguments[1], out int parentProcessId))
        {
            completed();
            return;
        }

        string executablePath = arguments[2];
        string backupPath = arguments[3];
        _ = Task.Run(() =>
        {
            try
            {
                using Process parent = Process.GetProcessById(parentProcessId);
                parent.WaitForExit();

                if (!File.Exists(executablePath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, executablePath);
                }
            }
            catch (ArgumentException)
            {
                if (!File.Exists(executablePath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, executablePath);
                }
            }
            catch (IOException ex)
            {
                AppDiagnostics.Error("The update recovery watchdog could not restore the executable.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppDiagnostics.Error("The update recovery watchdog could not restore the executable.", ex);
            }
            finally
            {
                completed();
            }
        });
    }

    internal static Process Start(
        int parentProcessId,
        string executablePath,
        string backupPath)
    {
        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(WatchdogArgument);
        startInfo.ArgumentList.Add(parentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(backupPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The update recovery watchdog did not start.");
    }
}
