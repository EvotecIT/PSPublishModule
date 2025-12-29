using PowerForge;
using PowerForge.Cli;
using System.Text.Json;
using System.Text.Json.Serialization;

const int OutputSchemaVersion = 1;

var cli = ParseCliOptions(args, out var cliParseError);
if (!string.IsNullOrWhiteSpace(cliParseError))
{
    if (IsJsonOutput(args ?? Array.Empty<string>()))
    {
        WriteJson(new { schemaVersion = OutputSchemaVersion, command = "cli", success = false, exitCode = 2, error = cliParseError });
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
                var (_, fullPath) = LoadJsonWithPath<JsonElement>(configToUse);
                var fullConfigPath = fullPath;

                if (LooksLikePipelineSpec(fullConfigPath))
                {
                    var (spec, specPath) = LoadJsonWithPath<ModulePipelineSpec>(fullConfigPath);
                    ResolvePipelineSpecPaths(spec, specPath);

                    var runner = new ModulePipelineRunner(cmdLogger);
                    var plan = runner.Plan(spec);

                    var pipeline = new ModuleBuildPipeline(cmdLogger);
                    var res = RunWithStatus(outputJson, cli, $"Building {plan.ModuleName} {plan.ResolvedVersion}", () => pipeline.BuildToStaging(plan.BuildSpec));

                    if (outputJson)
                    {
                        WriteJson(new
                        {
                            schemaVersion = OutputSchemaVersion,
                            command = "build",
                            success = true,
                            exitCode = 0,
                            config = "pipeline",
                            configPath = specPath,
                            spec,
                            plan,
                            result = res,
                            logs = logBuffer?.Entries
                        });
                        return 0;
                    }

                    logger.Success($"Built staging for {plan.ModuleName} {plan.ResolvedVersion} at {res.StagingPath}");
                    return 0;
                }
                else
                {
                    var (spec, specPath) = LoadJsonWithPath<ModuleBuildSpec>(fullConfigPath);
                    ResolveBuildSpecPaths(spec, specPath);

                    var pipeline = new ModuleBuildPipeline(cmdLogger);
                    var res = RunWithStatus(outputJson, cli, $"Building {spec.Name} {spec.Version}", () => pipeline.BuildToStaging(spec));

                    if (outputJson)
                    {
                        WriteJson(new
                        {
                            schemaVersion = OutputSchemaVersion,
                            command = "build",
                            success = true,
                            exitCode = 0,
                            config = "build",
                            configPath = specPath,
                            spec,
                            result = res,
                            logs = logBuffer?.Entries
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
                    WriteJson(new { schemaVersion = OutputSchemaVersion, command = "build", success = false, exitCode = 2, error = "Invalid arguments (missing --config and no default config found)." });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "build",
                    success = true,
                    exitCode = 0,
                    config = "args",
                    spec = specFromArgs,
                    result = resFromArgs,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "build", success = false, exitCode = 1, error = ex.Message });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "docs", success = false, exitCode = 2, error = "Missing --config and no default pipeline config found." });
                return 2;
            }

            Console.WriteLine("Usage: powerforge docs [--config <Pipeline.json>] [--output json]");
            return 2;
        }

        ModulePipelineSpec spec;
        try
        {
            var loaded = LoadJsonWithPath<ModulePipelineSpec>(configPath);
            spec = loaded.Value;
            ResolvePipelineSpecPaths(spec, loaded.FullPath);
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "docs", success = false, exitCode = 2, error = ex.Message });
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
                    WriteJson(new { schemaVersion = OutputSchemaVersion, command = "docs", success = false, exitCode = 2, error = msg, plan, logs = logBuffer?.Entries });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "docs",
                    success = true,
                    exitCode = 0,
                    spec,
                    plan = res.Plan,
                    result = res.DocumentationResult,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "docs", success = false, exitCode = 1, error = ex.Message });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pack", success = false, exitCode = 2, error = "Missing --config and no default pipeline config found." });
                return 2;
            }

            Console.WriteLine("Usage: powerforge pack [--config <Pipeline.json>] [--out <path>] [--output json]");
            return 2;
        }

        ModulePipelineSpec spec;
        try
        {
            var loaded = LoadJsonWithPath<ModulePipelineSpec>(configPath);
            spec = loaded.Value;
            ResolvePipelineSpecPaths(spec, loaded.FullPath);
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pack", success = false, exitCode = 2, error = ex.Message });
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
                    WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pack", success = false, exitCode = 2, error = msg, plan, logs = logBuffer?.Entries });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "pack",
                    success = true,
                    exitCode = 0,
                    spec,
                    plan = res.Plan,
                    artefacts = res.ArtefactResults,
                    logs = logBuffer?.Entries
                });
                return 0;
            }

            logger.Success($"Packed artefacts for {res.Plan.ModuleName} {res.Plan.ResolvedVersion}");
            foreach (var a in res.ArtefactResults)
            {
                if (!string.IsNullOrWhiteSpace(a.OutputPath)) logger.Info($" → {a.OutputPath}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pack", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "normalize", success = false, exitCode = 2, error = "At least one file is required." });
                return 2;
            }

            Console.WriteLine("Usage: powerforge normalize <files...>");
            return 2;
        }

        if (outputJson)
        {
            var results = new List<NormalizationResult>();
            foreach (var f in targets) results.Add(forge.Normalize(f));
            WriteJson(new { schemaVersion = OutputSchemaVersion, command = "normalize", success = true, exitCode = 0, results });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "format", success = false, exitCode = 2, error = "At least one file is required." });
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
            WriteJson(new { schemaVersion = OutputSchemaVersion, command = "format", success = true, exitCode = 0, results = jsonResults, logs = logBuffer?.Entries });
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
            var loaded = LoadJsonWithPath<ModuleInstallSpec>(configPath);
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
                    WriteJson(new { schemaVersion = OutputSchemaVersion, command = "install", success = false, exitCode = 2, error = "Invalid arguments." });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "install",
                    success = true,
                    exitCode = 0,
                    spec,
                    result = res,
                    logs = logBuffer?.Entries
                });
                return 0;
            }

            logger.Success($"Installed {spec.Name} {res.Version}");
            foreach (var path in res.InstalledPaths) logger.Info($" → {path}");
            if (res.PrunedPaths.Count > 0) logger.Warn($"Pruned versions: {res.PrunedPaths.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "install", success = false, exitCode = 1, error = ex.Message });
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
            var loaded = LoadJsonWithPath<ModuleTestSuiteSpec>(configPath);
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
                    WriteJson(new { schemaVersion = OutputSchemaVersion, command = "test", success = false, exitCode = 2, error = "Invalid arguments." });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "test",
                    success,
                    exitCode,
                    spec,
                    result = res,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "test", success = false, exitCode = 1, error = ex.Message });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pipeline", success = false, exitCode = 2, error = "Missing required --config." });
                return 2;
            }

            Console.WriteLine("Usage: powerforge pipeline --config <Pipeline.json> [--output json]");
            return 2;
        }

        ModulePipelineSpec spec;
        try
        {
            var loaded = LoadJsonWithPath<ModulePipelineSpec>(configPath);
            spec = loaded.Value;
            ResolvePipelineSpecPaths(spec, loaded.FullPath);
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pipeline", success = false, exitCode = 2, error = ex.Message });
                return 2;
            }

            logger.Error(ex.Message);
            return 2;
        }

        try
        {
            var interactive = PipelineConsoleUi.ShouldUseInteractiveView(outputJson, cli);
            var (cmdLogger, logBuffer) = interactive
                ? (new NullLogger { IsVerbose = cli.Verbose }, null)
                : CreateCommandLogger(outputJson, cli, logger);
            var runner = new ModulePipelineRunner(cmdLogger);
            var plan = runner.Plan(spec);
            var res = interactive
                ? PipelineConsoleUi.Run(runner, spec, plan, configPath, outputJson, cli)
                : RunWithStatus(outputJson, cli, "Running pipeline", () => runner.Run(spec, plan));

            if (outputJson)
            {
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "pipeline",
                    success = true,
                    exitCode = 0,
                    spec,
                    result = res,
                    logs = logBuffer?.Entries
                });
                return 0;
            }

            logger.Success($"Pipeline built {res.Plan.ModuleName} {res.Plan.ResolvedVersion}");
            logger.Info($"Staging: {res.BuildResult.StagingPath}");
            if (res.InstallResult is not null)
            {
                logger.Success($"Installed {res.Plan.ModuleName} {res.InstallResult.Version}");
                foreach (var path in res.InstallResult.InstalledPaths) logger.Info($" → {path}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "pipeline", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "plan", success = false, exitCode = 2, error = "Missing required --config." });
                return 2;
            }

            Console.WriteLine("Usage: powerforge plan --config <Pipeline.json> [--output json]");
            return 2;
        }

        ModulePipelineSpec spec;
        try
        {
            var loaded = LoadJsonWithPath<ModulePipelineSpec>(configPath);
            spec = loaded.Value;
            ResolvePipelineSpecPaths(spec, loaded.FullPath);
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "plan", success = false, exitCode = 2, error = ex.Message });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "plan",
                    success = true,
                    exitCode = 0,
                    spec,
                    plan,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "plan", success = false, exitCode = 1, error = ex.Message });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "find", success = false, exitCode = 2, error = "Invalid arguments." });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "find",
                    success = true,
                    exitCode = 0,
                    results,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "find", success = false, exitCode = 1, error = ex.Message });
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "publish", success = false, exitCode = 2, error = ex.Message });
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
                WriteJson(new
                {
                    schemaVersion = OutputSchemaVersion,
                    command = "publish",
                    success = true,
                    exitCode = 0,
                    path = result.Path,
                    isNupkg = result.IsNupkg,
                    repository = result.RepositoryName,
                    tool = result.Tool,
                    destinationPath = request.DestinationPath,
                    skipDependenciesCheck = request.SkipDependenciesCheck,
                    skipModuleManifestValidate = request.SkipModuleManifestValidate,
                    repositoryCreated = result.RepositoryCreated,
                    repositoryUnregistered = result.RepositoryUnregistered,
                    logs = logBuffer?.Entries
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
                WriteJson(new { schemaVersion = OutputSchemaVersion, command = "publish", success = false, exitCode = 1, error = ex.Message });
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

static void PrintHelp()
{
    Console.WriteLine(@"PowerForge CLI
Usage:
  powerforge build --name <ModuleName> --project-root <path> --version <X.Y.Z> [--csproj <path>] [--staging <path>] [--configuration Release] [--framework <tfm>]* [--author <name>] [--company <name>] [--description <text>] [--tag <tag>]* [--output json]
  powerforge build [--config <BuildSpec|Pipeline>.json] [--project-root <path>] [--output json]
  powerforge docs [--config <Pipeline.json>] [--project-root <path>] [--output json]
  powerforge pack [--config <Pipeline.json>] [--project-root <path>] [--out <path>] [--output json]
  powerforge normalize <files...>   Normalize encodings and line endings [--output json]
  powerforge format <files...>      Format scripts via PSScriptAnalyzer (out-of-proc) [--output json]
  powerforge test [--project-root <path>] [--test-path <path>] [--format Detailed|Normal|Minimal] [--coverage] [--force]
                 [--skip-dependencies] [--skip-import] [--keep-xml] [--timeout <seconds>] [--output json]
  powerforge test --config <TestSpec.json>
  powerforge install --name <ModuleName> --version <X.Y.Z> --staging <path> [--strategy exact|autorevision] [--keep N] [--root path]*
  powerforge install --config <InstallSpec.json>
  powerforge pipeline [--config <Pipeline.json>] [--project-root <path>] [--output json]
  powerforge plan [--config <Pipeline.json>] [--project-root <path>] [--output json]
  powerforge find --name <Name>[,<Name>...] [--repo <Repo>] [--version <X.Y.Z>] [--prerelease]
  powerforge publish --path <Path> [--repo <Repo>] [--tool auto|psresourceget|powershellget] [--apikey <Key>] [--nupkg]
                   [--destination <Path>] [--skip-dependencies-check] [--skip-manifest-validate]
                   [--repo-uri <Uri>] [--repo-source-uri <Uri>] [--repo-publish-uri <Uri>] [--repo-priority <N>] [--repo-api-version auto|v2|v3]
                   [--repo-trusted|--repo-untrusted] [--repo-ensure|--no-repo-ensure] [--repo-unregister-after-use]
                   [--repo-credential-username <User>] [--repo-credential-secret <Secret>] [--repo-credential-secret-file <Path>]
  --verbose, -Verbose              Enable verbose diagnostics
  --diagnostics                    Include logs in JSON output
  --quiet, -q                      Suppress non-essential output
  --no-color                       Disable ANSI colors
  --view auto|standard|ansi        Console rendering mode (default: auto)
  --output json                    Emit machine-readable JSON output      

Default config discovery (when --config is omitted):
  Searches for powerforge.json / powerforge.pipeline.json / .powerforge/pipeline.json
  in the current directory and parent directories.
");
}

static CliOptions ParseCliOptions(string[]? args, out string? error)
{
    error = null;
    if (args is null) return new CliOptions(verbose: false, quiet: false, diagnostics: false, noColor: false, view: ConsoleView.Auto);

    bool verbose = false;
    bool quiet = false;
    bool diagnostics = false;
    bool noColor = false;
    ConsoleView view = ConsoleView.Auto;
    bool viewExplicit = false;

    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
        {
            verbose = true;
            continue;
        }

        if (a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("-q", StringComparison.OrdinalIgnoreCase))
        {
            quiet = true;
            continue;
        }

        if (a.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) || a.Equals("--diag", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics = true;
            continue;
        }

        if (a.Equals("--no-color", StringComparison.OrdinalIgnoreCase) || a.Equals("--nocolor", StringComparison.OrdinalIgnoreCase))
        {
            noColor = true;
            continue;
        }

        if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
        {
            viewExplicit = true;
            if (++i >= args.Length)
            {
                error = "Missing value for --view. Expected: auto|standard|ansi.";
                return new CliOptions(verbose, quiet, diagnostics, noColor, view);
            }

            if (!TryParseConsoleView(args[i], out view))
            {
                error = $"Invalid value for --view: '{args[i]}'. Expected: auto|standard|ansi.";
                return new CliOptions(verbose, quiet, diagnostics, noColor, view);
            }

            continue;
        }
    }

    if (!viewExplicit)
    {
        var envView = Environment.GetEnvironmentVariable("POWERFORGE_VIEW");
        if (!string.IsNullOrWhiteSpace(envView) && TryParseConsoleView(envView, out var parsed))
        {
            view = parsed;
        }
    }

    return new CliOptions(verbose, quiet, diagnostics, noColor, view);
}

static string[] StripGlobalArgs(string[] args)
{
    if (args is null || args.Length == 0) return Array.Empty<string>();

    var list = new List<string>(args.Length);
    for (int i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (IsGlobalArg(a)) continue;

        if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
        {
            i++; // skip value
            continue;
        }

        list.Add(a);
    }
    return list.ToArray();
}

static bool IsGlobalArg(string arg)
{
    if (string.IsNullOrWhiteSpace(arg)) return false;

    return arg.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("-q", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--diag", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--no-color", StringComparison.OrdinalIgnoreCase) ||
           arg.Equals("--nocolor", StringComparison.OrdinalIgnoreCase);
}

static ILogger CreateTextLogger(CliOptions cli)
{
    ILogger baseLogger = cli.NoColor
        ? new ConsoleLogger { IsVerbose = cli.Verbose }
        : new SpectreConsoleLogger { IsVerbose = cli.Verbose };

    return cli.Quiet ? new QuietLogger(baseLogger) : baseLogger;
}

static (ILogger Logger, BufferingLogger? Buffer) CreateCommandLogger(bool outputJson, CliOptions cli, ILogger textLogger)
{
    if (!outputJson) return (textLogger, null);

    if (cli.Diagnostics)
    {
        var buffer = new BufferingLogger { IsVerbose = cli.Verbose };
        return (buffer, buffer);
    }

    return (new NullLogger { IsVerbose = cli.Verbose }, null);
}

static T RunWithStatus<T>(bool outputJson, CliOptions cli, string statusText, Func<T> action)
{
    if (action is null) throw new ArgumentNullException(nameof(action));

    if (outputJson || cli.Quiet || cli.NoColor)
        return action();

    var view = ResolveConsoleView(cli.View);
    if (view != ConsoleView.Standard)
        return action();

    if (ConsoleEnvironment.IsCI || !Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive)
        return action();

    T? result = default;
    Spectre.Console.AnsiConsole.Status().Start(statusText, _ => { result = action(); });
    return result!;
}

static ConsoleView ResolveConsoleView(ConsoleView requested)
{
    if (requested != ConsoleView.Auto) return requested;

    var interactive = Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive
        && !ConsoleEnvironment.IsCI;

    return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
}

static bool TryParseConsoleView(string? value, out ConsoleView view)
{
    view = ConsoleView.Auto;
    if (string.IsNullOrWhiteSpace(value)) return false;

    switch (value.Trim().ToLowerInvariant())
    {
        case "auto":
            view = ConsoleView.Auto;
            return true;
        case "standard":
        case "interactive":
            view = ConsoleView.Standard;
            return true;
        case "ansi":
        case "plain":
            view = ConsoleView.Ansi;
            return true;
        default:
            return false;
    }
}

static (string Name, string Source, string? Staging, string? Csproj, string Version, string Configuration, string[] Frameworks, string? Author, string? CompanyName, string? Description, string[] Tags, string? IconUri, string? ProjectUri)? ParseBuildArgs(string[] argv)
{
    string? name = null, source = null, staging = null, csproj = null, version = null;
    string configuration = "Release";
    var frameworks = new List<string>();
    string? author = null, companyName = null, description = null, iconUri = null, projectUri = null;
    var tags = new List<string>();

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--name": name = ++i < argv.Length ? argv[i] : null; break;
            case "--project-root":
            case "--source": source = ++i < argv.Length ? argv[i] : null; break;
            case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
            case "--csproj": csproj = ++i < argv.Length ? argv[i] : null; break;
            case "--version": version = ++i < argv.Length ? argv[i] : null; break;
            case "--configuration": configuration = ++i < argv.Length ? argv[i] : configuration; break;
            case "--config": i++; break; // handled before ParseBuildArgs
            case "--output": i++; break; // handled elsewhere
            case "--framework":
                if (++i < argv.Length)
                {
                    foreach (var f in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        frameworks.Add(f.Trim());
                }
                break;
            case "--author": author = ++i < argv.Length ? argv[i] : null; break;
            case "--company": companyName = ++i < argv.Length ? argv[i] : null; break;
            case "--description": description = ++i < argv.Length ? argv[i] : null; break;
            case "--tag": if (++i < argv.Length) tags.Add(argv[i]); break;
            case "--tags":
                if (++i < argv.Length)
                {
                    foreach (var t in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        tags.Add(t.Trim());
                }
                break;
            case "--icon-uri": iconUri = ++i < argv.Length ? argv[i] : null; break;
            case "--project-uri": projectUri = ++i < argv.Length ? argv[i] : null; break;
        }
    }

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(version))
        return null;

    var tfms = frameworks.Count > 0 ? frameworks.ToArray() : new[] { "net472", "net8.0" };
    return (name!, source!, staging, csproj, version!, configuration, tfms, author, companyName, description, tags.ToArray(), iconUri, projectUri);
}

static (string Name, string Version, string Staging, string[] Roots, PowerForge.InstallationStrategy Strategy, int Keep)? ParseInstallArgs(string[] argv)
{
    string? name = null, version = null, staging = null; var roots = new List<string>();
    var strategy = PowerForge.InstallationStrategy.Exact; int keep = 3;
    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        switch (a.ToLowerInvariant())
        {
            case "--name": name = ++i < argv.Length ? argv[i] : null; break;
            case "--version": version = ++i < argv.Length ? argv[i] : null; break;
            case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
            case "--root": if (++i < argv.Length) roots.Add(argv[i]); break;
            case "--strategy":
                var s = ++i < argv.Length ? argv[i] : null;
                if (string.Equals(s, "autorevision", StringComparison.OrdinalIgnoreCase)) strategy = PowerForge.InstallationStrategy.AutoRevision;
                else strategy = PowerForge.InstallationStrategy.Exact;
                break;
            case "--keep":
                if (++i < argv.Length && int.TryParse(argv[i], out var k)) keep = k;
                break;
            case "--config": i++; break; // handled before ParseInstallArgs
            case "--output": i++; break; // handled elsewhere
        }
    }
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(staging))
        return null;
    return (name!, version!, staging!, roots.ToArray(), strategy, keep);        
}

