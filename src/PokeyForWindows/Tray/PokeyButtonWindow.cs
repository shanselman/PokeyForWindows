using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace PokeyForWindows;

/// <summary>
/// Small always-on-top button in the bottom-left corner of the primary
/// monitor: the "Pokey" part toggles the pointer, the gear opens settings.
/// </summary>
internal sealed class PokeyButtonWindow : Window
{
    private const double EdgeMargin = 12;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private static readonly System.Windows.Media.Brush OnBrush = Freeze(
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71)));
    private static readonly System.Windows.Media.Brush OffBrush = Freeze(
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9A, 0x9A)));

    private static readonly ControlTemplate FlatTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
        "<Border x:Name='bg' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}'>" +
        "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
        "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
        "<Setter TargetName='bg' Property='Background' Value='#33FFFFFF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");

    private readonly System.Windows.Shapes.Ellipse _statusDot;

    internal PokeyButtonWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Pokey button";

        _statusDot = new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = OffBrush,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        StackPanel toggleContent = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
        toggleContent.Children.Add(_statusDot);
        toggleContent.Children.Add(new TextBlock
        {
            Text = "Pokey",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        System.Windows.Controls.Button toggleButton = CreateButton(toggleContent, "Toggle Pokey pointer");
        toggleButton.Click += (_, _) => ToggleRequested?.Invoke();

        System.Windows.Controls.Button settingsButton = CreateButton(
            CreateLucideSettingsIcon(),
            "Open Pokey settings");
        settingsButton.Margin = new Thickness(4, 0, 0, 0);
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();

        StackPanel row = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(toggleButton);
        row.Children.Add(settingsButton);

        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Opacity = 0.85,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
            Child = row
        };

        SourceInitialized += OnSourceInitialized;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SizeChanged += (_, _) => PositionBottomLeft();
        Loaded += (_, _) => PositionBottomLeft();
    }

    internal event Action? ToggleRequested;
    internal event Action? SettingsRequested;

    internal void SetPointerEnabled(bool enabled) =>
        _statusDot.Fill = enabled ? OnBrush : OffBrush;

    internal void ShowButton()
    {
        if (!IsVisible)
        {
            Show();
        }

        PositionBottomLeft();
        Topmost = false;
        Topmost = true;
    }

    internal void HideButton()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private static System.Windows.Controls.Button CreateButton(object content, string name)
    {
        System.Windows.Controls.Button button = new()
        {
            Content = content,
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 28,
            Foreground = System.Windows.Media.Brushes.White,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Template = FlatTemplate
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    // Lucide "settings" icon (https://lucide.dev, ISC license), 24x24 stroke icon.
    private static UIElement CreateLucideSettingsIcon()
    {
        System.Windows.Media.Brush stroke = System.Windows.Media.Brushes.White;
        Canvas canvas = new() { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(
                "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 " +
                "2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 " +
                "2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051 " +
                "a2.34 2.34 0 0 0 3.319-1.915"),
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        System.Windows.Shapes.Ellipse hub = new()
        {
            Width = 6,
            Height = 6,
            Stroke = stroke,
            StrokeThickness = 2
        };
        Canvas.SetLeft(hub, 9);
        Canvas.SetTop(hub, 9);
        canvas.Children.Add(hub);
        return new Viewbox { Width = 16, Height = 16, Child = canvas };
    }

    private static System.Windows.Media.Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void PositionBottomLeft()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + EdgeMargin;
        Top = workArea.Bottom - ActualHeight - EdgeMargin;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or
            nameof(SystemParameters.PrimaryScreenWidth) or
            nameof(SystemParameters.PrimaryScreenHeight))
        {
            PositionBottomLeft();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        _ = SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
