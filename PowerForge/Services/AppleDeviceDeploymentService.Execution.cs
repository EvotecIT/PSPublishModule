using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleDeviceDeploymentService
{
    private async Task<IReadOnlyList<AppleDeviceInfo>> GetDevicesCoreAsync(
        AppleDeviceListRequest request,
        bool requireTrustedSystemTool,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var processRequest = CreateXcrunRequest(
            request.XcrunExecutable,
            "Exact-source Apple device discovery",
            new[] { "devicectl", "list", "devices" },
            request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : request.Timeout,
            requireTrustedSystemTool);
        var result = await _processRunner.RunAsync(
            processRequest,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            throw CreateProcessException(result, "devicectl list devices failed.");

        var devices = ParseDevices(result.StdOut)
            .Where(device => request.IncludeUnavailable || device.IsAvailable);
        if (!string.IsNullOrWhiteSpace(request.Device))
        {
            var filter = request.Device!.Trim();
            devices = devices.Where(device => MatchesDevice(device, filter));
        }

        return devices.ToArray();
    }

    private async Task<AppleAppInstallResult> InstallCoreAsync(
        AppleAppInstallRequest request,
        bool requireTrustedSystemTool,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppPath))
            throw new ArgumentException("AppPath is required.", nameof(request));

        var appPath = Path.GetFullPath(request.AppPath);
        if (!Directory.Exists(appPath))
            throw new DirectoryNotFoundException($"App path was not found: {appPath}");

        var deviceIdentifier = await ResolveDeviceIdentifierAsync(
            request.DeviceIdentifier,
            request.Device,
            request.XcrunExecutable,
            request.Timeout,
            requireTrustedSystemTool,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(deviceIdentifier))
            throw new ArgumentException("DeviceIdentifier or Device is required.", nameof(request));

        var processRequest = CreateXcrunRequest(
            request.XcrunExecutable,
            "Exact-source Apple device installation",
            new[] { "devicectl", "device", "install", "app", "--device", deviceIdentifier!, appPath },
            request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : request.Timeout,
            requireTrustedSystemTool);
        var result = await _processRunner.RunAsync(
            processRequest,
            cancellationToken).ConfigureAwait(false);

        return new AppleAppInstallResult
        {
            DeviceIdentifier = deviceIdentifier!,
            AppPath = appPath,
            BundleIdentifier = MatchValue(BundleIdRegex, result.StdOut),
            InstallationUrl = MatchValue(InstallationUrlRegex, result.StdOut),
            ProcessResult = result
        };
    }

    private async Task<AppleAppLaunchResult> LaunchCoreAsync(
        AppleAppLaunchRequest request,
        bool requireTrustedSystemTool,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.BundleIdentifier))
            throw new ArgumentException("BundleIdentifier is required.", nameof(request));

        var deviceIdentifier = await ResolveDeviceIdentifierAsync(
            request.DeviceIdentifier,
            request.Device,
            request.XcrunExecutable,
            request.Timeout,
            requireTrustedSystemTool,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(deviceIdentifier))
            throw new ArgumentException("DeviceIdentifier or Device is required.", nameof(request));

        var arguments = new List<string>
        {
            "devicectl", "device", "process", "launch", "--device", deviceIdentifier!
        };
        if (request.EnvironmentVariables.Count > 0)
        {
            arguments.Add("--environment-variables");
            arguments.Add(JsonSerializer.Serialize(request.EnvironmentVariables));
        }
        if (request.TerminateExisting)
            arguments.Add("--terminate-existing");
        arguments.Add(request.BundleIdentifier.Trim());
        arguments.AddRange(request.Arguments);

        var processRequest = CreateXcrunRequest(
            request.XcrunExecutable,
            "Exact-source Apple device launch",
            arguments,
            request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : request.Timeout,
            requireTrustedSystemTool);
        var result = await _processRunner.RunAsync(
            processRequest,
            cancellationToken).ConfigureAwait(false);

        return new AppleAppLaunchResult
        {
            DeviceIdentifier = deviceIdentifier!,
            BundleIdentifier = request.BundleIdentifier.Trim(),
            ProcessResult = result
        };
    }

    private static ProcessRunRequest CreateXcrunRequest(
        string? executable,
        string trustedOperation,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        bool requireTrustedSystemTool)
        => requireTrustedSystemTool
            ? AppleTrustedExecutionEnvironment.CreateProcessRequest(
                executable,
                "xcrun",
                "/usr/bin/xcrun",
                trustedOperation,
                Directory.GetCurrentDirectory(),
                arguments,
                timeout)
            : new ProcessRunRequest(
                NormalizeExecutable(executable, "xcrun"),
                Directory.GetCurrentDirectory(),
                arguments,
                timeout);
}