static ModuleTestSuiteSpec? ParseTestArgs(string[] argv)
{
    var spec = new ModuleTestSuiteSpec { ProjectPath = Directory.GetCurrentDirectory() };

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--project-root":
            case "--project":
            case "--path":
                if (++i >= argv.Length) return null;
                spec.ProjectPath = argv[i];
                break;
            case "--test-path":
                if (++i >= argv.Length) return null;
                spec.TestPath = argv[i];
                break;
            case "--format":
            case "--output-format":
                if (++i >= argv.Length) return null;
                if (!Enum.TryParse<ModuleTestSuiteOutputFormat>(argv[i], ignoreCase: true, out var fmt))
                    return null;
                spec.OutputFormat = fmt;
                break;
            case "--additional-modules":
            case "--modules":
                if (++i >= argv.Length) return null;
                spec.AdditionalModules = SplitCsv(argv[i]);
                break;
            case "--skip-modules":
                if (++i >= argv.Length) return null;
                spec.SkipModules = SplitCsv(argv[i]);
                break;
            case "--coverage":
            case "--enable-code-coverage":
                spec.EnableCodeCoverage = true;
                break;
            case "--force":
                spec.Force = true;
                break;
            case "--skip-dependencies":
                spec.SkipDependencies = true;
                break;
            case "--skip-import":
                spec.SkipImport = true;
                break;
            case "--keep-xml":
            case "--keep-results-xml":
                spec.KeepResultsXml = true;
                break;
            case "--timeout":
            case "--timeout-seconds":
                if (++i >= argv.Length) return null;
                if (!int.TryParse(argv[i], out var t)) return null;
                spec.TimeoutSeconds = t;
                break;
            case "--prefer-powershell":
            case "--prefer-windows-powershell":
                spec.PreferPwsh = false;
                break;
            case "--prefer-pwsh":
                spec.PreferPwsh = true;
                break;
            case "--config":
                i++;
                break;
            case "--output":
                i++;
                break;
            case "--output-json":
            case "--json":
                break;
        }
    }

    return spec;
}

