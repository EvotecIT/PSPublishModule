namespace PowerForge;

/// <summary>
/// Builds, atomically installs, and launches local macOS application bundles.
/// </summary>
public sealed class AppleMacAppDeploymentService
{
    private readonly IProcessRunner _processRunner;
    private readonly AppleDeviceDeploymentService _buildService;

    /// <summary>
    /// Initializes a new local macOS deployment service.
    /// </summary>
    /// <param name="processRunner">Process runner used for xcodebuild, ditto, and open.</param>
    public AppleMacAppDeploymentService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
        _buildService = new AppleDeviceDeploymentService(_processRunner);
    }

    /// <summary>
    /// Builds, installs, and optionally launches a macOS app.
    /// </summary>
    /// <param name="request">Deployment request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deployment result.</returns>
    public async Task<AppleMacAppDeploymentResult> DeployAsync(
        AppleMacAppDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Platform != ApplePlatform.macOS)
            throw new ArgumentException("Local macOS deployment requires Platform macOS.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(request));
        var dittoExecutable = AppleTrustedExecutionEnvironment.ResolveSystemTool(
            request.DittoExecutable,
            "ditto",
            "/usr/bin/ditto",
            "Exact-source macOS app installation");
        var openExecutable = request.Launch
            ? AppleTrustedExecutionEnvironment.ResolveSystemTool(
                request.OpenExecutable,
                "open",
                "/usr/bin/open",
                "Exact-source macOS app launch")
            : null;
        var pkillExecutable = request.Launch && request.TerminateExisting
            ? AppleTrustedExecutionEnvironment.ResolveSystemTool(
                request.PkillExecutable,
                "pkill",
                "/usr/bin/pkill",
                "Exact-source macOS app launch")
            : null;

        var sourceRoot = AppleDeviceDeploymentService.ResolveBuildRoot(
            Path.GetFullPath(request.ProjectPath),
            request.BuildRoot);
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            request.InstallRoot,
            sourceRoot,
            nameof(request.InstallRoot),
            FrameworkCompatibility.GetPathStringComparisonForPath(sourceRoot));

        using var buildOperation = await _buildService.BuildForDeploymentAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        var build = buildOperation.Result;
        var deployment = new AppleMacAppDeploymentResult { Build = build };
        if (!build.Succeeded)
            return deployment;
        var productSnapshot = buildOperation.ProductSnapshot ?? throw new InvalidOperationException(
            "The successful macOS build did not retain its provenance-bound app snapshot.");
        var retainedAppPath = buildOperation.PreserveResultProduct();
        var installRoot = ResolveInstallRootAtBoundary(
            request.InstallRoot,
            sourceRoot);

        var installedAppPath = Path.Combine(installRoot, Path.GetFileName(build.AppPath));
        using var installLock = AppleMacAppBundleReplacement.AcquireInstallLock(installedAppPath);
        var install = await InstallAsync(
            request,
            installRoot,
            productSnapshot.AppPath,
            productSnapshot,
            dittoExecutable,
            cancellationToken).ConfigureAwait(false);
        install.SourceAppPath = retainedAppPath;
        deployment.Install = install;
        if (!install.Succeeded || !request.Launch)
            return deployment;

        deployment.Launch = await LaunchAsync(
            request,
            installRoot,
            install.InstalledAppPath,
            openExecutable!,
            pkillExecutable,
            cancellationToken).ConfigureAwait(false);
        return deployment;
    }

    private async Task<AppleMacAppInstallResult> InstallAsync(
        AppleMacAppDeploymentRequest request,
        string installRoot,
        string sourceAppPath,
        AppleBuiltAppSnapshot productSnapshot,
        string dittoExecutable,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourceAppPath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Built app path was not found: {source}");

        var destination = Path.Combine(installRoot, Path.GetFileName(source));
        var suffix = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(installRoot, $".{Path.GetFileName(source)}.powerforge-stage-{suffix}");
        var stage = Path.Combine(stageRoot, Path.GetFileName(source));
        var backup = Path.Combine(installRoot, $".{Path.GetFileName(source)}.powerforge-backup-{suffix}");
        var recoveryWarning = AppleMacAppBundleReplacement.RecoverInterruptedReplacement(destination);
        try
        {
            Directory.CreateDirectory(stageRoot);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(stageRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif

            var copy = await _processRunner.RunAsync(
                AppleTrustedExecutionEnvironment.CreateProcessRequest(
                    dittoExecutable,
                    "ditto",
                    "/usr/bin/ditto",
                    "Exact-source macOS app installation",
                    installRoot,
                    new[] { source, stage },
                    request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : request.Timeout),
                cancellationToken).ConfigureAwait(false);

            var result = new AppleMacAppInstallResult
            {
                SourceAppPath = source,
                InstalledAppPath = destination,
                ProcessResult = copy
            };
            if (!copy.Succeeded)
                return result;
            if (!Directory.Exists(stage))
                throw new InvalidOperationException($"ditto completed but did not create the staged app: {stage}");

            var stageSnapshot = productSnapshot.CaptureVerifiedCopy(
                stage,
                "staged macOS app");
            productSnapshot.ValidateUnchanged();
            var replacementWarning = AppleMacAppBundleReplacement.Replace(
                stage,
                destination,
                backup,
                stageSnapshot.ValidateUnchanged);
            result.Succeeded = true;
            result.Warning = string.Join(" ", new[] { recoveryWarning, replacementWarning }.Where(static value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(result.Warning))
                result.Warning = null;
            return result;
        }
        finally
        {
            if (Directory.Exists(stageRoot))
                AppleMacAppBundleReplacement.TryDeleteDirectory(stageRoot, out _);
        }
    }

    private async Task<AppleMacAppLaunchResult> LaunchAsync(
        AppleMacAppDeploymentRequest request,
        string installRoot,
        string appPath,
        string openExecutable,
        string? pkillExecutable,
        CancellationToken cancellationToken)
    {
        if (request.TerminateExisting)
            await TerminateExistingAsync(
                request,
                installRoot,
                appPath,
                pkillExecutable ?? throw new InvalidOperationException(
                    "Exact-source macOS termination requires a trusted pkill executable."),
                cancellationToken).ConfigureAwait(false);

        var arguments = new List<string> { "--new", "--fresh" };
        foreach (var pair in request.LaunchEnvironment.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            arguments.Add("--env");
            arguments.Add($"{pair.Key}={pair.Value}");
        }
        arguments.Add(appPath);
        if (request.LaunchArguments.Length > 0)
        {
            arguments.Add("--args");
            arguments.AddRange(request.LaunchArguments);
        }

        var process = await _processRunner.RunAsync(
            AppleTrustedExecutionEnvironment.CreateProcessRequest(
                openExecutable,
                "open",
                "/usr/bin/open",
                "Exact-source macOS app launch",
                installRoot,
                arguments,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        return new AppleMacAppLaunchResult
        {
            AppPath = appPath,
            ProcessResult = process
        };
    }

    private async Task TerminateExistingAsync(
        AppleMacAppDeploymentRequest request,
        string installRoot,
        string appPath,
        string pkillExecutable,
        CancellationToken cancellationToken)
    {
        var executableRoot = Path.Combine(Path.GetFullPath(appPath), "Contents", "MacOS") + Path.DirectorySeparatorChar;
        var pattern = $"^{System.Text.RegularExpressions.Regex.Escape(executableRoot)}";
        var process = await _processRunner.RunAsync(
            AppleTrustedExecutionEnvironment.CreateProcessRequest(
                pkillExecutable,
                "pkill",
                "/usr/bin/pkill",
                "Exact-source macOS app launch",
                installRoot,
                new[] { "-f", pattern },
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        if (process.ExitCode is not 0 and not 1)
            throw new InvalidOperationException($"Could not terminate the existing installed app: {process.StdErr.Trim()}");
    }

    private static string ResolveInstallRootAtBoundary(
        string requestedInstallRoot,
        string sourceRoot)
    {
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(
            sourceRoot);
        var resolved = AppleReleaseArtifactService.ResolvePhysicalPath(
            Path.GetFullPath(requestedInstallRoot));
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            resolved,
            sourceRoot,
            nameof(AppleMacAppDeploymentRequest.InstallRoot),
            comparison);
        Directory.CreateDirectory(resolved);

        var stable = AppleReleaseArtifactService.ResolvePhysicalPath(resolved);
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            stable,
            sourceRoot,
            nameof(AppleMacAppDeploymentRequest.InstallRoot),
            comparison);
        return stable;
    }

}
