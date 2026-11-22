using PowerForge;
using PowerForge.Cli;
using System.Diagnostics;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    public static int Main(string[] args)
    {
        using var consoleEncoding = ConsoleEncoding.UseUtf8(switchEncoding: true);

        var cli = ParseCliOptions(args, out var cliParseError);
        // ConsoleMonster reads NO_COLOR when it first detects the console.
        if (cli.NoColor) Environment.SetEnvironmentVariable("NO_COLOR", "1");
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
        if (IsVersionInvocation(args))
            return CommandVersion(args, typeof(Program).Assembly);

        if (filteredArgs.Length == 0 || filteredArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            filteredArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        var cmd = filteredArgs[0].ToLowerInvariant();
        if (cmd == "module")
            return CommandModule(filteredArgs, cli, CreateTextLogger(cli));

        return DispatchCommand(cmd, filteredArgs, cli);
    }

    // Keep references to hosted operations out of the JIT compilation of help, version and module commands.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DispatchCommand(string cmd, string[] filteredArgs, CliOptions cli)
    {
    var logger = CreateTextLogger(cli);
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
        case "nuget":
            return CommandNuGet(filteredArgs, cli, logger);
        case "plugin":
        case "plugins":
            return CommandPlugin(filteredArgs, cli, logger);
        case "agent-plugin":
            return CommandAgentPlugin(filteredArgs, cli, logger);
        case "project":
            return CommandProject(filteredArgs, cli, logger);
        case "release":
            return CommandRelease(filteredArgs, cli, logger);
        case "signing":
            return CommandSigning(filteredArgs, logger);
        case "validate-release":
            return CommandValidateRelease(filteredArgs, logger);
        case "apple-release":
            return CommandAppleRelease(filteredArgs, cli, logger);
        case "apple-testflight":
            return CommandAppleTestFlight(filteredArgs, logger);
        case "apple-deploy":
            return CommandAppleDeploy(filteredArgs, cli, logger);
        case "apple-screenshots":
            return CommandAppleScreenshots(filteredArgs, cli, logger);
        case "apple-simulator":
            return CommandAppleSimulator(filteredArgs, logger);
        case "apple-governance":
            return CommandAppleGovernance(filteredArgs, cli, logger);
        case "apple-review-details":
            return CommandAppleReviewDetails(filteredArgs, cli, logger);
        case "store":
            return CommandStore(filteredArgs, cli, logger);
        case "run":
            return CommandRun(filteredArgs, cli, logger);
        case "workspace":
            return CommandWorkspace(filteredArgs, cli, logger);
        case "test-dotnet":
            return CommandDotNetTest(filteredArgs, logger);
        case "powershell":
        case "ps":
            return CommandPowerShell(filteredArgs, cli, logger);
        case "github":
            return CommandGitHub(filteredArgs, cli, logger);
        case "homeassistant":
        case "hacs":
            return CommandHomeAssistant(filteredArgs, cli, logger);
        case "normalize":
        {
            var forge = new PowerForgeFacade(logger);
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

            var results = new PowerForgeFacade(logger).Format(targets);
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
                var pipeline = ModuleBuildPipelineFactory.Create(cmdLogger);
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
            return CommandTest(filteredArgs, cli, logger);
        case "pipeline":
            return CommandPipeline(filteredArgs, cli, logger);
        case "plan":
            return CommandPlan(filteredArgs, cli, logger);
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
