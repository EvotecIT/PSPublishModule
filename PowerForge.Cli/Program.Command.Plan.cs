using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandPlan(string[] filteredArgs, CliOptions cli, ILogger logger)
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
            logger.Info($"Docs: {plan.Documentation?.Path}");
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
}
