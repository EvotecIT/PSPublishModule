using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private const string DotNetPublishUsage =
        "Usage: powerforge dotnet publish [--config <DotNetPublish.json>] [--project-root <path>] [--profile <name>] [--plan] [--validate] [--output json] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>] [--matrix <runtime|framework|style=value[,value][;...]>] [--skip-restore] [--skip-build]";
    private const string DotNetScaffoldUsage =
        "Usage: powerforge dotnet scaffold [--project-root <path>] [--project <App.csproj>] [--target <Name>] [--framework <tfm>] [--rid <Rid[,Rid...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>[,...]] [--configuration <Release|Debug>] [--out <powerforge.dotnetpublish.json>] [--overwrite] [--no-schema] [--output json]";

    private static int CommandDotNet(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(DotNetPublishUsage);
            Console.WriteLine(DotNetScaffoldUsage);
            return 2;
        }

        var sub = argv[0].ToLowerInvariant();
        switch (sub)
        {
            case "publish":
            {
                var subArgs = argv.Skip(1).ToArray();
                var outputJson = IsJsonOutput(subArgs);
                var planOnly = subArgs.Any(a => a.Equals("--plan", StringComparison.OrdinalIgnoreCase) || a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                var validateOnly = subArgs.Any(a => a.Equals("--validate", StringComparison.OrdinalIgnoreCase));
                var runPipeline = !planOnly && !validateOnly;

                var overrideTargets = ParseCsvOptionValues(subArgs, "--target");
                var overrideRids = ParseCsvOptionValues(subArgs, "--rid", "--runtime");
                var overrideFrameworks = ParseCsvOptionValues(subArgs, "--framework");
                var overrideStyle = TryGetOptionValue(subArgs, "--style");
                var overrideProfile = TryGetOptionValue(subArgs, "--profile");
                var skipRestore = subArgs.Any(a => a.Equals("--skip-restore", StringComparison.OrdinalIgnoreCase));
                var skipBuild = subArgs.Any(a => a.Equals("--skip-build", StringComparison.OrdinalIgnoreCase));

                var configPath = TryGetOptionValue(subArgs, "--config");
                if (string.IsNullOrWhiteSpace(configPath))
                {
                    var baseDir = TryGetProjectRoot(subArgs);
                    if (!string.IsNullOrWhiteSpace(baseDir))
                        baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
                    else
                        baseDir = Directory.GetCurrentDirectory();

                    configPath = FindDefaultDotNetPublishConfig(baseDir);
                }

                if (string.IsNullOrWhiteSpace(configPath))
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "dotnet.publish",
                            Success = false,
                            ExitCode = 2,
                            Error = "Missing --config and no default dotnet publish config found."
                        });
                        return 2;
                    }

                    Console.WriteLine(DotNetPublishUsage);
                    return 2;
                }

                try
                {
                    var interactive = runPipeline && DotNetPublishConsoleUi.ShouldUseInteractiveView(outputJson, cli);
                    var (cmdLogger, logBuffer) = interactive
                        ? (new NullLogger { IsVerbose = cli.Verbose }, null)
                        : CreateCommandLogger(outputJson, cli, logger);

                    var loaded = LoadDotNetPublishSpecWithPath(configPath);
                    var spec = loaded.Value;
                    var specPath = loaded.FullPath;
                    var matrixOverrides = ParseDotNetPublishMatrixOverrides(subArgs);
                    var effectiveRids = overrideRids.Length > 0 ? overrideRids : matrixOverrides.Runtimes;
                    var effectiveFrameworks = overrideFrameworks.Length > 0 ? overrideFrameworks : matrixOverrides.Frameworks;
                    var effectiveStyles = !string.IsNullOrWhiteSpace(overrideStyle)
                        ? new[] { ParseDotNetPublishStyle(overrideStyle) }
                        : matrixOverrides.Styles;

                    var runner = new DotNetPublishPipelineRunner(cmdLogger);
                    if (!string.IsNullOrWhiteSpace(overrideProfile))
                        spec.Profile = overrideProfile.Trim();
                    var plan = runner.Plan(spec, specPath);
                    ApplyDotNetPublishPlanOverrides(plan, overrideTargets, effectiveRids, effectiveFrameworks, effectiveStyles);
                    ApplyDotNetPublishSkipFlags(plan, skipRestore, skipBuild);

                    if (validateOnly)
                    {
                        var errors = ValidateDotNetPublishPlan(plan);
                        var validateExitCode = errors.Length == 0 ? 0 : 2;

                        if (outputJson)
                        {
                            WriteJson(new CliJsonEnvelope
                            {
                                SchemaVersion = OutputSchemaVersion,
                                Command = "dotnet.publish.validate",
                                Success = validateExitCode == 0,
                                ExitCode = validateExitCode,
                                Error = validateExitCode == 0 ? null : string.Join("\n", errors),
                                Config = "dotnetpublish",
                                ConfigPath = specPath,
                                Spec = CliJson.SerializeToElement(spec, CliJson.Context.DotNetPublishSpec),
                                Plan = CliJson.SerializeToElement(plan, CliJson.Context.DotNetPublishPlan),
                                Logs = LogsToJsonElement(logBuffer)
                            });
                            return validateExitCode;
                        }

                        if (validateExitCode == 0)
                        {
                            cmdLogger.Success($"Dotnet publish config is valid ({plan.Steps.Length} steps, {plan.Targets.Length} target(s)).");
                            return 0;
                        }

                        cmdLogger.Error("Dotnet publish config validation failed.");
                        foreach (var err in errors) cmdLogger.Error(err);
                        return validateExitCode;
                    }

                    DotNetPublishResult? result = null;
                    if (runPipeline)
                    {
                        result = interactive
                            ? DotNetPublishConsoleUi.Run(runner, plan, specPath, outputJson, cli)
                            : RunWithStatus(outputJson, cli, "Publishing dotnet artefacts", () => runner.Run(plan, progress: null));
                    }

                    var exitCode = result is not null && !result.Succeeded ? 1 : 0;

                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "dotnet.publish",
                            Success = exitCode == 0,
                            ExitCode = exitCode,
                            Error = exitCode == 0 ? null : result?.ErrorMessage,
                            Config = "dotnetpublish",
                            ConfigPath = specPath,
                            Spec = CliJson.SerializeToElement(spec, CliJson.Context.DotNetPublishSpec),
                            Plan = CliJson.SerializeToElement(plan, CliJson.Context.DotNetPublishPlan),
                            Result = result is null ? null : CliJson.SerializeToElement(result, CliJson.Context.DotNetPublishResult),
                            Logs = LogsToJsonElement(logBuffer)
                        });
                        return exitCode;
                    }

                    if (planOnly)
                    {
                        cmdLogger.Success($"Planned dotnet publish ({plan.Steps.Length} steps, {plan.Targets.Length} target(s)).");
                        cmdLogger.Info($"Project root: {plan.ProjectRoot}");
                        if (!string.IsNullOrWhiteSpace(plan.Outputs.ManifestJsonPath))
                            cmdLogger.Info($"Manifest: {plan.Outputs.ManifestJsonPath}");
                        return 0;
                    }

                    if (result is null)
                    {
                        logger.Error("Dotnet publish failed (no result).");
                        return 1;
                    }

                    if (!result.Succeeded)
                    {
                        if (!interactive)
                        {
                            logger.Error(result.ErrorMessage ?? "Dotnet publish failed.");
                            WriteDotNetPublishFailureDetails(result, logger);
                        }
                        return 1;
                    }

                    cmdLogger.Success($"Dotnet publish completed ({result.Artefacts.Length} artefact(s)).");
                    if (!string.IsNullOrWhiteSpace(result.ManifestJsonPath))
                        cmdLogger.Info($"Manifest: {result.ManifestJsonPath}");
                    if (!string.IsNullOrWhiteSpace(result.ChecksumsPath))
                        cmdLogger.Info($"Checksums: {result.ChecksumsPath}");
                    if (!string.IsNullOrWhiteSpace(result.RunReportPath))
                        cmdLogger.Info($"Run report: {result.RunReportPath}");
                    foreach (var a in result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                    {
                        if (!string.IsNullOrWhiteSpace(a.OutputDir))
                            cmdLogger.Info($" -> {a.Target} {a.Framework} {a.Runtime} {a.Style}: {a.OutputDir}");
                    }
                    foreach (var prepared in result.MsiPrepares ?? Array.Empty<DotNetPublishMsiPrepareResult>())
                    {
                        cmdLogger.Info(
                            $" -> msi.prepare {prepared.InstallerId} from {prepared.Target} {prepared.Framework} {prepared.Runtime} {prepared.Style}: {prepared.StagingDir}");
                    }
                    foreach (var built in result.MsiBuilds ?? Array.Empty<DotNetPublishMsiBuildResult>())
                    {
                        var outputs = built.OutputFiles?.Length ?? 0;
                        var signed = built.SignedFiles?.Length ?? 0;
                        cmdLogger.Info(
                            $" -> msi.build {built.InstallerId} from {built.Target} {built.Framework} {built.Runtime} {built.Style}: {outputs} output(s), {signed} signed");
                    }
                    foreach (var gate in result.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGateResult>())
                    {
                        cmdLogger.Info(
                            $" -> benchmark.gate {gate.GateId}: {(gate.Passed ? "passed" : "failed")} ({gate.Metrics?.Length ?? 0} metric(s))");
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "dotnet.publish",
                            Success = false,
                            ExitCode = 1,
                            Error = ex.Message
                        });
                        return 1;
                    }

                    logger.Error(ex.Message);
                    return 1;
                }
            }
            case "scaffold":
            case "init":
            {
                var subArgs = argv.Skip(1).ToArray();
                var outputJson = IsJsonOutput(subArgs);
                var overwrite = subArgs.Any(a =>
                    a.Equals("--overwrite", StringComparison.OrdinalIgnoreCase)
                    || a.Equals("--force", StringComparison.OrdinalIgnoreCase));
                var includeSchema = !subArgs.Any(a => a.Equals("--no-schema", StringComparison.OrdinalIgnoreCase));

                try
                {
                    var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                    var projectRoot = TryGetProjectRoot(subArgs);
                    if (!string.IsNullOrWhiteSpace(projectRoot))
                        projectRoot = Path.GetFullPath(projectRoot.Trim().Trim('"'));
                    else
                        projectRoot = Directory.GetCurrentDirectory();

                    var projectPath = TryGetOptionValue(subArgs, "--project")
                        ?? TryGetOptionValue(subArgs, "--csproj");
                    var targetName = TryGetOptionValue(subArgs, "--target");
                    var framework = TryGetOptionValue(subArgs, "--framework");
                    var configuration = TryGetOptionValue(subArgs, "--configuration");
                    var runtimes = ParseCsvOptionValues(subArgs, "--rid", "--runtime");
                    var styleValues = ParseCsvOptionValues(subArgs, "--style");
                    var styles = styleValues
                        .Select(ParseDotNetPublishStyle)
                        .Distinct()
                        .ToArray();
                    var outputPath = TryGetOptionValue(subArgs, "--out")
                        ?? TryGetOptionValue(subArgs, "--config")
                        ?? TryGetOptionValue(subArgs, "--output-path")
                        ?? "powerforge.dotnetpublish.json";

                    var options = new DotNetPublishConfigScaffoldOptions
                    {
                        ProjectRoot = projectRoot,
                        ProjectPath = projectPath,
                        TargetName = targetName,
                        Framework = framework,
                        Configuration = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim(),
                        Runtimes = runtimes,
                        Styles = styles,
                        OutputPath = outputPath,
                        Overwrite = overwrite,
                        IncludeSchema = includeSchema
                    };

                    var scaffolder = new DotNetPublishConfigScaffolder(cmdLogger);
                    var generated = RunWithStatus(outputJson, cli, "Scaffolding dotnet publish config", () =>
                        scaffolder.Generate(options));

                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "dotnet.scaffold",
                            Success = true,
                            ExitCode = 0,
                            Config = "dotnetpublish",
                            ConfigPath = generated.ConfigPath,
                            Results = CliJson.SerializeToElement(generated, CliJson.Context.DotNetPublishConfigScaffoldResult),
                            Logs = LogsToJsonElement(logBuffer)
                        });
                        return 0;
                    }

                    cmdLogger.Success($"Generated dotnet publish config: {generated.ConfigPath}");
                    cmdLogger.Info($"Target: {generated.TargetName}");
                    cmdLogger.Info($"Project: {generated.ProjectPath}");
                    cmdLogger.Info($"Framework: {generated.Framework}");
                    cmdLogger.Info($"Runtimes: {string.Join(", ", generated.Runtimes)}");
                    cmdLogger.Info($"Styles: {string.Join(", ", generated.Styles.Select(s => s.ToString()))}");
                    return 0;
                }
                catch (Exception ex)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "dotnet.scaffold",
                            Success = false,
                            ExitCode = 1,
                            Error = ex.Message
                        });
                        return 1;
                    }

                    logger.Error(ex.Message);
                    return 1;
                }
            }
            default:
            {
                Console.WriteLine(DotNetPublishUsage);
                Console.WriteLine(DotNetScaffoldUsage);
                return 2;
            }
        }
    }
}
