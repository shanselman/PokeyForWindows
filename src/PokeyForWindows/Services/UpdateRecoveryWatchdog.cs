using System.Diagnostics;
using System.IO;

namespace PokeyForWindows;

internal static class UpdateRecoveryWatchdog
{
    private const string WatchdogArgument = "--update-watchdog";
    private const string WatchdogFilePrefix = "PokeyForWindows-watchdog-";

    /// <summary>Removes watchdog copies left in the temp folder by earlier updates.</summary>
    internal static void CleanupStaleCopies()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         Path.GetTempPath(),
                         WatchdogFilePrefix + "*.exe"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still running or locked: leave it for the next start.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Info("Could not clean stale update watchdog copies: " + ex.Message);
        }
    }

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
        // Run the watchdog from a temporary copy: the real executable is renamed while
        // the update is installed, which breaks a single-file host that is still starting.
        string watchdogPath = Path.Combine(
            Path.GetTempPath(),
            $"{WatchdogFilePrefix}{Guid.NewGuid():N}.exe");
        File.Copy(executablePath, watchdogPath, overwrite: false);

        ProcessStartInfo startInfo = new(watchdogPath)
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
