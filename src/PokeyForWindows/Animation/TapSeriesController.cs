using System.Diagnostics;

namespace PokeyForWindows;

internal readonly record struct TapEffect(
    int TapNumber,
    int Intensity,
    int FlashIndex,
    double FlashSize,
    double FlashRotation,
    double HandTravel,
    TimeSpan HandDuration,
    bool ShowSecondaryFlash);

internal sealed class TapSeriesController
{
    internal static readonly TimeSpan ResetInterval = TimeSpan.FromMilliseconds(800);

    private static readonly TimeSpan[] HandDurations =
    [
        TimeSpan.FromMilliseconds(360),
        TimeSpan.FromMilliseconds(440),
        TimeSpan.FromMilliseconds(540),
        TimeSpan.FromMilliseconds(700)
    ];

    private static readonly double[] FlashSizes = [48, 68, 94, 128, 148];
    private static readonly double[] FlashRotations = [-8, 5, -4, 8, 0];
    private static readonly double[] HandTravel = [4, 8, 14, 22];

    private long _lastTapTimestamp;
    private int _tapNumber;

    internal TapEffect RecordTap(long timestamp)
    {
        long resetTicks = (long)(ResetInterval.TotalSeconds * Stopwatch.Frequency);
        long elapsed = timestamp - _lastTapTimestamp;

        if (_lastTapTimestamp == 0 || elapsed <= 0 || elapsed > resetTicks)
        {
            _tapNumber = 0;
        }

        _lastTapTimestamp = timestamp;
        _tapNumber++;

        int intensity = Math.Min(_tapNumber - 1, HandDurations.Length - 1);
        int flashIndex = _tapNumber <= 5
            ? _tapNumber
            : 3 + ((_tapNumber - 6) % 3);
        int flashArrayIndex = flashIndex - 1;

        return new TapEffect(
            _tapNumber,
            intensity,
            flashIndex,
            FlashSizes[flashArrayIndex],
            FlashRotations[flashArrayIndex],
            HandTravel[intensity],
            HandDurations[intensity],
            intensity == 3 && _tapNumber % 2 == 0);
    }

    internal void Reset()
    {
        _lastTapTimestamp = 0;
        _tapNumber = 0;
    }
}
