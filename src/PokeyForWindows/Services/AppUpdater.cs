using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace PokeyForWindows;

internal sealed class AppUpdater : IDisposable
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/digital-workshop/pokeyforwindows/releases/latest";
    private const string ExpectedDownloadPrefix =
        "https://github.com/digital-workshop/pokeyforwindows/";
    private const string ExpectedSignerSubstring = "Hanselman";
    private const bool RequireSignedUpdates = false;
    private const long MaximumDownloadBytes = 150L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TrayIconController _tray;
    private readonly Action _beforeRestart;
    private readonly Action _updateAborted;
    private readonly Action _shutdown;
    private int _checking;

    internal AppUpdater(
        TrayIconController tray,
        Action beforeRestart,
        Action updateAborted,
        Action shutdown)
    {
        _tray = tray;
        _beforeRestart = beforeRestart;
        _updateAborted = updateAborted;
        _shutdown = shutdown;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.Brotli |
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate
        });
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PokeyForWindows", GetCurrentVersion().ToString()));
    }

    internal static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    internal async Task CheckForUpdatesAsync(
        bool userInitiated,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _checking, 1) != 0)
        {
            if (userInitiated)
            {
                _tray.ShowInfo("Pokey for Windows", "An update check is already running.");
            }
            return;
        }

        try
        {
            ReleaseInfo release = await GetLatestReleaseAsync(cancellationToken);
            Version currentVersion = GetCurrentVersion();
            if (release.Version <= currentVersion)
            {
                if (userInitiated)
                {
                    _tray.ShowInfo(
                        "Pokey for Windows",
                        $"You already have the latest version ({currentVersion}).");
                }
                return;
            }

            MessageBoxResult choice = System.Windows.MessageBox.Show(
                $"Pokey for Windows {release.Version} is available.\n\n" +
                "Download, verify, install, and restart now?",
                "Pokey for Windows update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            await DownloadAndInstallAsync(release, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppDiagnostics.Info("Update check was cancelled.");
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            JsonException or
            IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            Win32Exception or
            CryptographicException or
            InvalidOperationException or
            NotSupportedException)
        {
            AppDiagnostics.Error("Update failed.", ex);
            if (userInitiated)
            {
                _tray.ShowError("Update failed", ex.Message);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    internal static void CleanupPreviousUpdate()
    {
        string executablePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        string oldPath = executablePath + ".old.exe";
        string newPath = executablePath + ".new.exe";
        TryDelete(oldPath);
        TryDelete(newPath);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<ReleaseInfo> GetLatestReleaseAsync(
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            LatestReleaseApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("The release tag is missing.");
        Version version = ParseVersion(tag);
        string architectureSuffix = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64.zip",
            Architecture.Arm64 => "win-arm64.zip",
            _ => throw new NotSupportedException(
                $"Updates are not available for {RuntimeInformation.ProcessArchitecture}.")
        };

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(architectureSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string url = asset.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidDataException("The release download URL is missing.");
            ValidateDownloadUrl(url);
            return new ReleaseInfo(version, name, url);
        }

        throw new InvalidDataException(
            $"Release {version} has no {architectureSuffix} asset.");
    }

    private async Task DownloadAndInstallAsync(
        ReleaseInfo release,
        CancellationToken cancellationToken)
    {
        string temporaryZip = Path.Combine(
            Path.GetTempPath(),
            $"PokeyForWindows-update-{Guid.NewGuid():N}.zip");
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is unavailable.");
        string newExecutablePath = executablePath + ".new.exe";
        string oldExecutablePath = executablePath + ".old.exe";
        bool runtimeStopped = false;
        bool restartLaunched = false;

        try
        {
            await DownloadAsync(release.DownloadUrl, temporaryZip, cancellationToken);
            ExtractExecutable(temporaryZip, newExecutablePath);
            // Fork builds are unsigned: trust is limited to HTTPS downloads from the
            // fork's own GitHub releases (see ExpectedDownloadPrefix).
            if (RequireSignedUpdates)
            {
                AuthenticodeVerifier.Verify(newExecutablePath, ExpectedSignerSubstring);
            }

            EnsureDirectoryWritable(executablePath);

            _beforeRestart();
            runtimeStopped = true;
            using Process updateWatchdog = UpdateRecoveryWatchdog.Start(
                Environment.ProcessId,
                executablePath,
                oldExecutablePath);
            File.Move(executablePath, oldExecutablePath, true);
            try
            {
                File.Move(newExecutablePath, executablePath);
                ProcessStartInfo startInfo = new(executablePath)
                {
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("--restarting");
                _ = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException(
                        "The updated application did not restart.");
                restartLaunched = true;
            }
            catch
            {
                if (File.Exists(oldExecutablePath))
                {
                    TryDelete(executablePath);
                    File.Move(oldExecutablePath, executablePath, true);
                }
                throw;
            }

            _shutdown();
        }
        catch
        {
            if (runtimeStopped && !restartLaunched)
            {
                _updateAborted();
            }
            throw;
        }
        finally
        {
            TryDelete(temporaryZip);
            TryDelete(newExecutablePath);
        }
    }

    private async Task DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The update exceeds the download size limit.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidDataException("The update exceeds the download size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ExtractExecutable(string zipPath, string destination)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(
            candidate => string.Equals(
                Path.GetFileName(candidate.FullName),
                "PokeyForWindows.exe",
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "The update archive does not contain PokeyForWindows.exe.");

        if (entry.Length <= 0 || entry.Length > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The update executable has an invalid size.");
        }

        TryDelete(destination);
        entry.ExtractToFile(destination);
    }

    private static void EnsureDirectoryWritable(string executablePath)
    {
        string directory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException(
                "The application directory is unavailable.");
        string probe = Path.Combine(directory, $".pokey-write-{Guid.NewGuid():N}");
        using (File.Create(probe))
        {
        }
        File.Delete(probe);
    }

    internal static Version ParseVersion(string tag)
    {
        string normalized = tag.Trim().TrimStart('v', 'V');
        string numeric = normalized.Split('-', 2)[0];
        if (!Version.TryParse(numeric, out Version? version))
        {
            throw new InvalidDataException($"Release tag '{tag}' is not a version.");
        }

        return version;
    }

    internal static void ValidateDownloadUrl(string url)
    {
        if (!url.StartsWith(ExpectedDownloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update URL is not an approved GitHub URL.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            AppDiagnostics.Error($"Could not delete stale update file '{path}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppDiagnostics.Error($"Could not delete stale update file '{path}'.", ex);
        }
    }

    private sealed record ReleaseInfo(
        Version Version,
        string AssetName,
        string DownloadUrl);
}
