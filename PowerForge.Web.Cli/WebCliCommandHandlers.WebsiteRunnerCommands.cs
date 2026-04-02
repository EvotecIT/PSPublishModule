using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleWebsiteRunner(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var websiteRoot = TryGetOptionValue(subArgs, "--website-root") ??
                          TryGetOptionValue(subArgs, "--websiteRoot") ??
                          TryGetOptionValue(subArgs, "--root");
        var pipelineConfig = TryGetOptionValue(subArgs, "--pipeline-config") ??
                             TryGetOptionValue(subArgs, "--pipelineConfig") ??
                             TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(websiteRoot) || string.IsNullOrWhiteSpace(pipelineConfig))
            return Fail("website-runner requires --website-root and --pipeline-config.", outputJson, logger, "web.website-runner");

        try
        {
            var result = WebWebsiteRunner.Run(
                new WebWebsiteRunnerOptions
                {
                    WebsiteRoot = websiteRoot,
                    PipelineConfig = pipelineConfig,
                    EngineMode = TryGetOptionValue(subArgs, "--engine-mode") ?? TryGetOptionValue(subArgs, "--engineMode") ?? string.Empty,
                    PipelineMode = TryGetOptionValue(subArgs, "--pipeline-mode") ?? TryGetOptionValue(subArgs, "--pipelineMode") ?? "ci",
                    PowerForgeLockPath = TryGetOptionValue(subArgs, "--powerforge-lock-path") ?? TryGetOptionValue(subArgs, "--powerforgeLockPath"),
                    PowerForgeRepository = TryGetOptionValue(subArgs, "--powerforge-repository") ?? TryGetOptionValue(subArgs, "--powerforgeRepository"),
                    PowerForgeRef = TryGetOptionValue(subArgs, "--powerforge-ref") ?? TryGetOptionValue(subArgs, "--powerforgeRef"),
                    PowerForgeRepositoryOverride = TryGetOptionValue(subArgs, "--powerforge-repository-override") ?? TryGetOptionValue(subArgs, "--powerforgeRepositoryOverride"),
                    PowerForgeRefOverride = TryGetOptionValue(subArgs, "--powerforge-ref-override") ?? TryGetOptionValue(subArgs, "--powerforgeRefOverride"),
                    PowerForgeToolLockPath = TryGetOptionValue(subArgs, "--powerforge-tool-lock-path") ?? TryGetOptionValue(subArgs, "--powerforgeToolLockPath"),
                    PowerForgeWebTag = TryGetOptionValue(subArgs, "--powerforge-web-tag") ?? TryGetOptionValue(subArgs, "--powerforgeWebTag"),
                    GitHubToken = Environment.GetEnvironmentVariable("POWERFORGE_REPOSITORY_TOKEN") ??
                                  Environment.GetEnvironmentVariable("GH_TOKEN") ??
                                  Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
                    RunnerTempPath = Environment.GetEnvironmentVariable("RUNNER_TEMP"),
                    MaintenanceModeNote = HasOption(subArgs, "--maintenance-note") || HasOption(subArgs, "--maintenanceModeNote")
                },
                outputJson ? null : Console.WriteLine,
                outputJson ? null : Console.Error.WriteLine);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.website-runner",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebWebsiteRunnerResult)
                });
            }
            else
            {
                logger.Success($"Website runner ({result.EngineMode}) completed.");
                logger.Info($"Launched: {result.LaunchedPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.website-runner",
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
