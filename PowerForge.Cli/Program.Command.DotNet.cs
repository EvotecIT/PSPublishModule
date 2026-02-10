using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static int CommandDotNet(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: powerforge dotnet publish [--config <DotNetPublish.json>] [--project-root <path>] [--plan] [--validate] [--output json] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>] [--skip-restore] [--skip-build]");
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

                    Console.WriteLine("Usage: powerforge dotnet publish [--config <DotNetPublish.json>] [--project-root <path>] [--plan] [--validate] [--output json] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>] [--skip-restore] [--skip-build]");
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

                    var runner = new DotNetPublishPipelineRunner(cmdLogger);
                    ApplyDotNetPublishOverrides(spec, overrideTargets, overrideRids, overrideFrameworks, overrideStyle);
                    var plan = runner.Plan(spec, specPath);
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
                    foreach (var a in result.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                    {
                        if (!string.IsNullOrWhiteSpace(a.OutputDir))
                            cmdLogger.Info($" -> {a.Target} {a.Framework} {a.Runtime} {a.Style}: {a.OutputDir}");
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
            default:
            {
                Console.WriteLine("Usage: powerforge dotnet publish [--config <DotNetPublish.json>] [--project-root <path>] [--plan] [--validate] [--output json] [--target <Name[,Name...]>] [--rid <Rid[,Rid...]>] [--framework <tfm[,tfm...]>] [--style <Portable|PortableCompat|PortableSize|AotSpeed|AotSize>] [--skip-restore] [--skip-build]");
                return 2;
            }
        }
    }
}

