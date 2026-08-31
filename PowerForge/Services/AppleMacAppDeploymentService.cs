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

        var sourceRoot = AppleDeviceDeploymentService.ResolveBuildRoot(
            Path.GetFullPath(request.ProjectPath),
            request.BuildRoot);
        AppleDeviceDeploymentService.EnsureOutputPathOutsideBuildRoot(
            request.InstallRoot,
            sourceRoot,
            nameof(request.InstallRoot),
            FrameworkCompatibility.GetPathStringComparisonForPath(sourceRoot));

        var build = await _buildService.BuildAsync(request, cancellationToken).ConfigureAwait(false);
        var deployment = new AppleMacAppDeploymentResult { Build = build };
        if (!build.Succeeded)
            return deployment;

        var installedAppPath = Path.Combine(Path.GetFullPath(request.InstallRoot), Path.GetFileName(build.AppPath));
        using var installLock = AppleMacAppBundleReplacement.AcquireInstallLock(installedAppPath);
        var install = await InstallAsync(request, build.AppPath, cancellationToken).ConfigureAwait(false);
        deployment.Install = install;
        if (!install.Succeeded || !request.Launch)
            return deployment;

        deployment.Launch = await LaunchAsync(request, install.InstalledAppPath, cancellationToken).ConfigureAwait(false);
        return deployment;
    }

    private async Task<AppleMacAppInstallResult> InstallAsync(
        AppleMacAppDeploymentRequest request,
        string sourceAppPath,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourceAppPath);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Built app path was not found: {source}");

        var installRoot = Path.GetFullPath(request.InstallRoot);
        Directory.CreateDirectory(installRoot);
        var destination = Path.Combine(installRoot, Path.GetFileName(source));
        var suffix = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(installRoot, $".{Path.GetFileName(source)}.powerforge-stage-{suffix}");
        var backup = Path.Combine(installRoot, $".{Path.GetFileName(source)}.powerforge-backup-{suffix}");
        var recoveryWarning = AppleMacAppBundleReplacement.RecoverInterruptedReplacement(destination);

        var copy = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.DittoExecutable, "/usr/bin/ditto"),
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
        {
            AppleMacAppBundleReplacement.TryDeleteDirectory(stage, out _);
            return result;
        }
        if (!Directory.Exists(stage))
            throw new InvalidOperationException($"ditto completed but did not create the staged app: {stage}");

        try
        {
            var replacementWarning = AppleMacAppBundleReplacement.Replace(stage, destination, backup);
            result.Succeeded = true;
            result.Warning = string.Join(" ", new[] { recoveryWarning, replacementWarning }.Where(static value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(result.Warning))
                result.Warning = null;
            return result;
        }
        finally
        {
            if (Directory.Exists(stage))
                AppleMacAppBundleReplacement.TryDeleteDirectory(stage, out _);
        }
    }

    private async Task<AppleMacAppLaunchResult> LaunchAsync(
        AppleMacAppDeploymentRequest request,
        string appPath,
        CancellationToken cancellationToken)
    {
        if (request.TerminateExisting)
            await TerminateExistingAsync(request, appPath, cancellationToken).ConfigureAwait(false);

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
            new ProcessRunRequest(
                NormalizeExecutable(request.OpenExecutable, "/usr/bin/open"),
                request.InstallRoot,
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
        string appPath,
        CancellationToken cancellationToken)
    {
        var executableRoot = Path.Combine(Path.GetFullPath(appPath), "Contents", "MacOS") + Path.DirectorySeparatorChar;
        var pattern = $"^{System.Text.RegularExpressions.Regex.Escape(executableRoot)}";
        var process = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.PkillExecutable, "/usr/bin/pkill"),
                request.InstallRoot,
                new[] { "-f", pattern },
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        if (process.ExitCode is not 0 and not 1)
            throw new InvalidOperationException($"Could not terminate the existing installed app: {process.StdErr.Trim()}");
    }

    private static string NormalizeExecutable(string? executable, string fallback)
        => string.IsNullOrWhiteSpace(executable) ? fallback : executable!.Trim();

}