static string[] SplitCsv(string value)
    => (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .ToArray();

static string? TryGetOptionValue(string[] argv, string optionName)
{
    for (int i = 0; i < argv.Length; i++)
    {
        if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
        return ++i < argv.Length ? argv[i] : null;
    }
    return null;
}

static string? TryGetProjectRoot(string[] argv)
    => TryGetOptionValue(argv, "--project-root")
       ?? TryGetOptionValue(argv, "--project")
       ?? TryGetOptionValue(argv, "--path");

static string ResolvePathFromBase(string baseDir, string path)
{
    var raw = (path ?? string.Empty).Trim().Trim('"');
    if (string.IsNullOrWhiteSpace(raw))
        throw new ArgumentException("Path is empty.", nameof(path));

    return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
}

static string? ResolvePathFromBaseNullable(string baseDir, string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    var raw = path.Trim().Trim('"');
    return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(baseDir, raw));
}

static void ResolveBuildSpecPaths(ModuleBuildSpec spec, string configFullPath)
{
    if (spec is null) throw new ArgumentNullException(nameof(spec));

    var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
    if (!string.IsNullOrWhiteSpace(spec.SourcePath))
        spec.SourcePath = ResolvePathFromBase(baseDir, spec.SourcePath);
    spec.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.StagingPath);
    spec.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.CsprojPath);
}

