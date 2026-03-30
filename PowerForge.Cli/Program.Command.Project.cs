using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string ProjectReleaseUsage =
        "Usage: powerforge project release [--config <project.release.json>] [--plan] [--validate] [--publish-tool-github] [--skip-workspace-validation] [--workspace-config <workspace.validation.json>] [--workspace-profile <name>] [--workspace-enable-feature <name[,name...]>] [--workspace-disable-feature <name[,name...]>] [--skip-restore] [--skip-build] [--output-root <path>] [--stage-root <path>] [--manifest-json <path>] [--checksums-path <path>] [--skip-release-checksums] [--keep-symbols] [--sign] [--sign-profile <name>] [--sign-tool-path <path>] [--sign-thumbprint <sha1>] [--sign-subject-name <name>] [--sign-on-missing-tool <Warn|Fail|Skip>] [--sign-on-failure <Warn|Fail|Skip>] [--sign-timestamp-url <url>] [--sign-description <text>] [--sign-url <url>] [--sign-csp <name>] [--sign-key-container <name>] [--installer-property <Name=Value>] [--tool-output <Tool|Portable|Installer|Store>[,<...>]] [--skip-tool-output <Tool|Portable|Installer|Store>[,<...>]] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|FrameworkDependent|AotSpeed|AotSize>[,<...>]] [--output json]";

    private const string ProjectScaffoldUsage =
        "Usage: powerforge project scaffold [--project-root <path>] [--project <App.csproj>] [--name <Name>] [--target <Name>] [--framework <tfm>] [--rid <Rid[,Rid...]>] [--configuration <Release|Debug>] [--out <project.release.json>] [--portable] [--overwrite] [--output json]";

    private static int CommandProject(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "project",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { releaseUsage = ProjectReleaseUsage, scaffoldUsage = ProjectScaffoldUsage })
                });
            }
            else
            {
                Console.WriteLine(ProjectReleaseUsage);
                Console.WriteLine(ProjectScaffoldUsage);
            }

            return 0;
        }

        var sub = argv[0].ToLowerInvariant();
        return sub switch
        {
            "release" => CommandProjectRelease(argv.Skip(1).ToArray(), outputJson, cli, logger),
            "scaffold" => CommandProjectScaffold(argv.Skip(1).ToArray(), outputJson, cli, logger),
            _ => WriteReleaseError(outputJson, "project", 2, $"Unknown project subcommand '{sub}'. Expected 'release' or 'scaffold'.", logger)
        };
    }

    private static int CommandProjectRelease(string[] argv, bool outputJson, CliOptions cli, ILogger logger)
    {
        if (argv.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "project release",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = ProjectReleaseUsage })
                });
            }
            else
            {
                Console.WriteLine(ProjectReleaseUsage);
            }

            return 0;
        }

        var configPath = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultProjectReleaseConfig(Directory.GetCurrentDirectory());

        if (string.IsNullOrWhiteSpace(configPath))
            return WriteReleaseError(outputJson, "project release", 2, "Missing --config and no default project release config found.", logger);

        try
        {
            var fullConfigPath = Path.GetFullPath(configPath);
            var project = new PowerForgeProjectConfigurationJsonService().Load(fullConfigPath);
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var projectRoot = string.IsNullOrWhiteSpace(project.ProjectRoot)
                ? Path.GetDirectoryName(fullConfigPath) ?? Directory.GetCurrentDirectory()
                : project.ProjectRoot!;
            var syntheticConfigPath = Path.Combine(projectRoot, ".powerforge", "release.project.json");
            var (spec, requestDefaults) = PowerForgeProjectDslMapper.CreateRelease(project, syntheticConfigPath, projectRoot);
            var request = BuildReleaseRequestFromArgs(
                argv,
                syntheticConfigPath,
                planOnly: argv.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)),
                validateOnly: argv.Any(a => a.Equals("--validate", StringComparison.OrdinalIgnoreCase)),
                packagesOnly: false,
                moduleOnly: false,
                toolsOnly: true);

            var service = new PowerForgeReleaseService(cmdLogger);
            var result = RunWithStatus(outputJson, cli, "Running project release workflow", () => service.Execute(spec, request));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "project release",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Error = result.ErrorMessage,
                    Config = "project",
                    ConfigPath = fullConfigPath,
                    Spec = CliJson.SerializeToElement(project, CliJson.Context.ConfigurationProject),
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerForgeReleaseResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (!result.Success)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Project release workflow failed.");
                return exitCode;
            }

            if (result.DotNetToolPlan is not null)
            {
                var stepCount = result.DotNetToolPlan.Steps?.Length ?? 0;
                var targetCount = result.DotNetToolPlan.Targets?.Length ?? 0;
                cmdLogger.Success($"Project release: {(request.PlanOnly || request.ValidateOnly ? "planned" : "completed")} ({stepCount} step(s), {targetCount} target(s)).");
                if (!string.IsNullOrWhiteSpace(result.DotNetToolPlan.Outputs?.ManifestJsonPath))
                    cmdLogger.Info($"Manifest: {result.DotNetToolPlan.Outputs.ManifestJsonPath}");
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
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteReleaseError(outputJson, "project release", 1, ex.Message, logger);
        }
    }

    private static int CommandProjectScaffold(string[] argv, bool outputJson, CliOptions cli, ILogger logger)
    {
        if (argv.Any(a => a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "project scaffold",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new { usage = ProjectScaffoldUsage })
                });
            }
            else
            {
                Console.WriteLine(ProjectScaffoldUsage);
            }

            return 0;
        }

        try
        {
            var request = new PowerForgeProjectConfigurationScaffoldRequest
            {
                ProjectRoot = TryGetOptionValue(argv, "--project-root") ?? ".",
                ProjectPath = TryGetOptionValue(argv, "--project"),
                Name = TryGetOptionValue(argv, "--name"),
                TargetName = TryGetOptionValue(argv, "--target"),
                Framework = TryGetOptionValue(argv, "--framework"),
                Runtimes = ParseCsvOptionValues(argv, "--rid", "--runtime"),
                Configuration = TryGetOptionValue(argv, "--configuration") ?? "Release",
                OutputPath = TryGetOptionValue(argv, "--out") ?? Path.Combine("Build", "project.release.json"),
                Force = argv.Any(a => a.Equals("--overwrite", StringComparison.OrdinalIgnoreCase) || a.Equals("--force", StringComparison.OrdinalIgnoreCase)),
                IncludePortableOutput = argv.Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase)),
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            var service = new PowerForgeProjectConfigurationScaffoldService();
            var resolvedOutputPath = service.ResolveOutputPath(request);
            var action = File.Exists(resolvedOutputPath)
                ? "Overwrite project release configuration"
                : "Create project release configuration";
            var result = RunWithStatus(outputJson, cli, "Scaffolding project release config", () => service.Generate(request));

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "project scaffold",
                    Success = true,
                    ExitCode = 0,
                    Config = "project",
                    ConfigPath = resolvedOutputPath,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerForgeProjectConfigurationScaffoldResult)
                });
                return 0;
            }

            logger.Success($"{action}: {result.ConfigPath}");
            logger.Info($"Target: {result.TargetName}");
            logger.Info($"Framework: {result.Framework}");
            if (result.Runtimes.Length > 0)
                logger.Info($"Runtimes: {string.Join(", ", result.Runtimes)}");
            return 0;
        }
        catch (Exception ex)
        {
            return WriteReleaseError(outputJson, "project scaffold", 1, ex.Message, logger);
        }
    }

    private static string? FindDefaultProjectReleaseConfig(string baseDirectory)
    {
        var candidates = new[]
        {
            "project.release.json",
            Path.Combine(".powerforge", "project.release.json"),
            Path.Combine("Build", "project.release.json")
        };

        foreach (var directory in EnumerateSelfAndParents(baseDirectory))
        {
            foreach (var relativePath in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (IOException)
                {
                    // best effort only
                }
                catch (UnauthorizedAccessException)
                {
                    // best effort only
                }
            }
        }

        return null;
    }
}
