using PowerForge;
using PowerForge.Cli;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    private const int OutputSchemaVersion = 1;

    public static int Main(string[] args)
    {

    ConsoleEncoding.EnsureUtf8();
    try
    {
        if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
            AnsiConsole.Profile.Capabilities.Unicode = true;
    }
    catch
    {
        // best effort only
    }

    var cli = ParseCliOptions(args, out var cliParseError);
    if (!string.IsNullOrWhiteSpace(cliParseError))
    {
        if (IsJsonOutput(args ?? Array.Empty<string>()))
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "cli",
                Success = false,
                ExitCode = 2,
                Error = cliParseError
            });
            return 2;
        }

        Console.WriteLine(cliParseError);
        PrintHelp();
        return 2;
    }

    var filteredArgs = StripGlobalArgs(args);

    ILogger logger = CreateTextLogger(cli);
    var forge = new PowerForgeFacade(logger);

    if (filteredArgs.Length == 0 || filteredArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || filteredArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
    {
        PrintHelp();
        return 0;
    }

    var cmd = filteredArgs[0].ToLowerInvariant();
    switch (cmd)
    {
        case "build":
            return CommandBuild(filteredArgs, cli, logger);
        case "template":
            return CommandTemplate(filteredArgs, cli, logger);
        case "docs":
            return CommandDocs(filteredArgs, cli, logger);
        case "pack":
            return CommandPack(filteredArgs, cli, logger);
        case "dotnet":
            return CommandDotNet(filteredArgs, cli, logger);
        case "github":
            return CommandGitHub(filteredArgs, cli, logger);
        case "normalize":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);
 
            var targets = ParseTargets(argv);
            if (targets.Length == 0)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "normalize",
                        Success = false,
                        ExitCode = 2,
                        Error = "At least one file is required."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge normalize <files...>");
                return 2;
            }

            if (outputJson)
            {
                var results = new List<NormalizationResult>();
                foreach (var f in targets) results.Add(forge.Normalize(f));
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "normalize",
                    Success = true,
                    ExitCode = 0,
                    Results = CliJson.SerializeToElement(results.ToArray(), CliJson.Context.NormalizationResultArray)
                });
                return 0;
            }

            foreach (var f in targets)
            {
                var res = forge.Normalize(f);
                var msg = res.Changed ? $"Normalized {res.Path} ({res.Replacements} changes, {res.EncodingName})" : $"No changes: {res.Path}";
                logger.Info(msg);
            }
            return 0;
        }
        case "format":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);

            var targets = ParseTargets(argv);
            if (targets.Length == 0)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "format",
                        Success = false,
                        ExitCode = 2,
                        Error = "At least one file is required."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge format <files...>");
                return 2;
            }

            if (outputJson)
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var jsonForge = new PowerForgeFacade(cmdLogger);
                var jsonResults = jsonForge.Format(targets);
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "format",
                    Success = true,
                    ExitCode = 0,
                    Results = CliJson.SerializeToElement(jsonResults.ToArray(), CliJson.Context.FormatterResultArray),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return 0;
            }

            var results = forge.Format(targets);
            foreach (var r in results)
            {
                var prefix = r.Changed ? "Formatted" : "Unchanged";
                logger.Info($"{prefix}: {r.Path} ({r.Message})");
            }
            return 0;
        }
        case "install":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var configPath = TryGetOptionValue(argv, "--config");
            var outputJson = IsJsonOutput(argv);

            ModuleInstallSpec spec;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                var loaded = LoadInstallSpecWithPath(configPath);
                ResolveInstallSpecPaths(loaded.Value, loaded.FullPath);
                spec = loaded.Value;
            }
            else
            {
                var parsed = ParseInstallArgs(argv);
                if (parsed is null)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "install",
                            Success = false,
                            ExitCode = 2,
                            Error = "Invalid arguments."
                        });
                        return 2;
                    }

                    PrintHelp();
                    return 2;
                }
                var p = parsed.Value;
                spec = new ModuleInstallSpec
                {
                    Name = p.Name,
                    Version = p.Version,
                    StagingPath = p.Staging,
                    Strategy = p.Strategy,
                    KeepVersions = p.Keep,
                    Roots = p.Roots,
                };
            }

            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var pipeline = new ModuleBuildPipeline(cmdLogger);
                var res = RunWithStatus(outputJson, cli, $"Installing {spec.Name} {spec.Version}", () => pipeline.InstallFromStaging(spec));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "install",
                        Success = true,
                        ExitCode = 0,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModuleInstallSpec),
                        Result = CliJson.SerializeToElement(res, CliJson.Context.ModuleInstallerResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Installed {spec.Name} {res.Version}");
                foreach (var path in res.InstalledPaths) logger.Info($" \u2192 {path}");
                if (res.PrunedPaths.Count > 0) logger.Warn($"Pruned versions: {res.PrunedPaths.Count}");
                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "install",
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
        case "test":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var configPath = TryGetOptionValue(argv, "--config");
            var outputJson = IsJsonOutput(argv);

            ModuleTestSuiteSpec spec;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                var loaded = LoadTestSuiteSpecWithPath(configPath);
                ResolveTestSpecPaths(loaded.Value, loaded.FullPath);
                spec = loaded.Value;
            }
            else
            {
                var parsed = ParseTestArgs(argv);
                if (parsed is null)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "test",
                            Success = false,
                            ExitCode = 2,
                            Error = "Invalid arguments."
                        });
                        return 2;
                    }

                    PrintHelp();
                    return 2;
                }
                spec = parsed;
            }

            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var service = new ModuleTestSuiteService(new PowerShellRunner(), cmdLogger);
                var res = RunWithStatus(outputJson, cli, "Running test suite", () => service.Run(spec));

                var success = res.FailedCount == 0;
                var exitCode = success ? 0 : 1;

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "test",
                        Success = success,
                        ExitCode = exitCode,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModuleTestSuiteSpec),
                        Result = CliJson.SerializeToElement(res, CliJson.Context.ModuleTestSuiteResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return exitCode;
                }

                if (!string.IsNullOrWhiteSpace(res.StdOut))
                    Console.WriteLine(res.StdOut);

                if (!string.IsNullOrWhiteSpace(res.StdErr))
                    logger.Warn(res.StdErr.Trim());

                logger.Info($"Tests: {res.PassedCount}/{res.TotalCount} passed (failed: {res.FailedCount}, skipped: {res.SkippedCount})");
                if (res.Duration is not null) logger.Info($"Duration: {res.Duration}");
                if (res.CoveragePercent is not null) logger.Info($"Coverage: {res.CoveragePercent:0.00}%");

                if (!success && res.FailureAnalysis is not null && res.FailureAnalysis.FailedTests.Length > 0)
                {
                    logger.Error($"Failed tests ({res.FailureAnalysis.FailedTests.Length}):");
                    foreach (var f in res.FailureAnalysis.FailedTests)
                        logger.Error($" - {f.Name}");
                }

                if (success) logger.Success("Test suite passed");
                else logger.Error("Test suite failed");

                return exitCode;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "test",
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
        case "pipeline":
        case "run":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var configPath = TryGetOptionValue(argv, "--config");
            var outputJson = IsJsonOutput(argv);

            if (string.IsNullOrWhiteSpace(configPath))
            {
                var baseDir = TryGetProjectRoot(argv);
                if (!string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
                else
                    baseDir = Directory.GetCurrentDirectory();

                configPath = FindDefaultPipelineConfig(baseDir);
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "pipeline",
                        Success = false,
                        ExitCode = 2,
                        Error = "Missing required --config."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge pipeline --config <Pipeline.json> [--output json]");
                return 2;
            }

            ModulePipelineSpec spec;
            try
            {
                var loaded = LoadPipelineSpecWithPath(configPath);
                spec = loaded.Value;
                ResolvePipelineSpecPaths(spec, loaded.FullPath);
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "pipeline",
                        Success = false,
                        ExitCode = 2,
                        Error = ex.Message
                    });
                    return 2;
                }

                logger.Error(ex.Message);
                return 2;
            }

            BufferingLogger? interactiveBuffer = null;
            try
            {
                var interactive = PipelineConsoleUi.ShouldUseInteractiveView(outputJson, cli);
                var (cmdLogger, logBuffer) = interactive
                    ? (interactiveBuffer = new BufferingLogger { IsVerbose = cli.Verbose }, interactiveBuffer)
                    : CreateCommandLogger(outputJson, cli, logger);
                var runner = new ModulePipelineRunner(cmdLogger);
                var plan = runner.Plan(spec);
                var res = interactive
                    ? PipelineConsoleUi.Run(runner, spec, plan, configPath, outputJson, cli)
                    : RunWithStatus(outputJson, cli, "Running pipeline", () => runner.Run(spec, plan));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "pipeline",
                        Success = true,
                        ExitCode = 0,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                        Result = CliJson.SerializeToElement(res, CliJson.Context.ModulePipelineResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                WritePipelineSummary(res, cli, logger);
                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "pipeline",
                        Success = false,
                        ExitCode = 1,
                        Error = ex.Message
                    });
                    return 1;
                }

                if (interactiveBuffer is not null && interactiveBuffer.Entries.Count > 0 && !cli.Quiet)
                    WriteLogTail(interactiveBuffer, logger);
                logger.Error(ex.Message);
                return 1;
            }
        }
        case "plan":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var configPath = TryGetOptionValue(argv, "--config");
            var outputJson = IsJsonOutput(argv);

            if (string.IsNullOrWhiteSpace(configPath))
            {
                var baseDir = TryGetProjectRoot(argv);
                if (!string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
                else
                    baseDir = Directory.GetCurrentDirectory();

                configPath = FindDefaultPipelineConfig(baseDir);
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "plan",
                        Success = false,
                        ExitCode = 2,
                        Error = "Missing required --config."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge plan --config <Pipeline.json> [--output json]");
                return 2;
            }

            ModulePipelineSpec spec;
            try
            {
                var loaded = LoadPipelineSpecWithPath(configPath);
                spec = loaded.Value;
                ResolvePipelineSpecPaths(spec, loaded.FullPath);
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "plan",
                        Success = false,
                        ExitCode = 2,
                        Error = ex.Message
                    });
                    return 2;
                }

                logger.Error(ex.Message);
                return 2;
            }

            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var runner = new ModulePipelineRunner(cmdLogger);
                var plan = RunWithStatus(outputJson, cli, "Planning pipeline", () => runner.Plan(spec));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "plan",
                        Success = true,
                        ExitCode = 0,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                        Plan = CliJson.SerializeToElement(plan, CliJson.Context.ModulePipelinePlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Plan: {plan.ModuleName} {plan.ResolvedVersion}");
                logger.Info($"Project: {plan.ProjectRoot}");
                logger.Info($"Staging: {plan.BuildSpec.StagingPath}");
                logger.Info($"Install: {(plan.InstallEnabled ? "yes" : "no")} ({plan.InstallStrategy}, keep {plan.InstallKeepVersions})");
                foreach (var root in plan.InstallRoots) logger.Info($"Root: {root}");

                if (plan.DocumentationBuild is not null && plan.DocumentationBuild.Enable)
                {
                    logger.Info($"Docs: {plan.Documentation?.Path} ({plan.DocumentationBuild.Tool})");
                }

                if (plan.Artefacts.Length > 0) logger.Info($"Artefacts: {plan.Artefacts.Length}");
                if (plan.Publishes.Length > 0) logger.Info($"Publishes: {plan.Publishes.Length}");

                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "plan",
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
        case "find":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);
            var parsed = ParseFindArgs(argv);
            if (parsed is null)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "find",
                        Success = false,
                        ExitCode = 2,
                        Error = "Invalid arguments."
                    });
                    return 2;
                }

                PrintHelp();
                return 2;
            }
            var p = parsed.Value;

            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var runner = new PowerShellRunner();
                var client = new PSResourceGetClient(runner, cmdLogger);
                var opts = new PSResourceFindOptions(p.Names, p.Version, p.Prerelease, p.Repositories);
                var results = RunWithStatus(outputJson, cli, "Finding resources", () => client.Find(opts));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "find",
                        Success = true,
                        ExitCode = 0,
                        Results = CliJson.SerializeToElement(results.ToArray(), CliJson.Context.PSResourceInfoArray),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                foreach (var r in results)
                {
                    Console.WriteLine($"{r.Name}\t{r.Version}\t{r.Repository ?? string.Empty}");
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
                        Command = "find",
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
        case "publish":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);
            RepositoryPublishRequest request;
            try
            {
                request = ParsePublishArgs(argv) ?? throw new InvalidOperationException("Missing required --path.");
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "publish",
                        Success = false,
                        ExitCode = 2,
                        Error = ex.Message
                    });
                    return 2;
                }

                logger.Error(ex.Message);
                PrintHelp();
                return 2;
            }
 
            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                var publisher = new RepositoryPublisher(cmdLogger);
                var result = RunWithStatus(outputJson, cli, "Publishing", () => publisher.Publish(request));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "publish",
                        Success = true,
                        ExitCode = 0,
                        Logs = LogsToJsonElement(logBuffer)
                    },
                    writer =>
                    {
                        writer.WriteString("path", result.Path);
                        writer.WriteBoolean("isNupkg", result.IsNupkg);
                        writer.WriteString("repository", result.RepositoryName);
                        writer.WriteString("tool", result.Tool.ToString());

                        writer.WritePropertyName("destinationPath");
                        if (string.IsNullOrWhiteSpace(request.DestinationPath)) writer.WriteNullValue();
                        else writer.WriteStringValue(request.DestinationPath);

                        writer.WriteBoolean("skipDependenciesCheck", request.SkipDependenciesCheck);
                        writer.WriteBoolean("skipModuleManifestValidate", request.SkipModuleManifestValidate);
                        writer.WriteBoolean("repositoryCreated", result.RepositoryCreated);
                        writer.WriteBoolean("repositoryUnregistered", result.RepositoryUnregistered);
                    });
                    return 0;
                }

                logger.Success($"Published {result.Path} to {result.RepositoryName} ({result.Tool})");
                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "publish",
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
            PrintHelp();
            return 2;
    }

    }
}