static void ResolveInstallSpecPaths(ModuleInstallSpec spec, string configFullPath)
{
    if (spec is null) throw new ArgumentNullException(nameof(spec));

    var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
    if (!string.IsNullOrWhiteSpace(spec.StagingPath))
        spec.StagingPath = ResolvePathFromBase(baseDir, spec.StagingPath);
}

static void ResolveTestSpecPaths(ModuleTestSuiteSpec spec, string configFullPath)
{
    if (spec is null) throw new ArgumentNullException(nameof(spec));

    var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
    if (!string.IsNullOrWhiteSpace(spec.ProjectPath))
        spec.ProjectPath = ResolvePathFromBase(baseDir, spec.ProjectPath);
    spec.TestPath = ResolvePathFromBaseNullable(baseDir, spec.TestPath);
}

static void ResolvePipelineSpecPaths(ModulePipelineSpec spec, string configFullPath)
{
    if (spec is null) throw new ArgumentNullException(nameof(spec));

    var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();
    if (spec.Build is null) return;

    if (!string.IsNullOrWhiteSpace(spec.Build.SourcePath))
        spec.Build.SourcePath = ResolvePathFromBase(baseDir, spec.Build.SourcePath);
    spec.Build.StagingPath = ResolvePathFromBaseNullable(baseDir, spec.Build.StagingPath);
    spec.Build.CsprojPath = ResolvePathFromBaseNullable(baseDir, spec.Build.CsprojPath);
}

