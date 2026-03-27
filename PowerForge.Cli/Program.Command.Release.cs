using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string ReleaseUsage =
        "Usage: powerforge release [--config <release.json>] [--plan] [--validate] [--packages-only] [--tools-only] [--configuration <Release|Debug>] [--module-no-dotnet-build] [--module-version <version>] [--module-prerelease-tag <tag>] [--module-no-sign] [--module-sign] [--skip-workspace-validation] [--workspace-config <workspace.validation.json>] [--workspace-profile <name>] [--workspace-testimox-root <path>] [--workspace-enable-feature <name[,name...]>] [--workspace-disable-feature <name[,name...]>] [--publish-nuget] [--publish-project-github] [--publish-tool-github] [--skip-restore] [--skip-build] [--output-root <path>] [--stage-root <path>] [--manifest-json <path>] [--checksums-path <path>] [--skip-release-checksums] [--keep-symbols] [--sign] [--sign-profile <name>] [--sign-tool-path <path>] [--sign-thumbprint <sha1>] [--sign-subject-name <name>] [--sign-on-missing-tool <Warn|Fail|Skip>] [--sign-on-failure <Warn|Fail|Skip>] [--sign-timestamp-url <url>] [--sign-description <text>] [--sign-url <url>] [--sign-csp <name>] [--sign-key-container <name>] [--package-sign-thumbprint <sha1>] [--package-sign-store <CurrentUser|LocalMachine>] [--package-sign-timestamp-url <url>] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|FrameworkDependent|AotSpeed|AotSize>[,<...>]] [--flavor <SingleContained|SingleFx|Portable|Fx>[,<...>]] [--output json]";

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
        var toolsOnly = argv.Any(a => a.Equals("--tools-only", StringComparison.OrdinalIgnoreCase));

        if (packagesOnly && toolsOnly)
        {
            return WriteReleaseError(outputJson, "release", 2, "Use either --packages-only or --tools-only, not both.", logger);
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

            var flavors = ParseCsvOptionValues(argv, "--flavor")
                .Select(ParsePowerForgeToolReleaseFlavor)
                .Distinct()
                .ToArray();
            var styles = ParseCsvOptionValues(argv, "--style")
                .Select(ParseDotNetPublishStyle)
                .Distinct()
                .ToArray();

            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = fullConfigPath,
                PlanOnly = planOnly,
                ValidateOnly = validateOnly,
                PackagesOnly = packagesOnly,
                ToolsOnly = toolsOnly,
                Configuration = TryGetOptionValue(argv, "--configuration"),
                SkipWorkspaceValidation = argv.Any(a => a.Equals("--skip-workspace-validation", StringComparison.OrdinalIgnoreCase)),
                WorkspaceConfigPath = TryGetOptionValue(argv, "--workspace-config"),
                WorkspaceProfile = TryGetOptionValue(argv, "--workspace-profile"),
                WorkspaceTestimoXRoot = TryGetOptionValue(argv, "--workspace-testimox-root"),
                WorkspaceEnableFeatures = ParseCsvOptionValues(argv, "--workspace-enable-feature"),
                WorkspaceDisableFeatures = ParseCsvOptionValues(argv, "--workspace-disable-feature"),
                SkipRestore = argv.Any(a => a.Equals("--skip-restore", StringComparison.OrdinalIgnoreCase)),
                SkipBuild = argv.Any(a => a.Equals("--skip-build", StringComparison.OrdinalIgnoreCase)),
                OutputRoot = TryGetOptionValue(argv, "--output-root"),
                StageRoot = TryGetOptionValue(argv, "--stage-root"),
                PublishNuget = argv.Any(a => a.Equals("--publish-nuget", StringComparison.OrdinalIgnoreCase)) ? true : null,
                PublishProjectGitHub = argv.Any(a => a.Equals("--publish-project-github", StringComparison.OrdinalIgnoreCase)) ? true : null,
                PublishToolGitHub = argv.Any(a => a.Equals("--publish-tool-github", StringComparison.OrdinalIgnoreCase)) ? true : null,
                ModuleNoDotnetBuild = argv.Any(a => a.Equals("--module-no-dotnet-build", StringComparison.OrdinalIgnoreCase)) ? true : null,
                ModuleVersion = TryGetOptionValue(argv, "--module-version"),
                ModulePreReleaseTag = TryGetOptionValue(argv, "--module-prerelease-tag"),
                ModuleNoSign = argv.Any(a => a.Equals("--module-no-sign", StringComparison.OrdinalIgnoreCase)) ? true : null,
                ModuleSignModule = argv.Any(a => a.Equals("--module-sign", StringComparison.OrdinalIgnoreCase)) ? true : null,
                ManifestJsonPath = TryGetOptionValue(argv, "--manifest-json"),
                ChecksumsPath = TryGetOptionValue(argv, "--checksums-path"),
                SkipReleaseChecksums = argv.Any(a => a.Equals("--skip-release-checksums", StringComparison.OrdinalIgnoreCase)),
                KeepSymbols = argv.Any(a => a.Equals("--keep-symbols", StringComparison.OrdinalIgnoreCase)) ? true : null,
                EnableSigning = argv.Any(a => a.Equals("--sign", StringComparison.OrdinalIgnoreCase)) ? true : null,
                SignProfile = TryGetOptionValue(argv, "--sign-profile"),
                SignToolPath = TryGetOptionValue(argv, "--sign-tool-path"),
                SignThumbprint = TryGetOptionValue(argv, "--sign-thumbprint"),
                SignSubjectName = TryGetOptionValue(argv, "--sign-subject-name"),
                SignOnMissingTool = TryParseDotNetPublishPolicyMode(TryGetOptionValue(argv, "--sign-on-missing-tool")),
                SignOnFailure = TryParseDotNetPublishPolicyMode(TryGetOptionValue(argv, "--sign-on-failure")),
                SignTimestampUrl = TryGetOptionValue(argv, "--sign-timestamp-url"),
                SignDescription = TryGetOptionValue(argv, "--sign-description"),
                SignUrl = TryGetOptionValue(argv, "--sign-url"),
                SignCsp = TryGetOptionValue(argv, "--sign-csp"),
                SignKeyContainer = TryGetOptionValue(argv, "--sign-key-container"),
                PackageSignThumbprint = TryGetOptionValue(argv, "--package-sign-thumbprint"),
                PackageSignStore = TryGetOptionValue(argv, "--package-sign-store"),
                PackageSignTimestampUrl = TryGetOptionValue(argv, "--package-sign-timestamp-url"),
                Targets = ParseCsvOptionValues(argv, "--target"),
                Runtimes = ParseCsvOptionValues(argv, "--rid", "--runtime"),
                Frameworks = ParseCsvOptionValues(argv, "--framework"),
                Styles = styles,
                Flavors = flavors
            };

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
