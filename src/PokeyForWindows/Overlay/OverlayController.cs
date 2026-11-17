using System.Windows.Forms;
using System.Windows.Media;

namespace PokeyForWindows;

internal sealed class OverlayController : IDisposable
{
    private readonly List<OverlayWindow> _windows = [];
    private AppSettings _settings;
    private bool _running;

    internal Func<bool>? GrabCursorProbe { get; set; }

    internal OverlayController(AppSettings settings)
    {
        _settings = settings;
    }

    internal void Start()
    {
        if (_running)
        {
            return;
        }

        try
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                OverlayWindow window = new(screen, _settings);
                _windows.Add(window);
                window.Show();
            }

            CompositionTarget.Rendering += OnRendering;
            _running = true;
        }
        catch
        {
            foreach (OverlayWindow window in _windows)
            {
                window.Close();
            }

            _windows.Clear();
            throw;
        }
    }

    internal void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }

        foreach (OverlayWindow window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }

    internal void RefreshDisplays()
    {
        if (!_running)
        {
            return;
        }

        Stop();
        Start();
    }

    internal void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        foreach (OverlayWindow window in _windows)
        {
            window.ApplySettings(settings);
        }
    }

    internal void Trigger(TapEffect effect)
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.Point point))
        {
            AppDiagnostics.Error(
                $"GetCursorPos failed while triggering an animation. Win32 error " +
                $"{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
            return;
        }

        _windows.FirstOrDefault(window => window.ContainsPhysicalPoint(point))
            ?.Trigger(effect, point);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.Point point))
        {
            return;
        }

        bool pinched = _settings.PinchAtEdges && GrabCursorProbe?.Invoke() == true;
        foreach (OverlayWindow window in _windows)
        {
            window.UpdatePointer(point, visible: true);
            window.SetPinched(pinched);
        }
    }
}
