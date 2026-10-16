using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int WriteAppleDeployResult(
        AppleLocalDeploymentCliResult result,
        string configPath,
        bool outputJson,
        ILogger logger)
    {
        var exitCode = result.Success ? 0 : 1;
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "apple-deploy",
                Success = result.Success,
                ExitCode = exitCode,
                Config = "release",
                ConfigPath = configPath,
                Result = CliJson.SerializeToElement(result, CliJson.Context.AppleLocalDeploymentCliResult)
            });
            return exitCode;
        }

        logger.Info($"Target: {result.Target} ({result.Platform}, {result.Configuration})");
        if (result.OptimizeSwift)
            logger.Info("Swift optimization: -O (configuration retained)");
        if (!string.IsNullOrWhiteSpace(result.Profile))
            logger.Info($"Profile: {result.Profile}");
        if (!string.IsNullOrWhiteSpace(result.SourceRevision))
            logger.Info($"Source: {result.SourceRevision}" +
                (result.SourceDirty == true ? " (working tree has uncommitted changes)" : string.Empty));
        if (result.Planned)
        {
            logger.Success("Apple local deployment plan is valid.");
            return 0;
        }
        var appPath = result.InstalledAppPath ?? result.AppPath;
        if (!string.IsNullOrWhiteSpace(appPath))
            logger.Info($"App: {appPath}");
        if (!string.IsNullOrWhiteSpace(result.DeviceIdentifier))
            logger.Info($"Device: {result.DeviceIdentifier}");
        foreach (var stage in result.Stages)
            logger.Info($"{stage.Name}: {stage.DurationSeconds:F1}s (exit {stage.ExitCode})");
        logger.Info($"Total deployment time: {result.TotalDurationSeconds:F1}s");
        if (!string.IsNullOrWhiteSpace(result.Warning))
            logger.Warn(result.Warning);
        if (result.Success)
            logger.Success(result.Launch ? "Apple app installed and launched." : "Apple app installed.");
        else if (result.DeviceLocked && result.InstallSucceeded == true)
            logger.Warn("Apple app installed, but launch was deferred because the device is locked.");
        else
            logger.Error("Apple local deployment failed.");
        if (!string.IsNullOrWhiteSpace(result.Diagnostic))
        {
            if (result.DeviceLocked)
                logger.Warn(result.Diagnostic);
            else
                logger.Error(result.Diagnostic);
        }
        return exitCode;
    }

    private static AppleLocalDeploymentCliStage[] CollectAppleDeployStages(
        ProcessRunResult build, ProcessRunResult? install, ProcessRunResult? launch)
        => new[] { ("Build", build), ("Install", install), ("Launch", launch) }
            .Where(static entry => entry.Item2 is not null)
            .Select(static entry => new AppleLocalDeploymentCliStage
            {
                Name = entry.Item1,
                DurationSeconds = entry.Item2!.Duration.TotalSeconds,
                ExitCode = entry.Item2.ExitCode,
                Succeeded = entry.Item2.Succeeded,
                TimedOut = entry.Item2.TimedOut,
                StartFailed = entry.Item2.StartFailed
            }).ToArray();

    private static string? ResolveAppleDeployDiagnostic(
        IEnumerable<string> sensitiveValues,
        params ProcessRunResult?[] stages)
    {
        var failed = stages.FirstOrDefault(static stage => stage is not null && !stage.Succeeded);
        if (failed is null)
            return null;
        return AppleToolFailureFormatter.Format(failed, sensitiveValues);
    }

}
