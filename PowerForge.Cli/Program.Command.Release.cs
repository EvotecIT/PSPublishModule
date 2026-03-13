using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ReleaseUsage =
        "Usage: powerforge release [--config <release.json>] [--plan] [--validate] [--packages-only] [--tools-only] [--publish-nuget] [--publish-project-github] [--publish-tool-github] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--flavor <SingleContained|SingleFx|Portable|Fx>[,<...>]] [--output json]";

    private static int CommandRelease(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
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

            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = fullConfigPath,
                PlanOnly = planOnly,
                ValidateOnly = validateOnly,
                PackagesOnly = packagesOnly,
                ToolsOnly = toolsOnly,
                PublishNuget = argv.Any(a => a.Equals("--publish-nuget", StringComparison.OrdinalIgnoreCase)) ? true : null,
                PublishProjectGitHub = argv.Any(a => a.Equals("--publish-project-github", StringComparison.OrdinalIgnoreCase)) ? true : null,
                PublishToolGitHub = argv.Any(a => a.Equals("--publish-tool-github", StringComparison.OrdinalIgnoreCase)) ? true : null,
                Targets = ParseCsvOptionValues(argv, "--target"),
                Runtimes = ParseCsvOptionValues(argv, "--rid", "--runtime"),
                Frameworks = ParseCsvOptionValues(argv, "--framework"),
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

            if (result.ToolPlan is not null)
            {
                var comboCount = result.ToolPlan.Targets.Sum(target => target.Combinations?.Length ?? 0);
                cmdLogger.Success($"Tools: {(planOnly || validateOnly ? "planned" : "completed")} ({comboCount} combination(s)).");
            }

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
}
