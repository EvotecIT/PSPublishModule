using PowerForge;
using PowerForge.ConsoleUi;

namespace PowerForge.Cli;

internal static class DotNetPublishConsoleUi
{
    public static bool ShouldUseInteractiveView(bool outputJson, CliOptions cli)
        => ModulePipelineConsoleUi.ShouldUseInteractiveView(
            cli.Verbose, outputJson, cli.Quiet, cli.NoColor, cli.View);

    public static DotNetPublishResult Run(
        DotNetPublishPipelineRunner runner,
        DotNetPublishPlan plan,
        string? configPath,
        bool outputJson,
        CliOptions cli)
        => ShouldUseInteractiveView(outputJson, cli)
            ? ExecutablePublishConsoleUi.RunInteractive(plan, configPath, progress => runner.Run(plan, progress))
            : runner.Run(plan, progress: null);
}
