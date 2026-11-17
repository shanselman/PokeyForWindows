using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PokeyForWindows;

internal sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly OverlayController _overlays;
    private readonly SystemCursorController _systemCursor = new();
    private readonly MouseHook _mouseHook;
    private readonly GlobalHotkey _hotkey = new();
    private readonly TapSeriesController _tapSeries = new();
    private readonly TrayIconController _tray;
    private readonly SettingsWindow _settingsWindow;
    private readonly PokeyButtonWindow _button = new();
    private readonly AppUpdater _updater;
    private readonly CancellationTokenSource _lifetime = new();

    private bool _runtimeActive;
    private bool _suspended;
    private bool _disposed;

    internal AppController()
    {
        AppUpdater.CleanupPreviousUpdate();
        _settings = _settingsStore.Load();
        _overlays = new OverlayController(_settings);
        _mouseHook = new MouseHook(System.Windows.Application.Current.Dispatcher);
        _overlays.GrabCursorProbe = _systemCursor.IsGrabCursorActive;
        _tray = new TrayIconController(_settings);
        _settingsWindow = new SettingsWindow(_settings);
        _updater = new AppUpdater(
            _tray,
            DeactivateForShutdown,
            ResumeAfterFailedUpdate,
            () => System.Windows.Application.Current.Dispatcher.Invoke(
                System.Windows.Application.Current.Shutdown));
    }

    internal void Start()
    {
        WireEvents();
        SettingsStore.SetAutoStart(_settings.StartWithWindows);

        if (!_hotkey.TryRegister(out string? hotkeyError))
        {
            _tray.ShowError(
                "Shortcut unavailable",
                $"Ctrl+Alt+P could not be registered: {hotkeyError}");
        }

        if (_settings.IsFirstRun)
        {
            _settings.IsFirstRun = false;
            _settings.Enabled = false;
            SaveSettings();
            _tray.ShowInfo(
                "Pokey for Windows is ready",
                "Click the tray icon or press Ctrl+Alt+P to turn the pointer on.");
            ShowSettings();
        }
        else if (_settings.Enabled)
        {
            SetEnabled(true, persist: false);
        }

        ApplyButtonVisibility();

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.ShowInTaskbarMinimized();
        }

        if (_settingsStore.RecoveryNotice is not null)
        {
            _tray.ShowError("Settings were reset", _settingsStore.RecoveryNotice);
        }

        if (_settings.AutoCheckForUpdates)
        {
            _ = AutoCheckForUpdatesAsync(_lifetime.Token);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        UnwireEvents();
        DeactivateForShutdown();
        SaveSettings();
        _updater.Dispose();
        _settingsWindow.CloseForExit();
        _button.Close();
        _tray.Dispose();
        _hotkey.Dispose();
        _mouseHook.Dispose();
        _systemCursor.Dispose();
        _overlays.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private void WireEvents()
    {
        _hotkey.Pressed += ToggleEnabled;
        _mouseHook.PrimaryButtonPressed += OnPrimaryButtonPressed;
        _tray.ToggleRequested += ToggleEnabled;
        _tray.AnimateOnClickRequested += ToggleAnimateOnClick;
        _tray.StartWithWindowsRequested += ToggleStartWithWindows;
        _tray.AutoCheckRequested += ToggleAutoUpdate;
        _tray.SettingsRequested += ShowSettings;
        _tray.UpdateRequested += CheckForUpdates;
        _tray.ExitRequested += Exit;
        _settingsWindow.ToggleRequested += ToggleEnabled;
        _button.ToggleRequested += ToggleEnabled;
        _button.SettingsRequested += ShowSettings;
        _settingsWindow.SettingsChanged += ApplySettings;
        _settingsWindow.UpdateRequested += CheckForUpdates;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void UnwireEvents()
    {
        _hotkey.Pressed -= ToggleEnabled;
        _mouseHook.PrimaryButtonPressed -= OnPrimaryButtonPressed;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void ToggleEnabled() => SetEnabled(!_settings.Enabled, persist: true);

    private void SetEnabled(bool enabled, bool persist)
    {
        if (enabled == _runtimeActive && enabled == _settings.Enabled)
        {
            return;
        }

        if (enabled)
        {
            try
            {
                _overlays.Start();
                _mouseHook.Start();
                _systemCursor.Hide();
                _runtimeActive = true;
                _settings.Enabled = true;
                _tapSeries.Reset();
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                System.ComponentModel.Win32Exception)
            {
                _mouseHook.Stop();
                _systemCursor.Restore();
                _overlays.Stop();
                _runtimeActive = false;
                _settings.Enabled = false;
                AppDiagnostics.Error("Could not enable Pokey.", ex);
                _tray.ShowError("Could not enable Pokey", ex.Message);
            }
        }
        else
        {
            _settings.Enabled = false;
            DeactivateRuntime();
        }

        if (persist)
        {
            SaveSettings();
        }

        _tray.ApplySettings(_settings);
        _settingsWindow.RefreshControls();
        _button.SetPointerEnabled(_settings.Enabled);
    }

    private void DeactivateRuntime()
    {
        _mouseHook.Stop();
        _systemCursor.Restore();
        _overlays.Stop();
        _tapSeries.Reset();
        _runtimeActive = false;
    }

    private void DeactivateForShutdown()
    {
        bool rememberedEnabled = _settings.Enabled;
        DeactivateRuntime();
        _settings.Enabled = rememberedEnabled;
    }

    private void ResumeAfterFailedUpdate()
    {
        if (_settings.Enabled && !_runtimeActive)
        {
            SetEnabled(true, persist: false);
        }
    }

    private void OnPrimaryButtonPressed(long timestamp)
    {
        if (!_runtimeActive || !_settings.AnimateOnClick)
        {
            return;
        }

        TapEffect effect = _tapSeries.RecordTap(timestamp);
        _overlays.Trigger(effect);
    }

    private void ToggleAnimateOnClick()
    {
        _settings.AnimateOnClick = !_settings.AnimateOnClick;
        ApplySettings();
    }

    private void ToggleStartWithWindows()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        ApplySettings();
    }

    private void ToggleAutoUpdate()
    {
        _settings.AutoCheckForUpdates = !_settings.AutoCheckForUpdates;
        ApplySettings();
    }

    private void ApplySettings()
    {
        try
        {
            SettingsStore.SetAutoStart(_settings.StartWithWindows);
            _overlays.ApplySettings(_settings);
            ApplyButtonVisibility();
            SaveSettings();
            _tray.ApplySettings(_settings);
            _settingsWindow.RefreshControls();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            AppDiagnostics.Error("Could not apply settings.", ex);
            _tray.ShowError("Settings were not saved", ex.Message);
        }
    }

    private void ApplyButtonVisibility()
    {
        _button.SetPointerEnabled(_settings.Enabled);
        if (_settings.ShowButton)
        {
            _button.ShowButton();
        }
        else
        {
            _button.HideButton();
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppDiagnostics.Error("Could not save settings.", ex);
            _tray.ShowError("Settings were not saved", ex.Message);
        }
    }

    private void ShowSettings()
    {
        _settingsWindow.RefreshControls();
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void CheckForUpdates() =>
        _ = _updater.CheckForUpdatesAsync(userInitiated: true, _lifetime.Token);

    private void Exit() => System.Windows.Application.Current.Shutdown();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_runtimeActive)
            {
                _overlays.RefreshDisplays();
            }
        });
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock && _runtimeActive)
            {
                _suspended = true;
                DeactivateRuntime();
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock &&
                     _suspended &&
                     _settings.Enabled)
            {
                _suspended = false;
                SetEnabled(true, persist: false);
            }
        });
    }

    private async Task AutoCheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await _updater.CheckForUpdatesAsync(
                userInitiated: false,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppDiagnostics.Info("Automatic update check was cancelled.");
        }
    }
}