static string? FindDefaultPipelineConfig(string baseDir)
{
    var candidates = new[]
    {
        "powerforge.json",
        "powerforge.pipeline.json",
        Path.Combine(".powerforge", "powerforge.json"),
        Path.Combine(".powerforge", "pipeline.json"),
    };

    foreach (var dir in EnumerateSelfAndParents(baseDir))
    {
        foreach (var rel in candidates)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (File.Exists(full)) return full;
            }
            catch { /* ignore */ }
        }
    }

    return null;
}

static string? FindDefaultBuildConfig(string baseDir)
{
    var candidates = new[]
    {
        "powerforge.build.json",
        Path.Combine(".powerforge", "build.json"),
    };

    foreach (var dir in EnumerateSelfAndParents(baseDir))
    {
        foreach (var rel in candidates)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (File.Exists(full)) return full;
            }
            catch { /* ignore */ }
        }
    }

    return FindDefaultPipelineConfig(baseDir);
}

static IEnumerable<string> EnumerateSelfAndParents(string? baseDir)
{
    string current;
    try
    {
        current = Path.GetFullPath(string.IsNullOrWhiteSpace(baseDir)
            ? Directory.GetCurrentDirectory()
            : baseDir.Trim().Trim('"'));
    }
    catch
    {
        current = Directory.GetCurrentDirectory();
    }

    while (true)
    {
        yield return current;

        try
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)) yield break;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) yield break;
            current = parent;
        }
        catch
        {
            yield break;
        }
    }
}

