using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string ReleaseUsage =
        "Usage: powerforge release [--config <release.json>] [--plan] [--validate] [--packages-only] [--module-only] [--tools-only] [--configuration <Release|Debug>] [--module-no-dotnet-build] [--module-version <version>] [--module-prerelease-tag <tag>] [--module-no-sign] [--module-sign] [--skip-workspace-validation] [--workspace-config <workspace.validation.json>] [--workspace-profile <name>] [--workspace-testimox-root <path>] [--workspace-enable-feature <name[,name...]>] [--workspace-disable-feature <name[,name...]>] [--publish-nuget] [--publish-project-github] [--publish-tool-github] [--skip-restore] [--skip-build] [--output-root <path>] [--stage-root <path>] [--manifest-json <path>] [--checksums-path <path>] [--skip-release-checksums] [--keep-symbols] [--sign] [--sign-profile <name>] [--sign-tool-path <path>] [--sign-thumbprint <sha1>] [--sign-subject-name <name>] [--sign-on-missing-tool <Warn|Fail|Skip>] [--sign-on-failure <Warn|Fail|Skip>] [--sign-timestamp-url <url>] [--sign-description <text>] [--sign-url <url>] [--sign-csp <name>] [--sign-key-container <name>] [--package-sign-thumbprint <sha1>] [--package-sign-store <CurrentUser|LocalMachine>] [--package-sign-timestamp-url <url>] [--installer-property <Name=Value>] [--tool-output <Tool|Portable|Installer|Store>[,<...>]] [--skip-tool-output <Tool|Portable|Installer|Store>[,<...>]] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|FrameworkDependent|AotSpeed|AotSize>[,<...>]] [--flavor <SingleContained|SingleFx|Portable|Fx>[,<...>]] [--output json]";

    private static int CommandRelease(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "release",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = ReleaseUsage })
                });
            }
            else
            {
                Console.WriteLine(ReleaseUsage);
            }

            return 0;
        }

        var planOnly = argv.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var validateOnly = argv.Any(a => a.Equals("--validate", StringComparison.OrdinalIgnoreCase));
        var packagesOnly = argv.Any(a => a.Equals("--packages-only", StringComparison.OrdinalIgnoreCase));
        var moduleOnly = argv.Any(a => a.Equals("--module-only", StringComparison.OrdinalIgnoreCase));
        var toolsOnly = argv.Any(a => a.Equals("--tools-only", StringComparison.OrdinalIgnoreCase));

        var scopedCount = (packagesOnly ? 1 : 0) + (moduleOnly ? 1 : 0) + (toolsOnly ? 1 : 0);
        if (scopedCount > 1)
        {
            return WriteReleaseError(outputJson, "release", 2, "Use at most one of --packages-only, --module-only, or --tools-only.", logger);
        }

        var configPath = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultReleaseConfig(Directory.GetCurrentDirectory());

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "release",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default release config found."
                });
                return 2;
            }

            Console.WriteLine(ReleaseUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadPowerForgeReleaseSpecWithPath(configPath);
            var spec = loaded.Value;
            var fullConfigPath = loaded.FullPath;

            var request = BuildReleaseRequestFromArgs(argv, fullConfigPath, planOnly, validateOnly, packagesOnly, moduleOnly, toolsOnly);

            var service = new PowerForgeReleaseService(cmdLogger);
            var result = RunWithStatus(outputJson, cli, "Running unified release workflow", () => service.Execute(spec, request));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "release",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Error = result.ErrorMessage,
                    Config = "release",
                    ConfigPath = fullConfigPath,
                    Spec = CliJson.SerializeToElement(spec, CliJson.Context.PowerForgeReleaseSpec),
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerForgeReleaseResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (!result.Success)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Release workflow failed.");
                return exitCode;
            }

            if (result.Packages is not null)
            {
                var release = result.Packages.Result.Release;
                var packageCount = release?.Projects?.Count(project => project.IsPackable) ?? 0;
                cmdLogger.Success($"Packages: {(planOnly || validateOnly ? "planned" : "completed")} ({packageCount} project(s)).");
                if (!string.IsNullOrWhiteSpace(result.Packages.PlanOutputPath))
                    cmdLogger.Info($"Package plan: {result.Packages.PlanOutputPath}");
            }

            if (spec.Module is not null)
            {
                var moduleAssetCount = result.ModulePlan?.ArtifactPaths?.Length ?? result.ModuleAssets?.Length ?? 0;
                cmdLogger.Success($"Module: {(planOnly || validateOnly ? "planned" : "completed")} ({moduleAssetCount} asset path(s)).");
                if (result.ModulePlan is not null)
                {
                    cmdLogger.Info($"Module script: {result.ModulePlan.ScriptPath}");
                    if (!string.IsNullOrWhiteSpace(result.ModulePlan.ModuleVersion))
                        cmdLogger.Info($"Module version override: {result.ModulePlan.ModuleVersion}");
                    if (!string.IsNullOrWhiteSpace(result.ModulePlan.PreReleaseTag))
                        cmdLogger.Info($"Module prerelease tag: {result.ModulePlan.PreReleaseTag}");
                }
                if (result.Module is not null && !string.IsNullOrWhiteSpace(result.Module.Executable))
                    cmdLogger.Info($"Module host: {result.Module.Executable}");
            }

            if (result.WorkspaceValidationPlan is not null)
            {
                var stepCount = result.WorkspaceValidationPlan.Steps?.Length ?? 0;
                cmdLogger.Success($"Workspace validation: {(planOnly || validateOnly ? "planned" : "completed")} ({stepCount} step(s)).");
            }

            if (result.ToolPlan is not null)
            {
                var comboCount = result.ToolPlan.Targets.Sum(target => target.Combinations?.Length ?? 0);
                cmdLogger.Success($"Tools: {(planOnly || validateOnly ? "planned" : "completed")} ({comboCount} combination(s)).");
            }

            if (result.DotNetToolPlan is not null)
            {
                var stepCount = result.DotNetToolPlan.Steps?.Length ?? 0;
                var targetCount = result.DotNetToolPlan.Targets?.Length ?? 0;
                cmdLogger.Success($"Tools (DotNetPublish): {(planOnly || validateOnly ? "planned" : "completed")} ({stepCount} step(s), {targetCount} target(s)).");
                if (!string.IsNullOrWhiteSpace(result.DotNetToolPlan.Outputs?.ManifestJsonPath))
                    cmdLogger.Info($"Manifest: {result.DotNetToolPlan.Outputs.ManifestJsonPath}");
            }

            if (!string.IsNullOrWhiteSpace(result.ReleaseManifestPath))
                cmdLogger.Info($"Release manifest: {result.ReleaseManifestPath}");
            if (!string.IsNullOrWhiteSpace(result.ReleaseChecksumsPath))
                cmdLogger.Info($"Release checksums: {result.ReleaseChecksumsPath}");

            if (result.Tools is not null)
            {
                foreach (var artefact in result.Tools.Artefacts)
                {
                    cmdLogger.Info($" -> {artefact.Target} {artefact.Framework} {artefact.Runtime} {artefact.Flavor}: {artefact.OutputPath}");
                    if (!string.IsNullOrWhiteSpace(artefact.ZipPath))
                        cmdLogger.Info($"    zip: {artefact.ZipPath}");
                }

                foreach (var manifest in result.Tools.ManifestPaths)
                    cmdLogger.Info($"Manifest: {manifest}");
            }

            if (result.DotNetTools is not null)
            {
                foreach (var artefact in result.DotNetTools.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                {
                    var label = artefact.Category == DotNetPublishArtefactCategory.Bundle && !string.IsNullOrWhiteSpace(artefact.BundleId)
                        ? $"{artefact.Target} [{artefact.BundleId}]"
                        : artefact.Target;
                    cmdLogger.Info($" -> {label} {artefact.Framework} {artefact.Runtime} {artefact.Style}: {artefact.OutputDir}");
                    if (!string.IsNullOrWhiteSpace(artefact.ZipPath))
                        cmdLogger.Info($"    zip: {artefact.ZipPath}");
                }

                foreach (var built in result.DotNetTools.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
                {
                    foreach (var output in built.OutputFiles ?? Array.Empty<string>())
                        cmdLogger.Info($"    msi: {output}");
                }

                foreach (var store in result.DotNetTools.StorePackages ?? Array.Empty<DotNetPublishStorePackageResult>())
                {
                    foreach (var output in store.OutputFiles ?? Array.Empty<string>())
                        cmdLogger.Info($"    store: {output}");
                    foreach (var output in store.UploadFiles ?? Array.Empty<string>())
                        cmdLogger.Info($"    store upload: {output}");
                    foreach (var output in store.SymbolFiles ?? Array.Empty<string>())
                        cmdLogger.Info($"    store symbols: {output}");
                }

                if (!string.IsNullOrWhiteSpace(result.DotNetTools.ManifestJsonPath))
                    cmdLogger.Info($"Manifest: {result.DotNetTools.ManifestJsonPath}");
                if (!string.IsNullOrWhiteSpace(result.DotNetTools.ChecksumsPath))
                    cmdLogger.Info($"Checksums: {result.DotNetTools.ChecksumsPath}");
                if (!string.IsNullOrWhiteSpace(result.DotNetTools.RunReportPath))
                    cmdLogger.Info($"Run report: {result.DotNetTools.RunReportPath}");
            }

            foreach (var release in result.ToolGitHubReleases)
            {
                if (release.Success)
                    cmdLogger.Info($"GitHub release -> {release.Target} {release.TagName} {release.ReleaseUrl}");
                else
                    cmdLogger.Warn($"GitHub release failed for {release.Target}: {release.ErrorMessage}");
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteReleaseError(outputJson, "release", 1, ex.Message, logger);
        }
    }

    private static int WriteReleaseError(bool outputJson, string command, int exitCode, string message, ILogger logger)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = exitCode,
                Error = message
            });
            return exitCode;
        }

        logger.Error(message);
        return exitCode;
    }

    internal static PowerForgeReleaseRequest BuildReleaseRequestFromArgs(
        string[] argv,
        string fullConfigPath,
        bool planOnly,
        bool validateOnly,
        bool packagesOnly,
        bool moduleOnly,
        bool toolsOnly,
        PowerForgeReleaseRequest? defaults = null)
    {
        var flavors = ParseCsvOptionValues(argv, "--flavor")
            .Select(ParsePowerForgeToolReleaseFlavor)
            .Distinct()
            .ToArray();
        var toolOutputs = ParseCsvOptionValues(argv, "--tool-output")
            .Select(ParsePowerForgeReleaseToolOutputKind)
            .Distinct()
            .ToArray();
        var skipToolOutputs = ParseCsvOptionValues(argv, "--skip-tool-output")
            .Select(ParsePowerForgeReleaseToolOutputKind)
            .Distinct()
            .ToArray();
        var styles = ParseCsvOptionValues(argv, "--style")
            .Select(ParseDotNetPublishStyle)
            .Distinct()
            .ToArray();

        var request = defaults ?? new PowerForgeReleaseRequest();
        request.ConfigPath = fullConfigPath;
        request.PlanOnly = planOnly;
        request.ValidateOnly = validateOnly;
        request.PackagesOnly = packagesOnly;
        request.ModuleOnly = moduleOnly;
        request.ToolsOnly = request.ToolsOnly || toolsOnly;

        request.SkipWorkspaceValidation = request.SkipWorkspaceValidation || argv.Any(a => a.Equals("--skip-workspace-validation", StringComparison.OrdinalIgnoreCase));
        request.SkipRestore = request.SkipRestore || argv.Any(a => a.Equals("--skip-restore", StringComparison.OrdinalIgnoreCase));
        request.SkipBuild = request.SkipBuild || argv.Any(a => a.Equals("--skip-build", StringComparison.OrdinalIgnoreCase));
        request.SkipReleaseChecksums = request.SkipReleaseChecksums || argv.Any(a => a.Equals("--skip-release-checksums", StringComparison.OrdinalIgnoreCase));

        request.PublishNuget = ChooseBool(request.PublishNuget, argv.Any(a => a.Equals("--publish-nuget", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.PublishProjectGitHub = ChooseBool(request.PublishProjectGitHub, argv.Any(a => a.Equals("--publish-project-github", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.PublishToolGitHub = ChooseBool(request.PublishToolGitHub, argv.Any(a => a.Equals("--publish-tool-github", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.ModuleNoDotnetBuild = ChooseBool(request.ModuleNoDotnetBuild, argv.Any(a => a.Equals("--module-no-dotnet-build", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.ModuleNoSign = ChooseBool(request.ModuleNoSign, argv.Any(a => a.Equals("--module-no-sign", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.ModuleSignModule = ChooseBool(request.ModuleSignModule, argv.Any(a => a.Equals("--module-sign", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.KeepSymbols = ChooseBool(request.KeepSymbols, argv.Any(a => a.Equals("--keep-symbols", StringComparison.OrdinalIgnoreCase)) ? true : null);
        request.EnableSigning = ChooseBool(request.EnableSigning, argv.Any(a => a.Equals("--sign", StringComparison.OrdinalIgnoreCase)) ? true : null);

        request.Configuration = ChooseString(request.Configuration, TryGetOptionValue(argv, "--configuration"));
        request.WorkspaceConfigPath = ChooseString(request.WorkspaceConfigPath, TryGetOptionValue(argv, "--workspace-config"));
        request.WorkspaceProfile = ChooseString(request.WorkspaceProfile, TryGetOptionValue(argv, "--workspace-profile"));
        request.WorkspaceTestimoXRoot = ChooseString(request.WorkspaceTestimoXRoot, TryGetOptionValue(argv, "--workspace-testimox-root"));
        request.OutputRoot = ChooseString(request.OutputRoot, TryGetOptionValue(argv, "--output-root"));
        request.StageRoot = ChooseString(request.StageRoot, TryGetOptionValue(argv, "--stage-root"));
        request.ModuleVersion = ChooseString(request.ModuleVersion, TryGetOptionValue(argv, "--module-version"));
        request.ModulePreReleaseTag = ChooseString(request.ModulePreReleaseTag, TryGetOptionValue(argv, "--module-prerelease-tag"));
        request.ManifestJsonPath = ChooseString(request.ManifestJsonPath, TryGetOptionValue(argv, "--manifest-json"));
        request.ChecksumsPath = ChooseString(request.ChecksumsPath, TryGetOptionValue(argv, "--checksums-path"));
        request.SignProfile = ChooseString(request.SignProfile, TryGetOptionValue(argv, "--sign-profile"));
        request.SignToolPath = ChooseString(request.SignToolPath, TryGetOptionValue(argv, "--sign-tool-path"));
        request.SignThumbprint = ChooseString(request.SignThumbprint, TryGetOptionValue(argv, "--sign-thumbprint"));
        request.SignSubjectName = ChooseString(request.SignSubjectName, TryGetOptionValue(argv, "--sign-subject-name"));
        request.SignTimestampUrl = ChooseString(request.SignTimestampUrl, TryGetOptionValue(argv, "--sign-timestamp-url"));
        request.SignDescription = ChooseString(request.SignDescription, TryGetOptionValue(argv, "--sign-description"));
        request.SignUrl = ChooseString(request.SignUrl, TryGetOptionValue(argv, "--sign-url"));
        request.SignCsp = ChooseString(request.SignCsp, TryGetOptionValue(argv, "--sign-csp"));
        request.SignKeyContainer = ChooseString(request.SignKeyContainer, TryGetOptionValue(argv, "--sign-key-container"));
        request.PackageSignThumbprint = ChooseString(request.PackageSignThumbprint, TryGetOptionValue(argv, "--package-sign-thumbprint"));
        request.PackageSignStore = ChooseString(request.PackageSignStore, TryGetOptionValue(argv, "--package-sign-store"));
        request.PackageSignTimestampUrl = ChooseString(request.PackageSignTimestampUrl, TryGetOptionValue(argv, "--package-sign-timestamp-url"));

        if (TryParseDotNetPublishPolicyMode(TryGetOptionValue(argv, "--sign-on-missing-tool")) is { } signOnMissingTool)
            request.SignOnMissingTool = signOnMissingTool;
        if (TryParseDotNetPublishPolicyMode(TryGetOptionValue(argv, "--sign-on-failure")) is { } signOnFailure)
            request.SignOnFailure = signOnFailure;

        var workspaceEnableFeatures = ParseCsvOptionValues(argv, "--workspace-enable-feature");
        if (workspaceEnableFeatures.Length > 0)
            request.WorkspaceEnableFeatures = workspaceEnableFeatures;
        var workspaceDisableFeatures = ParseCsvOptionValues(argv, "--workspace-disable-feature");
        if (workspaceDisableFeatures.Length > 0)
            request.WorkspaceDisableFeatures = workspaceDisableFeatures;
        var targets = ParseCsvOptionValues(argv, "--target");
        if (targets.Length > 0)
            request.Targets = targets;
        var runtimes = ParseCsvOptionValues(argv, "--rid", "--runtime");
        if (runtimes.Length > 0)
            request.Runtimes = runtimes;
        var frameworks = ParseCsvOptionValues(argv, "--framework");
        if (frameworks.Length > 0)
            request.Frameworks = frameworks;
        if (styles.Length > 0)
            request.Styles = styles;
        if (flavors.Length > 0)
            request.Flavors = flavors;
        if (toolOutputs.Length > 0)
            request.ToolOutputs = toolOutputs;
        if (skipToolOutputs.Length > 0)
            request.SkipToolOutputs = skipToolOutputs;

        var installerProperties = ParseKeyValueOptions(argv, "--installer-property")
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
        if (installerProperties.Count > 0)
            request.InstallerMsBuildProperties = installerProperties;

        return request;
    }

    private static string? ChooseString(string? currentValue, string? overrideValue)
        => string.IsNullOrWhiteSpace(overrideValue) ? currentValue : overrideValue;

    private static bool? ChooseBool(bool? currentValue, bool? overrideValue)
        => overrideValue.HasValue ? overrideValue : currentValue;

    private static PowerForgeToolReleaseFlavor ParsePowerForgeToolReleaseFlavor(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Flavor must not be empty.", nameof(value));

        if (Enum.TryParse(raw, ignoreCase: true, out PowerForgeToolReleaseFlavor flavor))
            return flavor;

        throw new ArgumentException(
            $"Unknown tool release flavor: {raw}. Expected one of: {string.Join(", ", Enum.GetNames(typeof(PowerForgeToolReleaseFlavor)))}",
            nameof(value));
    }

    private static PowerForgeReleaseToolOutputKind ParsePowerForgeReleaseToolOutputKind(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Tool output kind must not be empty.", nameof(value));

        if (Enum.TryParse(raw, ignoreCase: true, out PowerForgeReleaseToolOutputKind kind))
            return kind;

        throw new ArgumentException(
            $"Unknown tool output kind: {raw}. Expected one of: {string.Join(", ", Enum.GetNames(typeof(PowerForgeReleaseToolOutputKind)))}",
            nameof(value));
    }

    private static DotNetPublishPolicyMode? TryParseDotNetPublishPolicyMode(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (Enum.TryParse(raw, ignoreCase: true, out DotNetPublishPolicyMode mode))
            return mode;

        throw new ArgumentException(
            $"Unknown dotnet publish policy mode: {raw}. Expected one of: {string.Join(", ", Enum.GetNames(typeof(DotNetPublishPolicyMode)))}",
            nameof(value));
    }

}
