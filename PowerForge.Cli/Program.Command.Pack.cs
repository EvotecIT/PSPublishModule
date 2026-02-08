using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static int CommandPack(string[] filteredArgs, CliOptions cli, ILogger logger)
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
                if (!string.IsNullOrWhiteSpace(a.OutputPath))
                    logger.Info($" -> {a.OutputPath}");
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
}