static bool LooksLikePipelineSpec(string fullConfigPath)
{
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(fullConfigPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;
        return root.TryGetProperty("Build", out _) || root.TryGetProperty("Segments", out _) || root.TryGetProperty("Install", out _);
    }
    catch
    {
        return false;
    }
}

static JsonSerializerOptions CreateJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    options.Converters.Add(new JsonStringEnumConverter());
    options.Converters.Add(new ConfigurationSegmentJsonConverter());
    return options;
}

static (T Value, string FullPath) LoadJsonWithPath<T>(string path)
{
    var full = Path.GetFullPath(path.Trim().Trim('"'));
    if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");

    var json = File.ReadAllText(full);
    var obj = JsonSerializer.Deserialize<T>(json, CreateJsonOptions());
    if (obj is null) throw new InvalidOperationException($"Failed to deserialize config file: {full}");
    return (obj, full);
}

static (string[] Names, string? Version, bool Prerelease, string[] Repositories)? ParseFindArgs(string[] argv)
{
    var names = new List<string>();
    var repos = new List<string>();
    string? version = null;
    bool prerelease = false;

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--name":
                if (++i < argv.Length)
                {
                    foreach (var n in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        names.Add(n.Trim());
                }
                break;
            case "--output":
                i++;
                break;
            case "--output-json":
            case "--json":
                break;
            case "--repo":
            case "--repository":
                if (++i < argv.Length) repos.Add(argv[i]);
                break;
            case "--version":
                version = ++i < argv.Length ? argv[i] : null;
                break;
            case "--prerelease":
                prerelease = true;
                break;
        }
    }

    if (names.Count == 0) return null;
    return (names.ToArray(), version, prerelease, repos.ToArray());
}

