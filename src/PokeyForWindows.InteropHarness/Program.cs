using System.Diagnostics;
using PokeyForWindows;

int iterations = args.Length == 0
    ? 10000
    : int.TryParse(args[0], out int parsed) && parsed > 0
        ? parsed
        : throw new ArgumentException("The iteration count must be a positive integer.");

var tests = new (string Name, Action Run)[]
{
    ("Tap series escalates and caps", TapSeriesEscalatesAndCaps),
    ("Tap series resets after idle", TapSeriesResetsAfterIdle),
    ("Tap series stress", () => TapSeriesStress(iterations)),
    ("Settings normalize range and enum", SettingsNormalizeRangeAndEnum),
    ("Settings round-trip", SettingsRoundTrip),
    ("Corrupt settings are quarantined", CorruptSettingsAreQuarantined),
    ("Locked corrupt settings use defaults", LockedCorruptSettingsUseDefaults),
    ("Release versions parse", ReleaseVersionsParse),
    ("Release URLs are restricted", ReleaseUrlsAreRestricted),
    ("Unsigned updates are rejected", UnsignedUpdatesAreRejected),
    ("Update gap restores executable", UpdateGapRestoresExecutable)
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void TapSeriesEscalatesAndCaps()
{
    TapSeriesController controller = new();
    long start = Stopwatch.GetTimestamp();
    long step = Stopwatch.Frequency / 10;

    TapEffect first = controller.RecordTap(start);
    TapEffect second = controller.RecordTap(start + step);
    TapEffect third = controller.RecordTap(start + step * 2);
    TapEffect fourth = controller.RecordTap(start + step * 3);
    controller.RecordTap(start + step * 4);
    TapEffect sixth = controller.RecordTap(start + step * 5);

    AssertEqual(0, first.Intensity, "first intensity");
    AssertEqual(1, second.Intensity, "second intensity");
    AssertEqual(2, third.Intensity, "third intensity");
    AssertEqual(3, fourth.Intensity, "fourth intensity");
    AssertEqual(3, sixth.Intensity, "intensity cap");
    AssertTrue(sixth.ShowSecondaryFlash, "sixth tap should combine flashes");
    AssertTrue(
        fourth.HandTravel > first.HandTravel,
        "rapid clicks should pull back farther");
    AssertTrue(
        fourth.PullbackScale < first.PullbackScale,
        "rapid clicks should recede farther into depth");
    AssertTrue(
        fourth.ImpactScale > first.ImpactScale,
        "rapid clicks should strike toward the viewer");
    AssertTrue(
        fourth.ImpactOvershoot > first.ImpactOvershoot,
        "rapid clicks should overshoot harder");
}

static void TapSeriesResetsAfterIdle()
{
    TapSeriesController controller = new();
    long start = Stopwatch.GetTimestamp();
    controller.RecordTap(start);
    controller.RecordTap(start + Stopwatch.Frequency / 10);

    long idleTicks = (long)(
        (TapSeriesController.ResetInterval.TotalSeconds + 0.1) *
        Stopwatch.Frequency);
    TapEffect reset = controller.RecordTap(
        start + Stopwatch.Frequency / 10 + idleTicks);

    AssertEqual(1, reset.TapNumber, "tap number after reset");
    AssertEqual(0, reset.Intensity, "intensity after reset");
}

static void TapSeriesStress(int iterations)
{
    TapSeriesController controller = new();
    long timestamp = Stopwatch.GetTimestamp();
    long step = Math.Max(1, Stopwatch.Frequency / 1000);

    for (int index = 0; index < iterations; index++)
    {
        TapEffect effect = controller.RecordTap(timestamp + step * index);
        AssertTrue(effect.Intensity is >= 0 and <= 3, "stress intensity bounds");
        AssertTrue(effect.FlashIndex is >= 1 and <= 5, "stress flash bounds");
    }
}

static void SettingsNormalizeRangeAndEnum()
{
    AppSettings settings = new()
    {
        PointerLength = 10,
        PointerTheme = (PointerTheme)999
    };
    settings.Normalize();

    AssertEqual(
        AppSettings.MinimumPointerLength,
        settings.PointerLength,
        "minimum pointer length");
    AssertEqual(PointerTheme.Light, settings.PointerTheme, "pointer theme");
}

static void SettingsRoundTrip()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeyForWindows-harness-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "settings.json");

    try
    {
        SettingsStore store = new(path);
        AppSettings expected = new()
        {
            Enabled = true,
            IsFirstRun = false,
            AnimateOnClick = false,
            PointerTheme = PointerTheme.Dark,
            PointerLength = 512,
            StartWithWindows = true,
            AutoCheckForUpdates = false
        };
        store.Save(expected);
        AppSettings actual = store.Load();

        AssertEqual(expected.Enabled, actual.Enabled, "enabled setting");
        AssertEqual(expected.PointerTheme, actual.PointerTheme, "theme setting");
        AssertEqual(expected.PointerLength, actual.PointerLength, "length setting");
        AssertEqual(
            expected.AutoCheckForUpdates,
            actual.AutoCheckForUpdates,
            "update setting");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void ReleaseVersionsParse()
{
    AssertEqual(new Version(1, 2, 3), AppUpdater.ParseVersion("v1.2.3"), "tag version");
    AssertEqual(
        new Version(2, 0, 1),
        AppUpdater.ParseVersion("2.0.1-preview.4"),
        "prerelease version");
}

static void LockedCorruptSettingsUseDefaults()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeyForWindows-locked-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "settings.json");

    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{still not valid json");
        using FileStream lockStream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        SettingsStore store = new(path);
        AppSettings settings = store.Load();

        AssertTrue(settings.IsFirstRun, "locked recovery should use defaults");
        AssertTrue(store.RecoveryNotice is not null, "locked recovery should be visible");
        AssertTrue(File.Exists(path), "locked invalid settings should remain in place");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void CorruptSettingsAreQuarantined()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeyForWindows-corrupt-{Guid.NewGuid():N}");
    string path = Path.Combine(directory, "settings.json");

    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{not valid json");
        SettingsStore store = new(path);
        AppSettings settings = store.Load();

        AssertTrue(settings.IsFirstRun, "recovered settings should use defaults");
        AssertTrue(store.RecoveryNotice is not null, "recovery notice should be recorded");
        AssertTrue(!File.Exists(path), "invalid settings should be moved aside");
        AssertTrue(
            Directory.EnumerateFiles(directory, "settings.json.corrupt-*").Any(),
            "quarantined settings file should exist");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void ReleaseUrlsAreRestricted()
{
    AppUpdater.ValidateDownloadUrl(
        "https://github.com/digital-workshop/pokeyforwindows/releases/download/v1.0.0/" +
        "PokeyForWindows-v1.0.0-win-x64.zip");

    AssertThrows<InvalidDataException>(
        () => AppUpdater.ValidateDownloadUrl("https://example.com/update.zip"),
        "unapproved URL");
}

static void UnsignedUpdatesAreRejected()
{
    string path = Path.Combine(
        Path.GetTempPath(),
        $"PokeyForWindows-unsigned-{Guid.NewGuid():N}.exe");
    try
    {
        File.WriteAllText(path, "not a signed executable");
        AssertThrows<InvalidDataException>(
            () => AuthenticodeVerifier.Verify(path, "Hanselman"),
            "unsigned executable");
    }
    finally
    {
        File.Delete(path);
    }
}

static void UpdateGapRestoresExecutable()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"PokeyForWindows-update-gap-{Guid.NewGuid():N}");
    string executable = Path.Combine(directory, "PokeyForWindows.exe");
    string backup = executable + ".old.exe";

    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(backup, "known good executable");
        using ManualResetEventSlim completed = new();

        UpdateRecoveryWatchdog.Run(
            ["--update-watchdog", int.MaxValue.ToString(), executable, backup],
            completed.Set);

        AssertTrue(completed.Wait(TimeSpan.FromSeconds(5)), "watchdog completion");
        AssertTrue(File.Exists(executable), "canonical executable should be restored");
        AssertTrue(!File.Exists(backup), "backup should be consumed");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void AssertTrue(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException(description);
    }
}

static void AssertEqual<T>(T expected, T actual, string description)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected '{expected}', got '{actual}'.");
    }
}

static void AssertThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}.");
}
