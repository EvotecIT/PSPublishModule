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
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);

            var configPath = TryGetOptionValue(argv, "--config");
            var parsed = string.IsNullOrWhiteSpace(configPath) ? ParseBuildArgs(argv) : null;

            try
            {
                var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);

                // 1) Prefer explicit --config. If omitted and args are invalid, try default config discovery.
                var configToUse = configPath;
                if (string.IsNullOrWhiteSpace(configToUse) && parsed is null)
                {
                    var baseDir = TryGetProjectRoot(argv);
                    if (!string.IsNullOrWhiteSpace(baseDir))
                        baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
                    else
                        baseDir = Directory.GetCurrentDirectory();

                    configToUse = FindDefaultBuildConfig(baseDir);
                }

                // 2) If config is present, load either a BuildSpec or PipelineSpec.
                if (!string.IsNullOrWhiteSpace(configToUse))
                {
                    var fullConfigPath = ResolveExistingFilePath(configToUse);

                    if (LooksLikePipelineSpec(fullConfigPath))
                    {
                        var (spec, specPath) = LoadPipelineSpecWithPath(fullConfigPath);
                        ResolvePipelineSpecPaths(spec, specPath);

                        var runner = new ModulePipelineRunner(cmdLogger);
                        var plan = runner.Plan(spec);

                        var pipeline = new ModuleBuildPipeline(cmdLogger);
                        var res = RunWithStatus(outputJson, cli, $"Building {plan.ModuleName} {plan.ResolvedVersion}", () => pipeline.BuildToStaging(plan.BuildSpec));

                        if (outputJson)
                        {
                            WriteJson(new CliJsonEnvelope
                            {
                                SchemaVersion = OutputSchemaVersion,
                                Command = "build",
                                Success = true,
                                ExitCode = 0,
                                Config = "pipeline",
                                ConfigPath = specPath,
                                Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                                Plan = CliJson.SerializeToElement(plan, CliJson.Context.ModulePipelinePlan),
                                Result = CliJson.SerializeToElement(res, CliJson.Context.ModuleBuildResult),
                                Logs = LogsToJsonElement(logBuffer)
                            });
                            return 0;
                        }

                        logger.Success($"Built staging for {plan.ModuleName} {plan.ResolvedVersion} at {res.StagingPath}");
                        return 0;
                    }
                    else
                    {
                        var (spec, specPath) = LoadBuildSpecWithPath(fullConfigPath);
                        ResolveBuildSpecPaths(spec, specPath);

                        var pipeline = new ModuleBuildPipeline(cmdLogger);
                        var res = RunWithStatus(outputJson, cli, $"Building {spec.Name} {spec.Version}", () => pipeline.BuildToStaging(spec));

                        if (outputJson)
                        {
                            WriteJson(new CliJsonEnvelope
                            {
                                SchemaVersion = OutputSchemaVersion,
                                Command = "build",
                                Success = true,
                                ExitCode = 0,
                                Config = "build",
                                ConfigPath = specPath,
                                Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModuleBuildSpec),
                                Result = CliJson.SerializeToElement(res, CliJson.Context.ModuleBuildResult),
                                Logs = LogsToJsonElement(logBuffer)
                            });
                            return 0;
                        }

                        logger.Success($"Built staging for {spec.Name} {spec.Version} at {res.StagingPath}");
                        return 0;
                    }
                }

                // 3) No config => use explicit arguments.
                if (parsed is null)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "build",
                            Success = false,
                            ExitCode = 2,
                            Error = "Invalid arguments (missing --config and no default config found)."
                        });
                        return 2;
                    }

                    PrintHelp();
                    return 2;
                }

                var p = parsed.Value;
                var specFromArgs = new ModuleBuildSpec
                {
                    Name = p.Name,
                    SourcePath = p.Source,
                    StagingPath = p.Staging,
                    CsprojPath = p.Csproj,
                    Version = p.Version,
                    Configuration = p.Configuration,
                    Frameworks = p.Frameworks,
                    Author = p.Author,
                    CompanyName = p.CompanyName,
                    Description = p.Description,
                    Tags = p.Tags,
                    IconUri = p.IconUri,
                    ProjectUri = p.ProjectUri,
                };

                var pipelineFromArgs = new ModuleBuildPipeline(cmdLogger);
                var resFromArgs = RunWithStatus(outputJson, cli, $"Building {specFromArgs.Name} {specFromArgs.Version}", () => pipelineFromArgs.BuildToStaging(specFromArgs));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "build",
                        Success = true,
                        ExitCode = 0,
                        Config = "args",
                        Spec = CliJson.SerializeToElement(specFromArgs, CliJson.Context.ModuleBuildSpec),
                        Result = CliJson.SerializeToElement(resFromArgs, CliJson.Context.ModuleBuildResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Built staging for {specFromArgs.Name} {specFromArgs.Version} at {resFromArgs.StagingPath}");
                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "build",
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
        case "template":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var outputJson = IsJsonOutput(argv);

            var scriptPath = TryGetOptionValue(argv, "--script") ?? TryGetOptionValue(argv, "--build-script");
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "template",
                        Success = false,
                        ExitCode = 2,
                        Error = "Missing --script <Build-Module.ps1>."
                    });
                }
                Console.WriteLine("Usage: powerforge template --script <Build-Module.ps1> [--out <path>] [--project-root <path>] [--powershell <path>] [--output json]");
                return 2;
            }

            try
            {
                var fullScriptPath = ResolveExistingFilePath(scriptPath);
                var projectRoot = TryGetProjectRoot(argv);
                if (!string.IsNullOrWhiteSpace(projectRoot))
                    projectRoot = Path.GetFullPath(projectRoot.Trim().Trim('"'));
                if (string.IsNullOrWhiteSpace(projectRoot))
                    projectRoot = Path.GetDirectoryName(fullScriptPath) ?? Directory.GetCurrentDirectory();

                var outPath = TryGetOptionValue(argv, "--out") ?? TryGetOptionValue(argv, "--out-path") ?? TryGetOptionValue(argv, "--output-path");
                if (string.IsNullOrWhiteSpace(outPath))
                    outPath = Path.Combine(projectRoot, "powerforge.json");
                else
                    outPath = Path.GetFullPath(outPath.Trim().Trim('"'));

                var shell = TryGetOptionValue(argv, "--powershell");
                if (string.IsNullOrWhiteSpace(shell))
                    shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

                var psScript = BuildTemplateScript(fullScriptPath, outPath, projectRoot);
                var psArgs = BuildPowerShellArgs(psScript);

                var result = RunProcess(shell, psArgs, projectRoot);
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "template",
                        Success = result.ExitCode == 0,
                        ExitCode = result.ExitCode,
                        Error = result.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(result.Error) ? "Template generation failed." : result.Error),
                        Config = "pipeline",
                        ConfigPath = outPath
                    });
                    return result.ExitCode;
                }

                if (result.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        logger.Error(result.Error);
                    return result.ExitCode;
                }

                logger.Success($"Generated {outPath}");
                return 0;
            }
            catch (Exception ex)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "template",
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
        case "docs":
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
                        Command = "docs",
                        Success = false,
                        ExitCode = 2,
                        Error = "Missing --config and no default pipeline config found."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge docs [--config <Pipeline.json>] [--output json]");
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
                        Command = "docs",
                        Success = false,
                        ExitCode = 2,
                        Error = ex.Message
                    });
                    return 2;
                }

                logger.Error(ex.Message);
                return 2;
            }

            // Ensure docs-only run: no install, no publish, no artefacts.
            spec.Install ??= new ModulePipelineInstallOptions();
            spec.Install.Enabled = false;
            spec.Segments = (spec.Segments ?? Array.Empty<IConfigurationSegment>())
                .Where(s => s is not ConfigurationArtefactSegment && s is not ConfigurationPublishSegment)
                .ToArray();

            try
            {
                var interactive = PipelineConsoleUi.ShouldUseInteractiveView(outputJson, cli);
                var (cmdLogger, logBuffer) = interactive
                    ? (new NullLogger { IsVerbose = cli.Verbose }, null)
                    : CreateCommandLogger(outputJson, cli, logger);
                var runner = new ModulePipelineRunner(cmdLogger);

                var plan = interactive
                    ? runner.Plan(spec)
                    : RunWithStatus(outputJson, cli, "Planning docs", () => runner.Plan(spec));
                if (plan.DocumentationBuild?.Enable != true || plan.Documentation is null)
                {
                    const string msg = "Docs are not enabled in the pipeline config. Add Documentation + BuildDocumentation segments (Enable=true).";
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "docs",
                            Success = false,
                            ExitCode = 2,
                            Error = msg,
                            Plan = CliJson.SerializeToElement(plan, CliJson.Context.ModulePipelinePlan),
                            Logs = LogsToJsonElement(logBuffer)
                        });
                        return 2;
                    }

                    logger.Error(msg);
                    return 2;
                }

                var res = interactive
                    ? PipelineConsoleUi.Run(runner, spec, plan, configPath, outputJson, cli)
                    : RunWithStatus(outputJson, cli, "Generating docs", () => runner.Run(spec, plan));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "docs",
                        Success = true,
                        ExitCode = 0,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                        Plan = CliJson.SerializeToElement(res.Plan, CliJson.Context.ModulePipelinePlan),
                        Result = res.DocumentationResult is null ? null : CliJson.SerializeToElement(res.DocumentationResult, CliJson.Context.DocumentationBuildResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Docs generated for {res.Plan.ModuleName} {res.Plan.ResolvedVersion}");
                if (res.DocumentationResult is not null)
                {
                    logger.Info($"Docs: {res.DocumentationResult.DocsPath}");
                    logger.Info($"Readme: {res.DocumentationResult.ReadmePath}");
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
                        Command = "docs",
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
        case "pack":
        {
            var argv = filteredArgs.Skip(1).ToArray();
            var configPath = TryGetOptionValue(argv, "--config");
            var outPath = TryGetOptionValue(argv, "--out") ?? TryGetOptionValue(argv, "--out-path") ?? TryGetOptionValue(argv, "--output-path");
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
                        Command = "pack",
                        Success = false,
                        ExitCode = 2,
                        Error = "Missing --config and no default pipeline config found."
                    });
                    return 2;
                }

                Console.WriteLine("Usage: powerforge pack [--config <Pipeline.json>] [--out <path>] [--output json]");
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
                        Command = "pack",
                        Success = false,
                        ExitCode = 2,
                        Error = ex.Message
                    });
                    return 2;
                }

                logger.Error(ex.Message);
                return 2;
            }

            // Ensure pack-only run: no install, no publish, no docs.
            spec.Install ??= new ModulePipelineInstallOptions();
            spec.Install.Enabled = false;
            spec.Segments = (spec.Segments ?? Array.Empty<IConfigurationSegment>())
                .Where(s => s is not ConfigurationDocumentationSegment && s is not ConfigurationBuildDocumentationSegment && s is not ConfigurationPublishSegment)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(outPath))
            {
                var enabled = spec.Segments.OfType<ConfigurationArtefactSegment>()
                    .Where(a => a.Configuration?.Enabled == true)
                    .ToArray();

                foreach (var a in enabled)
                {
                    a.Configuration ??= new ArtefactConfiguration();
                    a.Configuration.Path = enabled.Length <= 1
                        ? outPath
                        : Path.Combine(outPath, a.ArtefactType.ToString());
                }
            }

            try
            {
                var interactive = PipelineConsoleUi.ShouldUseInteractiveView(outputJson, cli);
                var (cmdLogger, logBuffer) = interactive
                    ? (new NullLogger { IsVerbose = cli.Verbose }, null)
                    : CreateCommandLogger(outputJson, cli, logger);
                var runner = new ModulePipelineRunner(cmdLogger);

                var plan = interactive
                    ? runner.Plan(spec)
                    : RunWithStatus(outputJson, cli, "Planning pack", () => runner.Plan(spec));
                if (plan.Artefacts.Length == 0)
                {
                    const string msg = "No enabled artefact segments found in the pipeline config.";
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "pack",
                            Success = false,
                            ExitCode = 2,
                            Error = msg,
                            Plan = CliJson.SerializeToElement(plan, CliJson.Context.ModulePipelinePlan),
                            Logs = LogsToJsonElement(logBuffer)
                        });
                        return 2;
                    }

                    logger.Error(msg);
                    return 2;
                }

                var res = interactive
                    ? PipelineConsoleUi.Run(runner, spec, plan, configPath, outputJson, cli)
                    : RunWithStatus(outputJson, cli, "Packing artefacts", () => runner.Run(spec, plan));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "pack",
                        Success = true,
                        ExitCode = 0,
                        Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                        Plan = CliJson.SerializeToElement(res.Plan, CliJson.Context.ModulePipelinePlan),
                        Artefacts = CliJson.SerializeToElement(res.ArtefactResults, CliJson.Context.ArtefactBuildResultArray),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Packed artefacts for {res.Plan.ModuleName} {res.Plan.ResolvedVersion}");
                foreach (var a in res.ArtefactResults)
                {
                    if (!string.IsNullOrWhiteSpace(a.OutputPath)) logger.Info($" â†’ {a.OutputPath}");
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
                        Command = "pack",
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
        case "dotnet":
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
                        if (!string.IsNullOrWhiteSpace(result?.ManifestJsonPath))
                            cmdLogger.Info($"Manifest: {result!.ManifestJsonPath}");
                        foreach (var a in result?.Artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
                        {
                            if (!string.IsNullOrWhiteSpace(a.OutputDir))
                                cmdLogger.Info($" â†’ {a.Target} {a.Framework} {a.Runtime} {a.Style}: {a.OutputDir}");
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
                foreach (var path in res.InstalledPaths) logger.Info($" â†’ {path}");
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
