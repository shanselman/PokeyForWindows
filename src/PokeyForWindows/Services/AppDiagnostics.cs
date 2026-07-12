using System.Diagnostics;

namespace PokeyForWindows;

internal static class AppDiagnostics
{
    internal static void Info(string message) => Write("INFO", message);

    internal static void Error(string message, Exception? exception = null)
    {
        string detail = exception is null
            ? message
            : $"{message} {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", detail);
    }

    private static void Write(string level, string message)
    {
        string line = $"[PokeyForWindows] [{level}] {message}";
        Trace.WriteLine(line);
        NativeMethods.OutputDebugString(line);
    }
}
