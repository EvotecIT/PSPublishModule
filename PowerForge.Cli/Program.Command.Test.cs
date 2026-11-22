using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandTest(string[] filteredArgs, CliOptions cli, ILogger logger)
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
        // Pester output is shown as it runs (above the status line); JSON mode keeps stdout for the envelope.
        Action<string>? showLine = outputJson
            ? null
            : line => PowerForge.ConsoleUi.ConsoleOutput.WriteOutside(() => PowerForge.ConsoleUi.ConsoleOutput.WriteLine(line));
        var res = RunWithStatus(outputJson, cli, "Running test suite", () => service.Run(spec, showLine), value => value.FailedCount == 0 && value.ExitCode == 0);

        var success = res.FailedCount == 0 && res.ExitCode == 0;
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

        // A passing run already reported its stderr as warnings while streaming.
        if (!success && !string.IsNullOrWhiteSpace(res.StdErr))
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
}
