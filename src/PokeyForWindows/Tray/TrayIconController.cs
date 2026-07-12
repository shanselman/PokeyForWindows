using System.Drawing;
using System.Windows.Forms;

namespace PokeyForWindows;

internal sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _animateItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _autoUpdateItem;

    internal TrayIconController(AppSettings settings)
    {
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
            ?? (Icon)SystemIcons.Information.Clone();

        ContextMenuStrip menu = new();
        _enabledItem = new ToolStripMenuItem(
            "Enabled",
            image: null,
            (_, _) => ToggleRequested?.Invoke());
        _animateItem = new ToolStripMenuItem(
            "Animate on click",
            image: null,
            (_, _) => AnimateOnClickRequested?.Invoke());
        _startupItem = new ToolStripMenuItem(
            "Start with Windows",
            image: null,
            (_, _) => StartWithWindowsRequested?.Invoke());
        _autoUpdateItem = new ToolStripMenuItem(
            "Auto-check for updates",
            image: null,
            (_, _) => AutoCheckRequested?.Invoke());

        menu.Items.Add(_enabledItem);
        menu.Items.Add(_animateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(_autoUpdateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add("Check for updates", null, (_, _) => UpdateRequested?.Invoke());
        menu.Items.Add("About Pokey for Windows", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _icon,
            Text = "Pokey for Windows",
            Visible = true
        };
        _notifyIcon.MouseClick += OnMouseClick;
        ApplySettings(settings);
    }

    internal event Action? ToggleRequested;
    internal event Action? AnimateOnClickRequested;
    internal event Action? StartWithWindowsRequested;
    internal event Action? AutoCheckRequested;
    internal event Action? SettingsRequested;
    internal event Action? UpdateRequested;
    internal event Action? ExitRequested;

    internal void ApplySettings(AppSettings settings)
    {
        _enabledItem.Checked = settings.Enabled;
        _animateItem.Checked = settings.AnimateOnClick;
        _startupItem.Checked = settings.StartWithWindows;
        _autoUpdateItem.Checked = settings.AutoCheckForUpdates;
        _notifyIcon.Text = settings.Enabled
            ? "Pokey for Windows — on"
            : "Pokey for Windows — off";
    }

    internal void ShowInfo(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }

    internal void ShowError(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
        _notifyIcon.ShowBalloonTip(7000);
    }

    public void Dispose()
    {
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleRequested?.Invoke();
        }
    }
}
