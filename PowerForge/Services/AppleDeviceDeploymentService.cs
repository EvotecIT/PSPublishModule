using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Builds, installs, launches, and discovers Apple devices through xcodebuild and xcrun devicectl.
/// </summary>
public sealed partial class AppleDeviceDeploymentService
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
        => await GetDevicesCoreAsync(
            request,
            requireTrustedSystemTool: false,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Builds an Apple app for local device installation.
    /// </summary>
    /// <param name="request">Build request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Build result.</returns>
    public async Task<AppleAppBuildResult> BuildAsync(
        AppleAppBuildRequest request,
        CancellationToken cancellationToken = default)
        => (await BuildCoreAsync(
            request,
            bindProductForDeployment: false,
            cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>
    /// Builds an app while retaining an exact product identity through the
    /// deployment consumer boundary.
    /// </summary>
    internal Task<AppleAppBuildOperation> BuildForDeploymentAsync(
        AppleAppBuildRequest request,
        CancellationToken cancellationToken = default)
        => BuildCoreAsync(
            request,
            bindProductForDeployment: true,
            cancellationToken);

    private async Task<AppleAppBuildOperation> BuildCoreAsync(
        AppleAppBuildRequest request,
        bool bindProductForDeployment,
        CancellationToken cancellationToken)
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

        // The local exact-source build surface is intentionally closed. Every
        // build input must be represented by tracked Xcode metadata that the
        // source-trust validator can inspect.
        AppleBuildProvenance.RejectLocalBuildAdditionalArguments(
            request.AdditionalArguments);
        if (bindProductForDeployment && !string.IsNullOrWhiteSpace(request.AppPath))
        {
            throw new InvalidOperationException(
                "Exact-source Apple deployment does not accept AppPath because xcodebuild does not produce a caller-selected path. " +
                "Use ProductName when the built app name differs from Scheme.");
        }
        var xcodeBuildExecutable = AppleTrustedExecutionEnvironment.ResolveSystemTool(
            request.XcodeBuildExecutable,
            "xcodebuild",
            "/usr/bin/xcodebuild",
            "Exact-source local Apple builds");
        var rsyncExecutable = request.UseBuildMirror
            ? AppleTrustedExecutionEnvironment.ResolveSystemTool(
                request.RsyncExecutable,
                "rsync",
                "/usr/bin/rsync",
                "Exact-source local Apple build mirroring")
            : null;

        var deviceIdentifier = await ResolveDeviceIdentifierAsync(
            request.DeviceIdentifier,
            request.Device,
            request.XcrunExecutable,
            request.Timeout,
            requireTrustedSystemTool: true,
            cancellationToken).ConfigureAwait(false);

        var destination = ResolveDestination(request.Destination, deviceIdentifier, request.Platform, request.ArchiveVariant);
        var derivedDataPath = ResolveDerivedDataPath(request);
        var deploymentProductRoot = bindProductForDeployment
            ? Path.Combine(
                Path.GetTempPath(),
                "PowerForge",
                "apple-local-products",
                Guid.NewGuid().ToString("N"))
            : null;
        var productDirectory = deploymentProductRoot ?? Path.Combine(
            derivedDataPath,
            "Build",
            "Products",
            GetProductDirectory(request));
        var appPath = deploymentProductRoot is null
            ? ResolveAppPath(request, derivedDataPath)
            : Path.Combine(productDirectory, ResolveProductName(request) + ".app");
        var buildProjectPath = projectPath;
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string? mirrorPath = null;
        if (!string.IsNullOrWhiteSpace(request.BuildRoot))
        {
            var declaredBuildRoot = Path.GetFullPath(request.BuildRoot!);
            EnsurePathWithinBuildRoot(
                projectPath,
                declaredBuildRoot,
                FrameworkCompatibility.GetPathStringComparisonForPath(
                    declaredBuildRoot));
        }
        var sourceRoot = ResolveBuildRoot(projectPath, request.BuildRoot);
        var sourcePathComparison =
            FrameworkCompatibility.GetPathStringComparisonForPath(sourceRoot);
        EnsurePathWithinBuildRoot(
            projectPath,
            sourceRoot,
            sourcePathComparison);
        EnsureOutputPathOutsideBuildRoot(
            derivedDataPath,
            sourceRoot,
            nameof(request.DerivedDataPath),
            sourcePathComparison);
        EnsureOutputPathOutsideBuildRoot(
            appPath,
            sourceRoot,
            nameof(request.AppPath),
            sourcePathComparison);
        AppleBuildProvenance.Snapshot sourceSnapshot;
        AppleReleaseSourceMutationMonitor buildInputMonitor;
        AppleReleaseSourceMutationMonitor? liveSourceMonitor = null;

        if (request.UseBuildMirror)
        {
            var sourceMonitor = new AppleReleaseSourceMutationMonitor(
                sourceRoot,
                "local Apple source",
                "rsync",
                "Discard the build mirror and retry from a stable working tree.",
                ignoredMutation: args =>
                    AppleBuildProvenance.IsGitMetadataMutation(
                        args,
                        sourceRoot,
                        sourcePathComparison));
            try
            {
                sourceSnapshot = AppleBuildProvenance.CaptureBuildInputs(
                    sourceRoot,
                    excludesGeneratedDirectories: true);
                AppleBuildProvenance.ValidateXcodeBuildInputsWithinSource(
                    sourceRoot,
                    projectPath,
                    request.Scheme);
                var mirror = await MirrorBuildRootAsync(
                    projectPath,
                    request,
                    rsyncExecutable!,
                    sourcePathComparison,
                    cancellationToken).ConfigureAwait(false);
                if (!mirror.ProcessResult.Succeeded)
                {
                    sourceMonitor.Dispose();
                    return new AppleAppBuildOperation(
                        new AppleAppBuildResult
                        {
                            AppPath = appPath,
                            Destination = destination,
                            DerivedDataPath = derivedDataPath,
                            BuildMirrorPath = mirror.MirrorPath,
                            SourceRevision = sourceSnapshot.Revision,
                            ProcessResult = mirror.ProcessResult
                        },
                        productSnapshot: null,
                        ownedBuildOutputRoot: deploymentProductRoot);
                }

                var mirrorMonitor = mirror.MutationMonitor ?? throw new InvalidOperationException(
                    "The Apple build mirror completed without an active source monitor.");
                AppleReleaseSourceMutationMonitor? continuedSourceMonitor = null;
                try
                {
                    continuedSourceMonitor = new AppleReleaseSourceMutationMonitor(
                        sourceRoot,
                        "local Apple source",
                        "xcodebuild",
                        "Discard the product and rebuild from a stable working tree.",
                        ignoredMutation: args =>
                            AppleBuildProvenance.IsGitMetadataMutation(
                                args,
                                sourceRoot,
                                sourcePathComparison));
                    sourceMonitor.ValidateNoChanges(
                        () => AppleBuildProvenance.ValidateUnchanged(sourceSnapshot));
                    sourceMonitor.Dispose();

                    buildProjectPath = RewritePath(
                        projectPath,
                        mirror.SourceRoot,
                        mirror.MirrorPath,
                        sourcePathComparison);
                    workingDirectory = mirror.MirrorPath;
                    mirrorPath = mirror.MirrorPath;
                    buildInputMonitor = mirrorMonitor;
                    liveSourceMonitor = continuedSourceMonitor;
                }
                catch
                {
                    continuedSourceMonitor?.Dispose();
                    mirrorMonitor.Dispose();
                    throw;
                }
            }
            catch
            {
                sourceMonitor.Dispose();
                throw;
            }
        }
        else
        {
            var sourceMonitor = new AppleReleaseSourceMutationMonitor(
                sourceRoot,
                "local Apple source",
                "xcodebuild",
                "Discard the product and rebuild from a stable working tree.",
                ignoredMutation: args =>
                    AppleBuildProvenance.IsGitMetadataMutation(
                        args,
                        sourceRoot,
                        sourcePathComparison));
            try
            {
                sourceSnapshot = AppleBuildProvenance.CaptureBuildInputs(
                    sourceRoot,
                    excludesGeneratedDirectories: false);
                AppleBuildProvenance.ValidateXcodeBuildInputsWithinSource(
                    sourceRoot,
                    projectPath,
                    request.Scheme);
                buildInputMonitor = sourceMonitor;
            }
            catch
            {
                sourceMonitor.Dispose();
                throw;
            }
        }
        using var buildInputMonitorLease = buildInputMonitor;
        using var liveSourceMonitorLease = liveSourceMonitor;

        Directory.CreateDirectory(derivedDataPath);
        if (deploymentProductRoot is not null)
        {
            Directory.CreateDirectory(deploymentProductRoot);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    deploymentProductRoot,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
#endif
        }

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
        if (deploymentProductRoot is not null)
            args.Add($"CONFIGURATION_BUILD_DIR={deploymentProductRoot}");

        args.Add("build");
        args.AddRange(AppleBuildProvenance.AppendXcodeBuildSetting(
            request.AdditionalArguments,
            sourceSnapshot.Revision));

        AppleBuiltAppSnapshot? productSnapshot = null;
        AppleSwiftPackageBuildSnapshot? packageSnapshot = null;

        try
        {
            var approvedPackageRevisions =
                AppleSwiftPackageBuildSnapshot.ReadApprovedRemotePackages(
                    projectPath);
            if (approvedPackageRevisions.Count > 0)
            {
                packageSnapshot = await AppleSwiftPackageBuildSnapshot.CreateAsync(
                        _processRunner,
                        xcodeBuildExecutable,
                        projectPath,
                        request.IsWorkspace,
                        request.Scheme.Trim(),
                        approvedPackageRevisions,
                        request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                packageSnapshot.AppendLocalBuildArguments(args);
            }

            var processRequest = AppleTrustedExecutionEnvironment.CreateProcessRequest(
                xcodeBuildExecutable,
                "xcodebuild",
                "/usr/bin/xcodebuild",
                "Exact-source local Apple builds",
                workingDirectory,
                args,
                request.Timeout <= TimeSpan.Zero ? TimeSpan.FromHours(1) : request.Timeout,
                isolateGitConfiguration: packageSnapshot is not null);
            if (bindProductForDeployment)
            {
                processRequest.SetCompletionBoundary(completionResult =>
                {
                    if (!completionResult.Succeeded)
                        return;
                    var producedAppPath = ResolveBuiltAppPath(
                        request,
                        productDirectory,
                        appPath);
                    productSnapshot = AppleBuiltAppSnapshot.Create(producedAppPath);
                });
            }

            var result = await _processRunner.RunAsync(
                processRequest,
                cancellationToken).ConfigureAwait(false);
            processRequest.InvokeCompletionBoundary(result);

            var resolvedAppPath = result.Succeeded
                ? ResolveBuiltAppPath(request, productDirectory, appPath)
                : appPath;

            if (result.Succeeded)
            {
                packageSnapshot?.ValidateUnchanged();
                buildInputMonitor.ValidateNoChanges(
                    request.UseBuildMirror
                        ? null
                        : () => AppleBuildProvenance.ValidateUnchanged(sourceSnapshot));
                liveSourceMonitor?.ValidateNoChanges(
                    () => AppleBuildProvenance.ValidateUnchanged(sourceSnapshot));
                if (bindProductForDeployment && productSnapshot is null)
                {
                    throw new InvalidOperationException(
                        "xcodebuild completed without binding the built app product at its process completion boundary.");
                }
            }
            else
            {
                productSnapshot?.Dispose();
                productSnapshot = null;
            }

            return new AppleAppBuildOperation(
                new AppleAppBuildResult
                {
                    AppPath = resolvedAppPath,
                    Destination = destination,
                    DerivedDataPath = derivedDataPath,
                    BuildMirrorPath = mirrorPath,
                    SourceRevision = sourceSnapshot.Revision,
                    ProcessResult = result
                },
                productSnapshot,
                deploymentProductRoot);
        }
        catch
        {
            productSnapshot?.Dispose();
            if (deploymentProductRoot is not null)
            {
                try { AppleArtifactCopy.DeleteOwnedDirectory(deploymentProductRoot); } catch { /* best effort private cleanup */ }
            }
            throw;
        }
        finally
        {
            packageSnapshot?.Dispose();
        }
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
        => await InstallCoreAsync(
            request,
            requireTrustedSystemTool: false,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Launches an installed app on a physical device.
    /// </summary>
    /// <param name="request">Launch request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Launch result.</returns>
    public async Task<AppleAppLaunchResult> LaunchAsync(
        AppleAppLaunchRequest request,
        CancellationToken cancellationToken = default)
        => await LaunchCoreAsync(
            request,
            requireTrustedSystemTool: false,
            cancellationToken).ConfigureAwait(false);

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

        var deployDeviceIdentifier = request.DeviceIdentifier ?? TryParseDestinationDeviceIdentifier(request.Destination);
        var install = await InstallCoreAsync(new AppleAppInstallRequest
        {
            DeviceIdentifier = deployDeviceIdentifier,
            Device = request.Device,
            AppPath = buildOperation.ProductSnapshot?.AppPath ?? build.AppPath,
            XcrunExecutable = request.XcrunExecutable,
            Timeout = request.Timeout
        }, requireTrustedSystemTool: true, cancellationToken).ConfigureAwait(false);
        buildOperation.ProductSnapshot?.ValidateUnchanged();
        install.AppPath = build.AppPath;
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
            Device = request.Device,
            BundleIdentifier = bundleIdentifier!,
            XcrunExecutable = request.XcrunExecutable,
            EnvironmentVariables = new Dictionary<string, string>(request.LaunchEnvironment, StringComparer.Ordinal),
            Arguments = request.LaunchArguments,
            TerminateExisting = request.TerminateExisting,
            Timeout = request.Timeout
        }, requireTrustedSystemTool: true, cancellationToken).ConfigureAwait(false);

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
        bool requireTrustedSystemTool,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(deviceIdentifier))
            return deviceIdentifier!.Trim();

        if (string.IsNullOrWhiteSpace(device))
            return null;

        var matches = await GetDevicesCoreAsync(new AppleDeviceListRequest
        {
            XcrunExecutable = xcrunExecutable,
            Device = device,
            Timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : timeout
        }, requireTrustedSystemTool, cancellationToken).ConfigureAwait(false);

        if (matches.Count == 0)
            throw new InvalidOperationException($"No available Apple device matched '{device}'.");
        if (matches.Count > 1)
            throw new InvalidOperationException($"Multiple available Apple devices matched '{device}'. Use DeviceIdentifier.");

        return matches[0].Identifier;
    }

    private static string ResolveDestination(
        string? destination,
        string? deviceIdentifier,
        ApplePlatform platform,
        AppleArchiveVariant archiveVariant)
    {
        if (!string.IsNullOrWhiteSpace(destination))
            return destination!.Trim();
        if (!string.IsNullOrWhiteSpace(deviceIdentifier))
            return $"id={deviceIdentifier!.Trim()}";

        return AppleAppArchiveService.GetGenericDestination(platform, archiveVariant);
    }

    internal static string ResolveBuildRoot(string projectPath, string? buildRoot)
    {
        var requestedRoot = !string.IsNullOrWhiteSpace(buildRoot)
            ? Path.GetFullPath(buildRoot!)
            : projectPath;
        var repositoryRoot = AppleBuildProvenance.ResolveRepositoryRoot(
            requestedRoot);
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
            return repositoryRoot!;
        if (!string.IsNullOrWhiteSpace(buildRoot))
            return requestedRoot;

        var root = Directory.Exists(projectPath)
            ? Path.GetDirectoryName(projectPath)
            : Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (string.IsNullOrWhiteSpace(root))
            return Directory.GetCurrentDirectory();

        return Path.GetFullPath(root!);
    }

    internal static void EnsurePathWithinBuildRoot(
        string projectPath,
        string sourceRoot,
        StringComparison sourcePathComparison)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var fullSourceRoot = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = EnsureTrailingDirectorySeparator(fullSourceRoot);
        if (!fullProjectPath.Equals(fullSourceRoot, sourcePathComparison) &&
            !fullProjectPath.StartsWith(prefix, sourcePathComparison))
        {
            throw new InvalidOperationException(
                $"ProjectPath '{fullProjectPath}' must be contained by BuildRoot '{fullSourceRoot}'.");
        }

        var current = fullProjectPath;
        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"ProjectPath must not traverse a symbolic link or reparse point: '{current}'.");
            }
            if (current.Equals(fullSourceRoot, sourcePathComparison))
                break;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException(
                    $"Unable to verify ProjectPath '{fullProjectPath}' against BuildRoot '{fullSourceRoot}'.");
        }
    }

    internal static void EnsureOutputPathOutsideBuildRoot(
        string outputPath,
        string sourceRoot,
        string parameterName,
        StringComparison sourcePathComparison)
    {
        var fullOutputPath = AppleReleaseArtifactService.ResolvePhysicalPath(outputPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullSourceRoot = AppleReleaseArtifactService.ResolvePhysicalPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullSourceRoot.Equals(fullOutputPath, sourcePathComparison) ||
            fullSourceRoot.StartsWith(
                EnsureTrailingDirectorySeparator(fullOutputPath),
                sourcePathComparison) ||
            fullOutputPath.StartsWith(
                EnsureTrailingDirectorySeparator(fullSourceRoot),
                sourcePathComparison))
        {
            throw new InvalidOperationException(
                $"{parameterName} '{fullOutputPath}' must be outside BuildRoot '{fullSourceRoot}'.");
        }
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

}
