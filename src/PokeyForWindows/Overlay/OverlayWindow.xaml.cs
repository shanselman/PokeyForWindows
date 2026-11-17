using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace PokeyForWindows;

public partial class OverlayWindow : Window
{
    private const double PointerAnchorX = 18;
    private const double PointerAnchorY = 2;

    private readonly Screen _screen;
    private AppSettings _settings;
    private IntPtr _windowHandle;
    private bool _pinched;
    private Brush _handFill = Brushes.White;

    internal OverlayWindow(Screen screen, AppSettings settings)
    {
        _screen = screen;
        _settings = settings;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        ApplySettings(settings);
    }

    internal bool ContainsPhysicalPoint(NativeMethods.Point point) =>
        _screen.Bounds.Contains(point.X, point.Y);

    internal void UpdatePointer(NativeMethods.Point point, bool visible)
    {
        if (!visible || !ContainsPhysicalPoint(point))
        {
            PointerRoot.Visibility = Visibility.Collapsed;
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double localX = (point.X - _screen.Bounds.Left) / dpi.DpiScaleX;
        double localY = (point.Y - _screen.Bounds.Top) / dpi.DpiScaleY;

        Canvas.SetLeft(PointerRoot, localX - PointerAnchorX);
        Canvas.SetTop(PointerRoot, localY - PointerAnchorY);
        PointerRoot.Visibility = Visibility.Visible;
    }

    internal void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        PointerStick.Height = settings.PointerLength;

        bool light = settings.PointerTheme == PointerTheme.Light;
        Brush handFill = light ? Brushes.White : new SolidColorBrush(Color.FromRgb(28, 28, 28));
        Brush contrasting = light ? Brushes.Black : Brushes.White;

        if (handFill.CanFreeze)
        {
            handFill.Freeze();
        }

        _handFill = handFill;
        PointerHand.Fill = _pinched ? PinchBrush : handFill;
        PointerHand.Stroke = contrasting;
        PrimaryFlash.Fill = contrasting;
        SecondaryFlash.Fill = contrasting;

        LinearGradientBrush stickBrush = new(
            contrasting is SolidColorBrush solid ? solid.Color : Colors.Black,
            Colors.Transparent,
            new Point(0.5, 0),
            new Point(0.5, 1));
        stickBrush.GradientStops.Clear();
        Color stickColor = light ? Colors.Black : Colors.White;
        stickBrush.GradientStops.Add(new GradientStop(stickColor, 0));
        stickBrush.GradientStops.Add(new GradientStop(Color.FromArgb(180, stickColor.R, stickColor.G, stickColor.B), 0.72));
        stickBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, stickColor.R, stickColor.G, stickColor.B), 1));
        stickBrush.Freeze();
        PointerStick.Background = stickBrush;
    }

    private static readonly Brush PinchBrush = CreatePinchBrush();

    private static Brush CreatePinchBrush()
    {
        SolidColorBrush brush = new(Color.FromRgb(255, 176, 32));
        brush.Freeze();
        return brush;
    }

    /// <summary>Squeezes the hand like a pinch while a resize/move cursor would show.</summary>
    internal void SetPinched(bool pinched)
    {
        if (pinched == _pinched)
        {
            return;
        }

        _pinched = pinched;
        PointerHand.Fill = pinched ? PinchBrush : _handFill;
        Duration duration = new(TimeSpan.FromMilliseconds(110));
        IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PinchScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(pinched ? 0.58 : 1, duration) { EasingFunction = ease });
        PinchScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(pinched ? 0.86 : 1, duration) { EasingFunction = ease });
    }

    internal void Trigger(TapEffect effect, NativeMethods.Point point)
    {
        if (!_settings.AnimateOnClick || !ContainsPhysicalPoint(point))
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double localX = (point.X - _screen.Bounds.Left) / dpi.DpiScaleX;
        double localY = (point.Y - _screen.Bounds.Top) / dpi.DpiScaleY;

        AnimateHand(effect);
        AnimateFlash(
            PrimaryFlash,
            PrimaryFlashScale,
            PrimaryFlashRotation,
            effect.FlashIndex,
            effect.FlashSize,
            effect.FlashRotation,
            localX,
            localY,
            GetImpactTime(effect));

        if (effect.ShowSecondaryFlash)
        {
            int secondaryIndex = effect.FlashIndex == 1 ? 5 : effect.FlashIndex - 1;
            AnimateFlash(
                SecondaryFlash,
                SecondaryFlashScale,
                SecondaryFlashRotation,
                secondaryIndex,
                effect.FlashSize * 0.58,
                -effect.FlashRotation - 11,
                localX - 10,
                localY + 8,
                GetImpactTime(effect));
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        IntPtr style = NativeMethods.GetWindowExStyle(_windowHandle);
        long extendedStyle =
            style.ToInt64() |
            NativeMethods.WsExTransparent |
            NativeMethods.WsExLayered |
            NativeMethods.WsExNoActivate |
            NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowExStyle(_windowHandle, new IntPtr(extendedStyle));

        if (!NativeMethods.SetWindowPos(
                _windowHandle,
                NativeMethods.HwndTopmost,
                _screen.Bounds.Left,
                _screen.Bounds.Top,
                _screen.Bounds.Width,
                _screen.Bounds.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow))
        {
            throw new Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                $"Could not position the overlay on {_screen.DeviceName}.");
        }
    }

    private void AnimateHand(TapEffect effect)
    {
        JabTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        DepthScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DepthScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        TimeSpan pullback = GetPullbackTime(effect);
        TimeSpan impact = GetImpactTime(effect);
        DoubleAnimationUsingKeyFrames translationAnimation = new()
        {
            Duration = new Duration(effect.HandDuration),
            FillBehavior = FillBehavior.Stop
        };
        translationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        translationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            effect.HandTravel,
            KeyTime.FromTimeSpan(pullback),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        translationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            -effect.ImpactOvershoot,
            KeyTime.FromTimeSpan(impact),
            new CubicEase { EasingMode = EasingMode.EaseIn }));
        translationAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            0,
            KeyTime.FromTimeSpan(effect.HandDuration),
            new BackEase
            {
                Amplitude = 0.18,
                EasingMode = EasingMode.EaseOut
            }));
        JabTranslation.BeginAnimation(
            TranslateTransform.YProperty,
            translationAnimation);

        DoubleAnimationUsingKeyFrames scaleAnimation = new()
        {
            Duration = new Duration(effect.HandDuration),
            FillBehavior = FillBehavior.Stop
        };
        scaleAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(
            1,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            effect.PullbackScale,
            KeyTime.FromTimeSpan(pullback),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            effect.ImpactScale,
            KeyTime.FromTimeSpan(impact),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            1,
            KeyTime.FromTimeSpan(effect.HandDuration),
            new BackEase
            {
                Amplitude = 0.12,
                EasingMode = EasingMode.EaseOut
            }));
        DepthScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        DepthScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    private void AnimateFlash(
        Path flash,
        ScaleTransform scale,
        RotateTransform rotation,
        int flashIndex,
        double size,
        double angle,
        double x,
        double y,
        TimeSpan beginTime)
    {
        flash.Data = (Geometry)FindResource($"PointerFlash{flashIndex}Geometry");
        flash.Width = size;
        flash.Height = size;
        rotation.Angle = angle;
        Canvas.SetLeft(flash, x - size / 2);
        Canvas.SetTop(flash, y - size / 2);

        flash.BeginAnimation(OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        TimeSpan duration = TimeSpan.FromSeconds(0.15);
        DoubleAnimation opacity = new(1, 0, new Duration(duration))
        {
            BeginTime = beginTime,
            FillBehavior = FillBehavior.Stop
        };

        DoubleAnimationUsingKeyFrames scaleAnimation = new()
        {
            BeginTime = beginTime,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop
        };
        scaleAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(
            0.72,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            1.46,
            KeyTime.FromPercent(0.55),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        scaleAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(
            1,
            KeyTime.FromPercent(1)));

        flash.BeginAnimation(OpacityProperty, opacity);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    private static TimeSpan GetPullbackTime(TapEffect effect) =>
        TimeSpan.FromMilliseconds(Math.Min(
            110,
            effect.HandDuration.TotalMilliseconds * 0.24));

    private static TimeSpan GetImpactTime(TapEffect effect) =>
        TimeSpan.FromMilliseconds(Math.Min(
            180,
            effect.HandDuration.TotalMilliseconds * 0.43));
}
