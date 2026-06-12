using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Builds, installs, launches, and discovers Apple devices through xcodebuild and xcrun devicectl.
/// </summary>
public sealed class AppleDeviceDeploymentService
{
    private static readonly Regex DeviceLineRegex = new(
        @"^(?<name>.+?)\s{2,}(?<hostname>\S+)\s{2,}(?<identifier>[0-9A-Fa-f-]{36})\s{2,}(?<state>.+?)\s{2,}(?<model>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BundleIdRegex = new(
        @"bundleID:\s*(?<value>\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InstallationUrlRegex = new(
        @"installationURL:\s*(?<value>\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleDeviceDeploymentService"/> class.
    /// </summary>
    /// <param name="processRunner">Process runner used to execute Apple tooling.</param>
    public AppleDeviceDeploymentService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    /// <summary>
    /// Lists devices known to xcrun devicectl.
    /// </summary>
    /// <param name="request">Device list request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered devices.</returns>
    public async Task<IReadOnlyList<AppleDeviceInfo>> GetDevicesAsync(
        AppleDeviceListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcrunExecutable, "xcrun"),
                Directory.GetCurrentDirectory(),
                new[] { "devicectl", "list", "devices" },
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : request.Timeout),
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

    /// <summary>
    /// Builds an Apple app for local device installation.
    /// </summary>
    /// <param name="request">Build request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Build result.</returns>
    public async Task<AppleAppBuildResult> BuildAsync(
        AppleAppBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Scheme))
            throw new ArgumentException("Scheme is required.", nameof(request));

        var projectPath = Path.GetFullPath(request.ProjectPath);
        if (!File.Exists(projectPath) && !Directory.Exists(projectPath))
            throw new FileNotFoundException("Xcode project or workspace was not found.", projectPath);

        var deviceIdentifier = await ResolveDeviceIdentifierAsync(
            request.DeviceIdentifier,
            request.Device,
            request.XcrunExecutable,
            request.Timeout,
            cancellationToken).ConfigureAwait(false);

        var destination = ResolveDestination(request.Destination, deviceIdentifier, request.Platform);
        var derivedDataPath = ResolveDerivedDataPath(request);
        var appPath = ResolveAppPath(request, derivedDataPath);
        var buildProjectPath = projectPath;
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string? mirrorPath = null;

        if (request.UseBuildMirror)
        {
            var mirror = await MirrorBuildRootAsync(projectPath, request, cancellationToken).ConfigureAwait(false);
            if (!mirror.ProcessResult.Succeeded)
            {
                return new AppleAppBuildResult
                {
                    AppPath = appPath,
                    Destination = destination,
                    DerivedDataPath = derivedDataPath,
                    BuildMirrorPath = mirror.MirrorPath,
                    ProcessResult = mirror.ProcessResult
                };
            }

            buildProjectPath = RewritePath(projectPath, mirror.SourceRoot, mirror.MirrorPath);
            workingDirectory = mirror.MirrorPath;
            mirrorPath = mirror.MirrorPath;
        }

        Directory.CreateDirectory(derivedDataPath);

        var args = new List<string>
        {
            request.IsWorkspace ? "-workspace" : "-project",
            buildProjectPath,
            "-scheme",
            request.Scheme.Trim(),
            "-configuration",
            string.IsNullOrWhiteSpace(request.Configuration) ? "Debug" : request.Configuration.Trim(),
            "-destination",
            destination,
            "-derivedDataPath",
            derivedDataPath
        };

        if (request.AllowProvisioningUpdates)
            args.Add("-allowProvisioningUpdates");

        args.Add("build");
        args.AddRange(request.AdditionalArguments ?? Array.Empty<string>());

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcodeBuildExecutable, "xcodebuild"),
                workingDirectory,
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        return new AppleAppBuildResult
        {
            AppPath = appPath,
            Destination = destination,
            DerivedDataPath = derivedDataPath,
            BuildMirrorPath = mirrorPath,
            ProcessResult = result
        };
    }

    /// <summary>
    /// Installs a built app on a physical device.
    /// </summary>
    /// <param name="request">Install request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Install result.</returns>
    public async Task<AppleAppInstallResult> InstallAsync(
        AppleAppInstallRequest request,
        CancellationToken cancellationToken = default)
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
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
            throw new ArgumentException("DeviceIdentifier or Device is required.", nameof(request));

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcrunExecutable, "xcrun"),
                Directory.GetCurrentDirectory(),
                new[] { "devicectl", "device", "install", "app", "--device", deviceIdentifier!, appPath },
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : request.Timeout),
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

    /// <summary>
    /// Launches an installed app on a physical device.
    /// </summary>
    /// <param name="request">Launch request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Launch result.</returns>
    public async Task<AppleAppLaunchResult> LaunchAsync(
        AppleAppLaunchRequest request,
        CancellationToken cancellationToken = default)
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
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(deviceIdentifier))
            throw new ArgumentException("DeviceIdentifier or Device is required.", nameof(request));

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.XcrunExecutable, "xcrun"),
                Directory.GetCurrentDirectory(),
                new[] { "devicectl", "device", "process", "launch", "--device", deviceIdentifier!, request.BundleIdentifier.Trim() },
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        return new AppleAppLaunchResult
        {
            DeviceIdentifier = deviceIdentifier!,
            BundleIdentifier = request.BundleIdentifier.Trim(),
            ProcessResult = result
        };
    }

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

        var build = await BuildAsync(request, cancellationToken).ConfigureAwait(false);
        var deployment = new AppleAppDeviceDeploymentResult
        {
            Build = build
        };

        if (!build.Succeeded)
            return deployment;

        var deployDeviceIdentifier = request.DeviceIdentifier ?? TryParseDestinationDeviceIdentifier(request.Destination);
        var install = await InstallAsync(new AppleAppInstallRequest
        {
            DeviceIdentifier = deployDeviceIdentifier,
            Device = request.Device,
            AppPath = build.AppPath,
            XcrunExecutable = request.XcrunExecutable,
            Timeout = request.Timeout
        }, cancellationToken).ConfigureAwait(false);
        deployment.Install = install;

        if (!install.Succeeded || !request.Launch)
            return deployment;

        var bundleIdentifier = string.IsNullOrWhiteSpace(request.BundleIdentifier)
            ? install.BundleIdentifier
            : request.BundleIdentifier!.Trim();
        if (string.IsNullOrWhiteSpace(bundleIdentifier))
            throw new InvalidOperationException("BundleIdentifier is required to launch and could not be parsed from the install output.");

        deployment.Launch = await LaunchAsync(new AppleAppLaunchRequest
        {
            DeviceIdentifier = deployDeviceIdentifier,
            Device = request.Device,
            BundleIdentifier = bundleIdentifier!,
            XcrunExecutable = request.XcrunExecutable,
            Timeout = request.Timeout
        }, cancellationToken).ConfigureAwait(false);

        return deployment;
    }

    internal static IReadOnlyList<AppleDeviceInfo> ParseDevices(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<AppleDeviceInfo>();

        var devices = new List<AppleDeviceInfo>();
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("----", StringComparison.Ordinal))
            {
                continue;
            }

            var match = DeviceLineRegex.Match(line);
            if (!match.Success)
                continue;

            devices.Add(new AppleDeviceInfo
            {
                Name = match.Groups["name"].Value.Trim(),
                Hostname = match.Groups["hostname"].Value.Trim(),
                Identifier = match.Groups["identifier"].Value.Trim(),
                State = match.Groups["state"].Value.Trim(),
                Model = match.Groups["model"].Value.Trim()
            });
        }

        return devices;
    }

    private async Task<string?> ResolveDeviceIdentifierAsync(
        string? deviceIdentifier,
        string? device,
        string xcrunExecutable,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(deviceIdentifier))
            return deviceIdentifier!.Trim();

        if (string.IsNullOrWhiteSpace(device))
            return null;

        var matches = await GetDevicesAsync(new AppleDeviceListRequest
        {
            XcrunExecutable = xcrunExecutable,
            Device = device,
            Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : timeout
        }, cancellationToken).ConfigureAwait(false);

        if (matches.Count == 0)
            throw new InvalidOperationException($"No available Apple device matched '{device}'.");
        if (matches.Count > 1)
            throw new InvalidOperationException($"Multiple available Apple devices matched '{device}'. Use DeviceIdentifier.");

        return matches[0].Identifier;
    }

    private async Task<MirrorResult> MirrorBuildRootAsync(
        string projectPath,
        AppleAppBuildRequest request,
        CancellationToken cancellationToken)
    {
        var sourceRoot = ResolveBuildRoot(projectPath, request.BuildRoot);
        var mirrorPath = ResolveBuildMirrorPath(request);
        var normalizedSourceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sourceRoot));
        var normalizedMirrorPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(mirrorPath));
        if (normalizedMirrorPath.StartsWith(normalizedSourceRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("BuildMirrorPath must not be inside the mirrored build root.");

        Directory.CreateDirectory(mirrorPath);

        var args = new List<string>
        {
            "-a",
            "--delete",
            "--exclude",
            ".git",
            "--exclude",
            ".build",
            "--exclude",
            ".swiftpm",
            "--exclude",
            "build",
            "--exclude",
            "DerivedData",
            normalizedSourceRoot,
            normalizedMirrorPath
        };

        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                NormalizeExecutable(request.RsyncExecutable, "rsync"),
                sourceRoot,
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout),
            cancellationToken).ConfigureAwait(false);

        return new MirrorResult(sourceRoot, mirrorPath, result);
    }

    private static string ResolveDestination(string? destination, string? deviceIdentifier, ApplePlatform platform)
    {
        if (!string.IsNullOrWhiteSpace(destination))
            return destination!.Trim();
        if (!string.IsNullOrWhiteSpace(deviceIdentifier))
            return $"id={deviceIdentifier!.Trim()}";

        return AppleAppArchiveService.GetGenericDestination(platform);
    }

    private static string ResolveDerivedDataPath(AppleAppBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DerivedDataPath))
            return Path.GetFullPath(request.DerivedDataPath!);

        var safeScheme = SanitizePathPart(request.Scheme);
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        return Path.Combine(Path.GetTempPath(), "powerforge-apple-derived-data", $"{safeScheme}-{uniqueSuffix}");
    }

    private static string ResolveAppPath(AppleAppBuildRequest request, string derivedDataPath)
    {
        if (!string.IsNullOrWhiteSpace(request.AppPath))
            return Path.GetFullPath(request.AppPath!);

        var productName = string.IsNullOrWhiteSpace(request.ProductName) ? request.Scheme.Trim() : request.ProductName!.Trim();
        return Path.Combine(derivedDataPath, "Build", "Products", GetProductDirectory(request), $"{productName}.app");
    }

    private static string GetProductDirectory(AppleAppBuildRequest request)
    {
        var configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Debug" : request.Configuration.Trim();
        return request.Platform == ApplePlatform.macOS
            ? configuration
            : $"{configuration}-{GetSdkProductSuffix(request.Platform)}";
    }

    private static string GetSdkProductSuffix(ApplePlatform platform)
        => platform switch
        {
            ApplePlatform.iOS => "iphoneos",
            ApplePlatform.iPadOS => "iphoneos",
            ApplePlatform.tvOS => "appletvos",
            ApplePlatform.watchOS => "watchos",
            ApplePlatform.visionOS => "xros",
            ApplePlatform.macOS => "macosx",
            _ => "iphoneos"
        };

    private static string ResolveBuildRoot(string projectPath, string? buildRoot)
    {
        if (!string.IsNullOrWhiteSpace(buildRoot))
            return Path.GetFullPath(buildRoot!);

        var root = Directory.Exists(projectPath)
            ? Path.GetDirectoryName(projectPath)
            : Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(root))
            return Directory.GetCurrentDirectory();

        return Path.GetFullPath(root!);
    }

    private static string ResolveBuildMirrorPath(AppleAppBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.BuildMirrorPath))
            return Path.GetFullPath(request.BuildMirrorPath!);

        var safeScheme = SanitizePathPart(request.Scheme);
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 12);
        return Path.Combine(Path.GetTempPath(), "powerforge-apple-build-mirror", $"{safeScheme}-{uniqueSuffix}");
    }

    private static string RewritePath(string path, string sourceRoot, string mirrorPath)
    {
        var fullPath = Path.GetFullPath(path);
        var fullSourceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(sourceRoot));
        if (!fullPath.StartsWith(fullSourceRoot, StringComparison.Ordinal))
            return fullPath;

        var relative = fullPath.Substring(fullSourceRoot.Length);
        return Path.Combine(mirrorPath, relative);
    }

    private static bool MatchesDevice(AppleDeviceInfo device, string filter)
        => string.Equals(device.Identifier, filter, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(device.Name, filter, StringComparison.OrdinalIgnoreCase) ||
           device.Model.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string? TryParseDestinationDeviceIdentifier(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return null;

        var trimmed = destination!.Trim();
        const string prefix = "id=";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var value = trimmed.Substring(prefix.Length).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? MatchValue(Regex regex, string output)
    {
        var match = regex.Match(output ?? string.Empty);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static InvalidOperationException CreateProcessException(ProcessRunResult result, string message)
    {
        var detail = string.Join(Environment.NewLine, new[] { result.StdErr, result.StdOut }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        var errorMessage = string.IsNullOrWhiteSpace(detail)
            ? $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}."
            : $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}.{Environment.NewLine}{detail}";

        return new InvalidOperationException(errorMessage);
    }

    private static string NormalizeExecutable(string? executable, string fallback)
    {
        var value = executable?.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "AppleApp" : sanitized;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private sealed class MirrorResult
    {
        public MirrorResult(string sourceRoot, string mirrorPath, ProcessRunResult processResult)
        {
            SourceRoot = sourceRoot;
            MirrorPath = mirrorPath;
            ProcessResult = processResult;
        }

        public string SourceRoot { get; }

        public string MirrorPath { get; }

        public ProcessRunResult ProcessResult { get; }
    }
}
