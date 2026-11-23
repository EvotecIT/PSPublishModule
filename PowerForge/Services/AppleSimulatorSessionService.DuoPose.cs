using System.Diagnostics;

namespace PowerForge;

public sealed partial class AppleSimulatorSessionService
{
    /// <summary>duoctl release the fold step accepts; other versions fall back to the prompt.</summary>
    public const string PinnedDuoFoldToolVersion = "0.2.1";

    private static readonly TimeSpan DuoPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DuoRotationSettle = TimeSpan.FromSeconds(3);

    private async Task<AppleDuoPoseResult> EnsureDuoPoseAsync(string deviceId, string? deviceSet, AppleDuoPoseRequest pose,
        TimeSpan simulatorTimeout, CancellationToken cancellationToken)
    {
        var display = pose.Pose == AppleDuoPose.Closed ? "primary" : "primary-1";
        var result = new AppleDuoPoseResult { Pose = pose.Pose, Display = display };
        var reading = await ProbeDuoDisplayAsync(deviceId, deviceSet, display, simulatorTimeout, cancellationToken).ConfigureAwait(false);
        if (DuoPoseReached(pose, reading)) return Complete(result, reading!.Value, "already");

        if (!string.IsNullOrWhiteSpace(pose.FoldToolPath))
        {
            var (folded, note) = await TryFoldAsync(deviceId, deviceSet, display, pose, simulatorTimeout, cancellationToken).ConfigureAwait(false);
            if (folded is not null) return Complete(result, folded.Value, "fold-tool");
            result.FoldToolNote = note;
            _promptWriter.WriteLine($"Automatic fold didn't set the pose: {note}");
        }

        _promptWriter.WriteLine($"iPhone Duo {deviceId}: in Device Hub, choose {DescribePose(pose)}. " +
            $"Waiting up to {FormatDuration(pose.PromptTimeout)} for the {DescribeDisplay(pose.Pose)} display...");
        var prompted = Stopwatch.StartNew();
        for (var waited = TimeSpan.Zero; waited < pose.PromptTimeout && prompted.Elapsed < pose.PromptTimeout; waited += DuoPollInterval)
        {
            reading = await ProbeDuoDisplayBeforeDeadlineAsync(deviceId, deviceSet, display, simulatorTimeout,
                prompted, pose.PromptTimeout, cancellationToken).ConfigureAwait(false);
            if (DuoPoseReached(pose, reading))
            {
                _promptWriter.WriteLine($"iPhone Duo {deviceId}: {DescribeDisplay(pose.Pose)} display is live at {reading!.Value.Width}x{reading.Value.Height}.");
                return Complete(result, reading.Value, "prompt");
            }
            await DelayDuoWithinBudgetAsync(DuoPollInterval, prompted, pose.PromptTimeout, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"The iPhone Duo {DescribeDisplay(pose.Pose)} display wasn't live " +
            $"{DescribeOrientation(pose.Orientation)}within {FormatDuration(pose.PromptTimeout)}. Set {DescribePose(pose)} in Device Hub.");
    }

    private async Task<(AppleDuoScreenshotProbe.Reading? Reading, string Note)> TryFoldAsync(string deviceId, string? deviceSet,
        string display, AppleDuoPoseRequest pose, TimeSpan simulatorTimeout, CancellationToken cancellationToken)
    {
        var tool = pose.FoldToolPath!;
        if (!File.Exists(tool)) return (null, $"fold tool not found at {tool}");
        var version = await RunFoldToolAsync(tool, new[] { "--version" }, pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
        var reported = (version.StdOut ?? string.Empty).Trim();
        if (!version.Succeeded || reported != "duoctl " + PinnedDuoFoldToolVersion)
            return (null, $"expected duoctl {PinnedDuoFoldToolVersion}, found '{(reported.Length == 0 ? "no version" : reported)}'");

        // duoctl confirms each change through devicectl, which doesn't list every simulator runtime, so its exit
        // code isn't trusted either way: the screenshot of the target display decides.
        await RunFoldToolAsync(tool, new[] { "-d", deviceId, pose.Pose == AppleDuoPose.Closed ? "close" : "open", "--duration", "1" },
            pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        AppleDuoScreenshotProbe.Reading? reading = null;
        for (var waited = TimeSpan.Zero; waited < pose.FoldTimeout && clock.Elapsed < pose.FoldTimeout; waited += DuoPollInterval)
        {
            reading = await ProbeDuoDisplayBeforeDeadlineAsync(deviceId, deviceSet, display, simulatorTimeout,
                clock, pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
            if (DuoPoseReached(pose, reading)) return (reading, string.Empty);
            if (reading is { Live: true }) break;
            await DelayDuoWithinBudgetAsync(DuoPollInterval, clock, pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
        }
        if (reading is { Live: true } && pose.Orientation != AppleDuoOrientation.Any)
        {
            // The display is live in the other orientation. One rotate, then one more look once it has settled.
            await RunFoldToolAsync(tool, new[] { "-d", deviceId, "rotate", pose.Orientation == AppleDuoOrientation.Portrait ? "portrait" : "landscape" },
                pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
            var rotationClock = Stopwatch.StartNew();
            await DelayDuoWithinBudgetAsync(DuoRotationSettle, rotationClock, pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
            reading = await ProbeDuoDisplayBeforeDeadlineAsync(deviceId, deviceSet, display, simulatorTimeout,
                rotationClock, pose.FoldTimeout, cancellationToken).ConfigureAwait(false);
            if (DuoPoseReached(pose, reading)) return (reading, string.Empty);
        }
        var seen = reading is null ? "no screenshot" : reading.Value.Live ? $"live at {reading.Value.Width}x{reading.Value.Height}" : "not live";
        return (null, $"the {DescribeDisplay(pose.Pose)} display was {seen} after duoctl ran");
    }

    private Task<ProcessRunResult> RunFoldToolAsync(string tool, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        => _simulatorRunner.RunAsync(new ProcessRunRequest(tool, Directory.GetCurrentDirectory(), arguments, timeout)
        { MaxCapturedOutputCharacters = 64 * 1024 }, cancellationToken);

    private async Task<AppleDuoScreenshotProbe.Reading?> ProbeDuoDisplayBeforeDeadlineAsync(string deviceId, string? deviceSet,
        string display, TimeSpan simulatorTimeout, Stopwatch clock, TimeSpan budget, CancellationToken cancellationToken)
    {
        var remaining = budget - clock.Elapsed;
        if (remaining <= TimeSpan.Zero) return null;
        var reading = await ProbeDuoDisplayAsync(deviceId, deviceSet, display,
            remaining < simulatorTimeout ? remaining : simulatorTimeout, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return clock.Elapsed < budget ? reading : null;
    }

    private Task DelayDuoWithinBudgetAsync(TimeSpan delay, Stopwatch clock, TimeSpan budget, CancellationToken cancellationToken)
    {
        var remaining = budget - clock.Elapsed;
        return remaining > TimeSpan.Zero
            ? _delay(remaining < delay ? remaining : delay, cancellationToken)
            : Task.CompletedTask;
    }

    private async Task<AppleDuoScreenshotProbe.Reading?> ProbeDuoDisplayAsync(string deviceId, string? deviceSet, string display,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "powerforge-duo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, display + ".png");
            var capture = await SimctlAsync(deviceSet, new[] { "io", deviceId, "screenshot", "--display=" + display, path },
                timeout, cancellationToken).ConfigureAwait(false);
            if (!capture.Succeeded || !File.Exists(path)) return null;
            try { return AppleDuoScreenshotProbe.Read(path); }
            catch (InvalidDataException) { return null; }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* A leftover probe image in temp doesn't affect the session. */ }
            catch (UnauthorizedAccessException) { /* Same as above. */ }
        }
    }

    internal static bool DuoPoseReached(AppleDuoPoseRequest pose, AppleDuoScreenshotProbe.Reading? reading)
    {
        if (reading is not { Live: true } value) return false;
        var (width, height) = pose.Pose == AppleDuoPose.Closed ? (1398, 2034) : (2007, 2853);
        var portrait = value.Width == width && value.Height == height;
        var landscape = value.Width == height && value.Height == width;
        return pose.Orientation switch
        {
            AppleDuoOrientation.Portrait => portrait,
            AppleDuoOrientation.Landscape => landscape,
            _ => portrait || landscape
        };
    }

    internal static IReadOnlyDictionary<string, string?> DuoPoseEnvironment(AppleDuoPoseResult pose) => new Dictionary<string, string?>
    {
        ["POWERFORGE_DUO_POSE"] = pose.Pose == AppleDuoPose.Closed ? "closed" : "open",
        ["POWERFORGE_DUO_DISPLAY"] = pose.Display,
        ["POWERFORGE_DUO_SIZE"] = $"{pose.Width}x{pose.Height}"
    };

    private static AppleDuoPoseResult Complete(AppleDuoPoseResult result, AppleDuoScreenshotProbe.Reading reading, string method)
    {
        result.Width = reading.Width;
        result.Height = reading.Height;
        result.Orientation = reading.Width < reading.Height ? "portrait" : "landscape";
        result.Method = method;
        return result;
    }

    private static string DescribeDisplay(AppleDuoPose pose) => pose == AppleDuoPose.Closed ? "outer" : "inner";

    private static string DescribePose(AppleDuoPoseRequest pose)
        => (pose.Pose == AppleDuoPose.Closed ? "Closed" : "Open") + pose.Orientation switch
        {
            AppleDuoOrientation.Portrait => " and rotate to portrait",
            AppleDuoOrientation.Landscape => " and rotate to landscape",
            _ => string.Empty
        };

    private static string DescribeOrientation(AppleDuoOrientation orientation) => orientation switch
    {
        AppleDuoOrientation.Portrait => "in portrait ",
        AppleDuoOrientation.Landscape => "in landscape ",
        _ => string.Empty
    };

    private static string FormatDuration(TimeSpan value)
        => value.TotalMinutes >= 1 && value.Seconds == 0 ? $"{(int)value.TotalMinutes} min" : $"{(int)value.TotalSeconds} s";
}
