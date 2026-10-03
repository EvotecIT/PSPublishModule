namespace PowerForge;

public sealed partial class AppleDeviceDeploymentService
{
    /// <summary>
    /// Builds, installs, and optionally launches an Apple app on a physical device.
    /// </summary>
    /// <param name="request">Deployment request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deployment result.</returns>
    public async Task<AppleAppDeviceDeploymentResult> DeployAsync(
        AppleAppDeviceDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        ValidateDeploymentDeviceSelection(request);
        _ = AppleTrustedExecutionEnvironment.ResolveSystemTool(
            request.XcrunExecutable,
            "xcrun",
            "/usr/bin/xcrun",
            "Exact-source Apple device deployment");

        using var buildOperation = await BuildForDeploymentAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        var build = buildOperation.Result;
        var deployment = new AppleAppDeviceDeploymentResult
        {
            Build = build,
            LaunchRequested = request.Launch
        };

        if (!build.Succeeded)
            return deployment;

        var retainedAppPath = buildOperation.PreserveResultProduct();

        // BuildCore resolves a name/model selector once. Keep that identity for
        // every subsequent stage even if device availability changes.
        var deployDeviceIdentifier = TryParseDestinationDeviceIdentifier(build.Destination)
            ?? throw new InvalidOperationException("The build did not resolve a physical device identifier.");
        var install = await InstallCoreAsync(new AppleAppInstallRequest
        {
            DeviceIdentifier = deployDeviceIdentifier,
            AppPath = buildOperation.ProductSnapshot?.AppPath ?? build.AppPath,
            XcrunExecutable = request.XcrunExecutable,
            Timeout = request.Timeout
        }, requireTrustedSystemTool: true, cancellationToken).ConfigureAwait(false);
        buildOperation.ProductSnapshot?.ValidateUnchanged();
        install.AppPath = retainedAppPath;
        deployment.Install = install;

        if (!install.Succeeded || !request.Launch)
            return deployment;

        var bundleIdentifier = string.IsNullOrWhiteSpace(request.BundleIdentifier)
            ? install.BundleIdentifier
            : request.BundleIdentifier!.Trim();
        if (string.IsNullOrWhiteSpace(bundleIdentifier))
            throw new InvalidOperationException("BundleIdentifier is required to launch and could not be parsed from the install output.");

        deployment.Launch = await LaunchCoreAsync(new AppleAppLaunchRequest
        {
            DeviceIdentifier = deployDeviceIdentifier,
            BundleIdentifier = bundleIdentifier!,
            XcrunExecutable = request.XcrunExecutable,
            EnvironmentVariables = new Dictionary<string, string>(request.LaunchEnvironment, StringComparer.Ordinal),
            Arguments = request.LaunchArguments,
            TerminateExisting = request.TerminateExisting,
            Timeout = request.Timeout
        }, requireTrustedSystemTool: true, cancellationToken).ConfigureAwait(false);

        return deployment;
    }

    private static void ValidateDeploymentDeviceSelection(AppleAppDeviceDeploymentRequest request)
    {
        var destinationIdentifier = TryParseDestinationDeviceIdentifier(request.Destination);
        if (!string.IsNullOrWhiteSpace(request.Destination) && destinationIdentifier is null)
            throw new ArgumentException("Device deployment requires a Destination containing id=<device identifier>. Generic destinations are supported by BuildAsync only.", nameof(request));
        if (destinationIdentifier is null && string.IsNullOrWhiteSpace(request.DeviceIdentifier) && string.IsNullOrWhiteSpace(request.Device))
            throw new ArgumentException("Device deployment requires Device, DeviceIdentifier, or a Destination containing id=<device identifier>.", nameof(request));
        if (destinationIdentifier is not null && !string.IsNullOrWhiteSpace(request.DeviceIdentifier) &&
            !destinationIdentifier.Equals(request.DeviceIdentifier!.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Destination and DeviceIdentifier select different devices.", nameof(request));
    }

}
