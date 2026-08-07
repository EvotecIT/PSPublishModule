using System.Security.Cryptography;
using System.Text;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleDeployUsage =
        "Usage: powerforge apple-deploy [--config <powerforge.release.json>] " +
        "[--platform <iOS|iPadOS|watchOS|macOS|tvOS|visionOS>] [--target <name-or-scheme>] " +
        "[--device <name>|--device-id <id>] [--profile <name>] [--configuration <Debug|Release>] " +
        "[--install-root </Applications>] [--build-mirror|--no-build-mirror] [--launch|--no-launch] " +
        "[--plan] [--output json]";

    private static int CommandAppleDeploy(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Any(static value => value.Equals("-h", StringComparison.OrdinalIgnoreCase) || value.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "apple-deploy",
                    Success = true,
                    ExitCode = 0,
                    Result = System.Text.Json.JsonSerializer.SerializeToElement(new { usage = AppleDeployUsage })
                });
            }
            else
            {
                Console.WriteLine(AppleDeployUsage);
            }
            return 0;
        }

        try
        {
            ValidateAppleDeployArguments(argv);
            var configPath = TryGetOptionValue(argv, "--config") ?? FindDefaultReleaseConfig(Directory.GetCurrentDirectory());
            if (string.IsNullOrWhiteSpace(configPath))
                throw new ArgumentException("Missing --config and no default release config found.");

            var (release, fullConfigPath) = LoadPowerForgeReleaseSpecWithPath(configPath);
            var apple = release.AppleApps ?? throw new InvalidOperationException("Release config has no AppleApps section.");
            var local = apple.LocalDeployment;
            var requestedPlatform = ParseAppleDeployPlatform(TryGetOptionValue(argv, "--platform"), local.DefaultPlatform);
            var selectedTarget = SelectAppleDeployTarget(
                apple.Apps,
                requestedPlatform,
                TryGetOptionValue(argv, "--target"));
            var scheme = selectedTarget.Scheme?.Trim();
            if (string.IsNullOrWhiteSpace(scheme))
                throw new InvalidOperationException($"Apple target '{selectedTarget.Name}' has no Scheme.");

            var configDirectory = Path.GetDirectoryName(fullConfigPath) ?? Directory.GetCurrentDirectory();
            var projectRoot = ResolvePathFromBase(configDirectory, string.IsNullOrWhiteSpace(apple.ProjectRoot) ? "." : apple.ProjectRoot!);
            var projectPath = ResolvePathFromBase(projectRoot, selectedTarget.ProjectPath);
            if (!Directory.Exists(projectPath) && !File.Exists(projectPath))
                throw new FileNotFoundException("Xcode project or workspace was not found.", projectPath);

            var profile = SelectAppleDeployProfile(local, TryGetOptionValue(argv, "--profile"));
            var configuration = TryGetOptionValue(argv, "--configuration") ?? local.Configuration;
            var deviceIdentifier = TryGetOptionValue(argv, "--device-id");
            var device = TryGetOptionValue(argv, "--device") ??
                         (requestedPlatform == ApplePlatform.macOS ? null : local.DefaultDevice);
            if (!string.IsNullOrWhiteSpace(deviceIdentifier) && argv.Any(static value => value.Equals("--device", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Use either --device or --device-id, not both.");
            if (requestedPlatform == ApplePlatform.macOS && (!string.IsNullOrWhiteSpace(device) || !string.IsNullOrWhiteSpace(deviceIdentifier)))
                throw new ArgumentException("--device and --device-id apply to device platforms, not macOS deployment.");

            var launch = ResolveAppleDeployFlag(argv, "--launch", "--no-launch", local.Launch);
            var useBuildMirror = ResolveAppleDeployFlag(argv, "--build-mirror", "--no-build-mirror", local.UseBuildMirror);
            var localRoot = ResolveStableAppleDeployRoot(projectRoot, requestedPlatform, scheme);
            var derivedDataPath = Path.Combine(localRoot, "DerivedData");
            var buildMirrorPath = useBuildMirror ? Path.Combine(localRoot, "Source") : null;
            var installRoot = TryGetOptionValue(argv, "--install-root") ?? local.InstallRoot;
            var planOnly = argv.Any(static value => value.Equals("--plan", StringComparison.OrdinalIgnoreCase));

            var cliResult = new AppleLocalDeploymentCliResult
            {
                Planned = planOnly,
                Target = selectedTarget.Name ?? scheme,
                Platform = requestedPlatform,
                ArchiveVariant = selectedTarget.ArchiveVariant,
                Configuration = configuration,
                Profile = profile?.Name,
                ProjectPath = projectPath,
                Scheme = scheme,
                DerivedDataPath = derivedDataPath,
                Device = deviceIdentifier ?? device,
                InstallRoot = requestedPlatform == ApplePlatform.macOS ? Path.GetFullPath(installRoot) : null,
                Launch = launch,
                UseBuildMirror = useBuildMirror,
                BuildMirrorPath = buildMirrorPath
            };

            if (!planOnly)
            {
                using var deploymentLock = AppleLocalDeploymentLock.Acquire(localRoot, $"build cache '{localRoot}'");
                ExecuteAppleDeploy(cliResult, apple, selectedTarget, projectRoot, profile, device, deviceIdentifier);
            }
            else
                cliResult.Success = true;

            return WriteAppleDeployResult(cliResult, fullConfigPath, outputJson, logger);
        }
        catch (Exception exception)
        {
            return WriteReleaseError(outputJson, "apple-deploy", 1, exception.Message, logger);
        }
    }

    private static void ExecuteAppleDeploy(
        AppleLocalDeploymentCliResult cliResult,
        PowerForgeAppleReleaseOptions apple,
        AppleAppConfiguration selectedTarget,
        string projectRoot,
        PowerForgeAppleLocalDeploymentProfile? profile,
        string? device,
        string? deviceIdentifier)
    {
        var environment = profile is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(profile.Environment, StringComparer.Ordinal);
        var arguments = profile?.Arguments ?? Array.Empty<string>();
        var isWorkspace = selectedTarget.ProjectPath.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase);

        if (cliResult.Platform == ApplePlatform.macOS)
        {
            var request = new AppleMacAppDeploymentRequest
            {
                ProjectPath = cliResult.ProjectPath,
                IsWorkspace = isWorkspace,
                Scheme = cliResult.Scheme,
                ProductName = selectedTarget.ProductName,
                Configuration = cliResult.Configuration,
                Platform = ApplePlatform.macOS,
                ArchiveVariant = selectedTarget.ArchiveVariant,
                DerivedDataPath = cliResult.DerivedDataPath,
                XcodeBuildExecutable = apple.XcodeBuildExecutable,
                AllowProvisioningUpdates = apple.AllowProvisioningUpdates,
                UseBuildMirror = cliResult.UseBuildMirror,
                BuildRoot = projectRoot,
                BuildMirrorPath = cliResult.BuildMirrorPath,
                InstallRoot = cliResult.InstallRoot!,
                Launch = cliResult.Launch,
                LaunchEnvironment = environment,
                LaunchArguments = arguments
            };
            var deployment = new AppleMacAppDeploymentService().DeployAsync(request).GetAwaiter().GetResult();
            cliResult.AppPath = deployment.Build.AppPath;
            cliResult.InstalledAppPath = deployment.Install?.InstalledAppPath;
            cliResult.BuildSucceeded = deployment.Build.Succeeded;
            cliResult.InstallSucceeded = deployment.Install?.Succeeded ?? false;
            cliResult.LaunchSucceeded = deployment.Launch?.Succeeded;
            cliResult.Success = deployment.Succeeded;
            cliResult.Warning = deployment.Install?.Warning;
            cliResult.Diagnostic = ResolveAppleDeployDiagnostic(
                deployment.Build.ProcessResult,
                deployment.Install?.ProcessResult,
                deployment.Launch?.ProcessResult);
            return;
        }

        var deviceRequest = new AppleAppDeviceDeploymentRequest
        {
            ProjectPath = cliResult.ProjectPath,
            IsWorkspace = isWorkspace,
            Scheme = cliResult.Scheme,
            ProductName = selectedTarget.ProductName,
            Configuration = cliResult.Configuration,
            Platform = selectedTarget.Platform,
            ArchiveVariant = selectedTarget.ArchiveVariant,
            Device = device,
            DeviceIdentifier = deviceIdentifier,
            BundleIdentifier = selectedTarget.BundleId,
            Launch = cliResult.Launch,
            LaunchEnvironment = environment,
            LaunchArguments = arguments,
            TerminateExisting = cliResult.Launch,
            DerivedDataPath = cliResult.DerivedDataPath,
            XcodeBuildExecutable = apple.XcodeBuildExecutable,
            AllowProvisioningUpdates = apple.AllowProvisioningUpdates,
            UseBuildMirror = cliResult.UseBuildMirror,
            BuildRoot = projectRoot,
            BuildMirrorPath = cliResult.BuildMirrorPath
        };
        var deviceDeployment = new AppleDeviceDeploymentService().DeployAsync(deviceRequest).GetAwaiter().GetResult();
        cliResult.AppPath = deviceDeployment.Build.AppPath;
        cliResult.DeviceIdentifier = deviceDeployment.Install?.DeviceIdentifier;
        cliResult.BuildSucceeded = deviceDeployment.Build.Succeeded;
        cliResult.InstallSucceeded = deviceDeployment.Install?.Succeeded ?? false;
        cliResult.LaunchSucceeded = deviceDeployment.Launch?.Succeeded;
        cliResult.DeviceLocked = deviceDeployment.Launch?.DeviceLocked ?? false;
        cliResult.Success = deviceDeployment.RequestedStagesSucceeded;
        cliResult.Diagnostic = ResolveAppleDeployDiagnostic(
            deviceDeployment.Build.ProcessResult,
            deviceDeployment.Install?.ProcessResult,
            deviceDeployment.Launch?.ProcessResult);
    }

    private static AppleAppConfiguration SelectAppleDeployTarget(
        IEnumerable<AppleAppConfiguration> configuredTargets,
        ApplePlatform requestedPlatform,
        string? requestedTarget)
    {
        var targetPlatform = requestedPlatform == ApplePlatform.iPadOS ? ApplePlatform.iOS : requestedPlatform;
        var candidates = configuredTargets
            .Where(static target => target.Enabled && target.ProductRole is AppleProductRole.PrimaryApp or AppleProductRole.CompanionApp)
            .Where(target => target.Platform == targetPlatform)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(requestedTarget))
        {
            candidates = candidates.Where(target =>
                    string.Equals(target.Name, requestedTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(target.Scheme, requestedTarget, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (candidates.Length == 0)
            throw new InvalidOperationException($"No deployable Apple target matched platform '{requestedPlatform}'{FormatAppleDeployTargetSuffix(requestedTarget)}.");
        if (candidates.Length > 1)
        {
            var choices = string.Join(", ", candidates.Select(target => target.Name ?? target.Scheme ?? "unnamed"));
            throw new InvalidOperationException($"Multiple Apple targets matched platform '{requestedPlatform}': {choices}. Use --target.");
        }
        return candidates[0];
    }

    private static PowerForgeAppleLocalDeploymentProfile? SelectAppleDeployProfile(
        PowerForgeAppleLocalDeploymentOptions options,
        string? requestedProfile)
    {
        var name = requestedProfile ?? options.DefaultProfile;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var matches = options.Profiles
            .Where(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Local deployment profile '{name}' is not configured exactly once.");
        return matches[0];
    }

    private static ApplePlatform ParseAppleDeployPlatform(string? value, ApplePlatform fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!Enum.TryParse<ApplePlatform>(value, ignoreCase: true, out var platform) || !Enum.IsDefined(platform))
            throw new ArgumentException($"Unknown Apple platform '{value}'.");
        return platform;
    }

    private static bool ResolveAppleDeployFlag(string[] argv, string enabledFlag, string disabledFlag, bool fallback)
    {
        var enabled = argv.Any(value => value.Equals(enabledFlag, StringComparison.OrdinalIgnoreCase));
        var disabled = argv.Any(value => value.Equals(disabledFlag, StringComparison.OrdinalIgnoreCase));
        if (enabled && disabled)
            throw new ArgumentException($"Use either {enabledFlag} or {disabledFlag}, not both.");
        return enabled || (!disabled && fallback);
    }

    private static string ResolveStableAppleDeployRoot(string projectRoot, ApplePlatform platform, string scheme)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectRoot))))[..12].ToLowerInvariant();
        var safeScheme = new string(scheme.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        return Path.Combine(Path.GetTempPath(), "powerforge-apple-local", hash, platform.ToString(), safeScheme);
    }

    private static int WriteAppleDeployResult(
        AppleLocalDeploymentCliResult result,
        string configPath,
        bool outputJson,
        ILogger logger)
    {
        var exitCode = result.Success ? 0 : 1;
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "apple-deploy",
                Success = result.Success,
                ExitCode = exitCode,
                Config = "release",
                ConfigPath = configPath,
                Result = CliJson.SerializeToElement(result, CliJson.Context.AppleLocalDeploymentCliResult)
            });
            return exitCode;
        }

        logger.Info($"Target: {result.Target} ({result.Platform}, {result.Configuration})");
        if (!string.IsNullOrWhiteSpace(result.Profile))
            logger.Info($"Profile: {result.Profile}");
        if (result.Planned)
        {
            logger.Success("Apple local deployment plan is valid.");
            return 0;
        }
        var appPath = result.InstalledAppPath ?? result.AppPath;
        if (!string.IsNullOrWhiteSpace(appPath))
            logger.Info($"App: {appPath}");
        if (result.Success)
            logger.Success(result.Launch ? "Apple app installed and launched." : "Apple app installed.");
        else if (result.DeviceLocked && result.InstallSucceeded == true)
            logger.Warn("Apple app installed, but launch was deferred because the device is locked.");
        else
            logger.Error("Apple local deployment failed.");
        return exitCode;
    }

    private static void ValidateAppleDeployArguments(string[] argv)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--build-mirror", "--no-build-mirror", "--launch", "--no-launch", "--plan"
        };
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--config", "--platform", "--target", "--device", "--device-id", "--profile",
            "--configuration", "--install-root", "--output"
        };
        for (var index = 0; index < argv.Length; index++)
        {
            var argument = argv[index];
            if (flags.Contains(argument))
                continue;
            if (!options.Contains(argument))
                throw new ArgumentException($"Unknown apple-deploy option '{argument}'.");
            if (++index >= argv.Length || argv[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for apple-deploy option '{argument}'.");
        }
    }

    private static string FormatAppleDeployTargetSuffix(string? target)
        => string.IsNullOrWhiteSpace(target) ? string.Empty : $" and target '{target}'";

    private static string? ResolveAppleDeployDiagnostic(params ProcessRunResult?[] stages)
    {
        var failed = stages.FirstOrDefault(static stage => stage is not null && !stage.Succeeded);
        if (failed is null)
            return null;
        var text = string.IsNullOrWhiteSpace(failed.StdErr) ? failed.StdOut : failed.StdErr;
        if (string.IsNullOrWhiteSpace(text))
            return $"{failed.Executable} exited with code {failed.ExitCode}.";
        var compact = text.Trim();
        return compact.Length <= 2000 ? compact : compact[^2000..];
    }
}
