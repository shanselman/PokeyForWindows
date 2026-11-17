namespace PokeyForWindows;

internal enum PointerTheme
{
    Light,
    Dark
}

internal sealed class AppSettings
{
    internal const double MinimumPointerLength = 160;
    internal const double MaximumPointerLength = 720;

    public bool Enabled { get; set; }
    public bool IsFirstRun { get; set; } = true;
    public bool AnimateOnClick { get; set; } = true;
    public PointerTheme PointerTheme { get; set; } = PointerTheme.Light;
    public double PointerLength { get; set; } = 400;
    public bool StartWithWindows { get; set; }
    public bool AutoCheckForUpdates { get; set; } = true;
    public bool ShowButton { get; set; } = true;
    public bool PinchAtEdges { get; set; } = true;

    internal void Normalize()
    {
        PointerLength = Math.Clamp(
            PointerLength,
            MinimumPointerLength,
            MaximumPointerLength);

        if (!Enum.IsDefined(PointerTheme))
        {
            PointerTheme = PointerTheme.Light;
        }
    }
}
