using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace PokeyForWindows;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _allowClose;

    internal SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Closing += OnClosing;
        RefreshControls();
    }

    internal event Action? ToggleRequested;
    internal event Action? SettingsChanged;
    internal event Action? UpdateRequested;

    internal void RefreshControls()
    {
        ToggleButton.Content = _settings.Enabled
            ? "Turn Pokey off"
            : "Turn Pokey on";
        LightPointerRadio.IsChecked = _settings.PointerTheme == PointerTheme.Light;
        DarkPointerRadio.IsChecked = _settings.PointerTheme == PointerTheme.Dark;
        PointerLengthSlider.Value = _settings.PointerLength;
        AnimateOnClickCheckBox.IsChecked = _settings.AnimateOnClick;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        AutoUpdateCheckBox.IsChecked = _settings.AutoCheckForUpdates;
    }

    internal void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleRequested?.Invoke();
        RefreshControls();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.PointerTheme = DarkPointerRadio.IsChecked == true
            ? PointerTheme.Dark
            : PointerTheme.Light;
        _settings.PointerLength = PointerLengthSlider.Value;
        _settings.AnimateOnClick = AnimateOnClickCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.AutoCheckForUpdates = AutoUpdateCheckBox.IsChecked == true;
        SettingsChanged?.Invoke();
        Hide();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshControls();
        Hide();
    }

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        UpdateRequested?.Invoke();

    private void Hyperlink_RequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}