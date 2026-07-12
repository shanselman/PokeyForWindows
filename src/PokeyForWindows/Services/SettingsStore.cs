using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PokeyForWindows;

internal sealed class SettingsStore
{
    private const string AutoStartRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "PokeyForWindows";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    internal string? RecoveryNotice { get; private set; }

    internal SettingsStore()
        : this(GetDefaultSettingsPath())
    {
    }

    internal SettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    private static string GetDefaultSettingsPath()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeyForWindows");
        return Path.Combine(directory, "settings.json");
    }

    internal AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("The settings file contains no settings.");
            settings.Normalize();
            return settings;
        }
        catch (JsonException ex)
        {
            return RecoverCorruptSettings(ex);
        }
        catch (InvalidDataException ex)
        {
            return RecoverCorruptSettings(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Could not read settings from '{_settingsPath}'.",
                ex);
        }
    }

    internal void Save(AppSettings settings)
    {
        settings.Normalize();
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The settings directory is unavailable.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }

    internal static void SetAutoStart(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(
            AutoStartRegistryPath,
            writable: true);

        if (enabled)
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The executable path is unavailable.");
            key.SetValue(AutoStartValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    private AppSettings RecoverCorruptSettings(Exception cause)
    {
        string quarantinedPath =
            $"{_settingsPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
        try
        {
            File.Move(_settingsPath, quarantinedPath, true);
            RecoveryNotice =
                $"Invalid settings were moved to '{Path.GetFileName(quarantinedPath)}'. " +
                "Defaults were restored.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecoveryNotice =
                "Invalid settings could not be moved aside because the file is in use. " +
                "Defaults were restored for this session.";
            AppDiagnostics.Error("Could not quarantine invalid settings.", ex);
        }

        AppDiagnostics.Error(RecoveryNotice, cause);
        return new AppSettings();
    }
}