static RepositoryPublishRequest? ParsePublishArgs(string[] argv)
{
    string? path = null;
    string? repositoryName = null;
    string? apiKey = null;
    string? destination = null;
    bool isNupkg = false;
    bool skipDeps = false;
    bool skipManifest = false;

    PublishTool tool = PublishTool.Auto;

    string? repoUri = null;
    string? repoSourceUri = null;
    string? repoPublishUri = null;
    bool repoTrusted = true;
    bool repoTrustedProvided = false;
    int? repoPriority = null;
    RepositoryApiVersion repoApiVersion = RepositoryApiVersion.Auto;
    bool repoApiVersionProvided = false;
    bool ensureRepoRegistered = true;
    bool ensureRepoProvided = false;
    bool unregisterAfterUse = false;

    string? repoCredUser = null;
    string? repoCredSecret = null;
    string? repoCredSecretFile = null;

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--path":
                path = ++i < argv.Length ? argv[i] : null;
                break;
            case "--output":
                i++;
                break;
            case "--output-json":
            case "--json":
                break;
            case "--repo":
            case "--repository":
                repositoryName = ++i < argv.Length ? argv[i] : null;
                break;
            case "--tool":
                tool = ParsePublishTool(++i < argv.Length ? argv[i] : null);
                break;
            case "--apikey":
            case "--api-key":
                apiKey = ++i < argv.Length ? argv[i] : null;
                break;
            case "--destination":
            case "--destination-path":
                destination = ++i < argv.Length ? argv[i] : null;
                break;
            case "--nupkg":
                isNupkg = true;
                break;
            case "--skip-dependencies-check":
                skipDeps = true;
                break;
            case "--skip-manifest-validate":
            case "--skip-module-manifest-validate":
                skipManifest = true;
                break;

            case "--repo-uri":
                repoUri = ++i < argv.Length ? argv[i] : null;
                break;
            case "--repo-source-uri":
                repoSourceUri = ++i < argv.Length ? argv[i] : null;
                break;
            case "--repo-publish-uri":
                repoPublishUri = ++i < argv.Length ? argv[i] : null;
                break;
            case "--repo-trusted":
                repoTrusted = true;
                repoTrustedProvided = true;
                break;
            case "--repo-untrusted":
                repoTrusted = false;
                repoTrustedProvided = true;
                break;
            case "--repo-priority":
                if (++i < argv.Length && int.TryParse(argv[i], out var p)) repoPriority = p;
                break;
            case "--repo-api-version":
                repoApiVersion = ParseRepositoryApiVersion(++i < argv.Length ? argv[i] : null);
                repoApiVersionProvided = true;
                break;
            case "--repo-ensure":
                ensureRepoRegistered = true;
                ensureRepoProvided = true;
                break;
            case "--no-repo-ensure":
                ensureRepoRegistered = false;
                ensureRepoProvided = true;
                break;
            case "--repo-unregister-after-use":
                unregisterAfterUse = true;
                break;

            case "--repo-credential-username":
                repoCredUser = ++i < argv.Length ? argv[i] : null;
                break;
            case "--repo-credential-secret":
                repoCredSecret = ++i < argv.Length ? argv[i] : null;
                break;
            case "--repo-credential-secret-file":
                repoCredSecretFile = ++i < argv.Length ? argv[i] : null;
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(path)) return null;

    string? repositorySecret = null;
    if (!string.IsNullOrWhiteSpace(repoCredSecretFile))
    {
        var full = Path.GetFullPath(repoCredSecretFile!.Trim().Trim('"'));
        repositorySecret = File.ReadAllText(full).Trim();
    }
    else if (!string.IsNullOrWhiteSpace(repoCredSecret))
    {
        repositorySecret = repoCredSecret.Trim();
    }

    var anyRepositoryUriProvided =
        !string.IsNullOrWhiteSpace(repoUri) ||
        !string.IsNullOrWhiteSpace(repoSourceUri) ||
        !string.IsNullOrWhiteSpace(repoPublishUri);

    if (anyRepositoryUriProvided)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
            throw new InvalidOperationException("Repository name is required when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
        if (string.Equals(repositoryName.Trim(), "PSGallery", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Repository name cannot be 'PSGallery' when --repo-uri/--repo-source-uri/--repo-publish-uri is provided.");
    }

    PublishRepositoryConfiguration? repoConfig = null;
    var hasRepoCred = !string.IsNullOrWhiteSpace(repoCredUser) && !string.IsNullOrWhiteSpace(repositorySecret);
    var hasRepoOptions =
        anyRepositoryUriProvided ||
        hasRepoCred ||
        repoPriority.HasValue ||
        repoApiVersionProvided ||
        repoTrustedProvided ||
        ensureRepoProvided ||
        unregisterAfterUse;

    if (hasRepoOptions)
    {
        repoConfig = new PublishRepositoryConfiguration
        {
            Name = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
            Uri = string.IsNullOrWhiteSpace(repoUri) ? null : repoUri.Trim(),
            SourceUri = string.IsNullOrWhiteSpace(repoSourceUri) ? null : repoSourceUri.Trim(),
            PublishUri = string.IsNullOrWhiteSpace(repoPublishUri) ? null : repoPublishUri.Trim(),
            Trusted = repoTrusted,
            Priority = repoPriority,
            ApiVersion = repoApiVersion,
            EnsureRegistered = ensureRepoRegistered,
            UnregisterAfterUse = unregisterAfterUse,
            Credential = hasRepoCred
                ? new RepositoryCredential { UserName = repoCredUser!.Trim(), Secret = repositorySecret }
                : null
        };
    }

    return new RepositoryPublishRequest
    {
        Path = path!,
        IsNupkg = isNupkg,
        RepositoryName = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName.Trim(),
        Tool = tool,
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
        Repository = repoConfig,
        DestinationPath = string.IsNullOrWhiteSpace(destination) ? null : destination.Trim(),
        SkipDependenciesCheck = skipDeps,
        SkipModuleManifestValidate = skipManifest
    };
}

static PublishTool ParsePublishTool(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return PublishTool.Auto;

    var v = value.Trim();
    if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return PublishTool.Auto;
    if (v.Equals("psresourceget", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("psresource", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("psrg", StringComparison.OrdinalIgnoreCase))
        return PublishTool.PSResourceGet;
    if (v.Equals("powershellget", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("psget", StringComparison.OrdinalIgnoreCase))
        return PublishTool.PowerShellGet;

    throw new InvalidOperationException($"Invalid value for --tool: '{value}'. Expected: auto|psresourceget|powershellget.");
}

static RepositoryApiVersion ParseRepositoryApiVersion(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return RepositoryApiVersion.Auto;

    var v = value.Trim();
    if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return RepositoryApiVersion.Auto;
    if (v.Equals("v2", StringComparison.OrdinalIgnoreCase) || v.Equals("2", StringComparison.OrdinalIgnoreCase))
        return RepositoryApiVersion.V2;
    if (v.Equals("v3", StringComparison.OrdinalIgnoreCase) || v.Equals("3", StringComparison.OrdinalIgnoreCase))
        return RepositoryApiVersion.V3;

    throw new InvalidOperationException($"Invalid value for --repo-api-version: '{value}'. Expected: auto|v2|v3.");
}

static bool IsJsonOutput(string[] argv)
{
    foreach (var a in argv)
    {
        if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            return true;
    }

    var output = TryGetOptionValue(argv, "--output");
    return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
}

static string[] ParseTargets(string[] argv)
{
    var list = new List<string>();
    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        if (a.Equals("--output", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            continue;
        }

        if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            continue;

        list.Add(a);
    }

    return list.ToArray();
}

static void WriteJson(object obj)
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    options.Converters.Add(new JsonStringEnumConverter());
    options.Converters.Add(new ConfigurationSegmentJsonConverter());

    var json = JsonSerializer.Serialize(obj, options);
    Console.WriteLine(json);
}

sealed class LogEntry
{
    public string Level { get; }
    public string Message { get; }

    public LogEntry(string level, string message)
    {
        Level = level;
        Message = message;
    }
}

sealed class BufferingLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public List<LogEntry> Entries { get; } = new();

    public void Info(string message) => Entries.Add(new LogEntry("info", message));
    public void Success(string message) => Entries.Add(new LogEntry("success", message));
    public void Warn(string message) => Entries.Add(new LogEntry("warn", message));
    public void Error(string message) => Entries.Add(new LogEntry("error", message));
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new LogEntry("verbose", message));
    }
}

sealed class CliOptions
{
    public bool Verbose { get; }
    public bool Quiet { get; }
    public bool Diagnostics { get; }
    public bool NoColor { get; }
    public ConsoleView View { get; }

    public CliOptions(bool verbose, bool quiet, bool diagnostics, bool noColor, ConsoleView view)
    {
        Verbose = verbose;
        Quiet = quiet;
        Diagnostics = diagnostics;
        NoColor = noColor;
        View = view;
    }
}

sealed class QuietLogger : ILogger
{
    private readonly ILogger _inner;

    public QuietLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsVerbose => _inner.IsVerbose;

    public void Info(string message) { }
    public void Success(string message) { }
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
    public void Verbose(string message) { }
}
