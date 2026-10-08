using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleSimulatorSessionService
{
    private async Task<ProcessRunResult> SimctlAsync(string? deviceSet, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var argv = new List<string> { "simctl" };
        if (deviceSet is not null) { argv.Add("--set"); argv.Add(deviceSet); }
        argv.AddRange(arguments);
        return await _simulatorRunner.RunAsync(new ProcessRunRequest("xcrun", Directory.GetCurrentDirectory(), argv, timeout)
        { MaxCapturedOutputCharacters = 1024 * 1024 }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetDeviceStateAsync(string deviceId, string? deviceSet, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var inventory = await SimctlAsync(deviceSet, new[] { "list", "devices", "--json" }, timeout, cancellationToken).ConfigureAwait(false);
        RequireSuccess(inventory, "Simulator discovery");
        using var document = JsonDocument.Parse(inventory.StdOut);
        var matches = new List<JsonElement>();
        foreach (var runtime in document.RootElement.GetProperty("devices").EnumerateObject())
            foreach (var device in runtime.Value.EnumerateArray())
                if (string.Equals(device.GetProperty("udid").GetString(), deviceId, StringComparison.OrdinalIgnoreCase)) matches.Add(device);
        if (matches.Count != 1) throw new InvalidOperationException($"Expected exactly one simulator with UUID {deviceId}; found {matches.Count}.");
        var selected = matches[0];
        if (selected.TryGetProperty("isAvailable", out var available) && !available.GetBoolean())
            throw new InvalidOperationException($"Simulator {deviceId} is unavailable.");
        return selected.GetProperty("state").GetString() ?? throw new InvalidDataException("Simulator state is missing.");
    }

    private static void RequireSuccess(ProcessRunResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} failed (exit {result.ExitCode}, timeout {result.TimedOut}, output limit {result.StandardOutputLimitExceeded || result.StandardErrorLimitExceeded}).");
    }

    private static void RequireFiniteTimeout(TimeSpan timeout, string name)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "A finite positive timeout no longer than 24 days is required.");
    }

    private static string NormalizeDeviceId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("An exact nonempty simulator UUID is required; names, 'booted', and 'all' are not accepted.");
        return id.ToString("D").ToUpperInvariant();
    }

    private static string? NormalizeDeviceSet(string? path)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "CoreSimulator", "Devices");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Device-set path cannot be empty.");
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Explicit simulator device set must already exist: " + fullPath);
        return AppleReleaseArtifactService.ResolvePhysicalPath(fullPath);
    }
}
