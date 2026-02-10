using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static int CommandBuild(string[] filteredArgs, CliOptions cli, ILogger logger)
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

                var (buildSpec, buildSpecPath) = LoadBuildSpecWithPath(fullConfigPath);
                ResolveBuildSpecPaths(buildSpec, buildSpecPath);

                var buildPipeline = new ModuleBuildPipeline(cmdLogger);
                var buildRes = RunWithStatus(outputJson, cli, $"Building {buildSpec.Name} {buildSpec.Version}", () => buildPipeline.BuildToStaging(buildSpec));

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "build",
                        Success = true,
                        ExitCode = 0,
                        Config = "build",
                        ConfigPath = buildSpecPath,
                        Spec = CliJson.SerializeToElement(buildSpec, CliJson.Context.ModuleBuildSpec),
                        Result = CliJson.SerializeToElement(buildRes, CliJson.Context.ModuleBuildResult),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                logger.Success($"Built staging for {buildSpec.Name} {buildSpec.Version} at {buildRes.StagingPath}");
                return 0;
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
}

