using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static int CommandDocs(string[] filteredArgs, CliOptions cli, ILogger logger)
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
}

