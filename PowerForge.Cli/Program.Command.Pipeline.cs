using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandPipeline(string[] filteredArgs, CliOptions cli, ILogger logger)
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
        spec.Diagnostics ??= new ModulePipelineDiagnosticsOptions();
        var diagnosticsBaselinePath = TryGetOptionValue(argv, "--diagnostics-baseline");
        if (!string.IsNullOrWhiteSpace(diagnosticsBaselinePath))
            spec.Diagnostics.BaselinePath = ResolvePathFromBase(Path.GetDirectoryName(loaded.FullPath) ?? Directory.GetCurrentDirectory(), diagnosticsBaselinePath);
        spec.Diagnostics.GenerateBaseline = spec.Diagnostics.GenerateBaseline || argv.Any(a => a.Equals("--diagnostics-baseline-generate", StringComparison.OrdinalIgnoreCase));
        spec.Diagnostics.UpdateBaseline = spec.Diagnostics.UpdateBaseline || argv.Any(a => a.Equals("--diagnostics-baseline-update", StringComparison.OrdinalIgnoreCase));
        spec.Diagnostics.FailOnNewDiagnostics = spec.Diagnostics.FailOnNewDiagnostics || argv.Any(a => a.Equals("--fail-on-new-diagnostics", StringComparison.OrdinalIgnoreCase));
        var failOnSeverity = TryGetOptionValue(argv, "--fail-on-diagnostics-severity");
        if (!string.IsNullOrWhiteSpace(failOnSeverity))
        {
            if (!Enum.TryParse<BuildDiagnosticSeverity>(failOnSeverity, ignoreCase: true, out var severity) ||
                severity == BuildDiagnosticSeverity.Info)
            {
                throw new InvalidOperationException("Invalid value for --fail-on-diagnostics-severity. Expected Warning or Error.");
            }

            spec.Diagnostics.FailOnSeverity = severity;
        }
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

    ModulePipelinePlan? lastPlan = null;
    bool usedInteractiveView = false;
    try
    {
        var interactive = PipelineConsoleUi.ShouldUseInteractiveView(outputJson, cli);
        usedInteractiveView = interactive;
        var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
        var runner = new ModulePipelineRunner(cmdLogger);
        var plan = runner.Plan(spec);
        lastPlan = plan;
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
        var policyFailure = ex as ModulePipelineDiagnosticsPolicyException;
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "pipeline",
                Success = false,
                ExitCode = 1,
                Error = ex.Message,
                Spec = CliJson.SerializeToElement(spec, CliJson.Context.ModulePipelineSpec),
                Result = policyFailure is null ? null : CliJson.SerializeToElement(policyFailure.Result, CliJson.Context.ModulePipelineResult)
            });
            return 1;
        }

        if (policyFailure is not null)
        {
            WritePipelineSummary(policyFailure.Result, cli, logger);
            logger.Error(policyFailure.Message);
        }
        else if (usedInteractiveView && lastPlan is not null)
        {
            try { PipelineConsoleUi.WriteFailureSummary(lastPlan, ex); } catch { }
        }
        else
        {
            logger.Error(ex.Message);
        }
        return 1;
    }
    }
}
