using PowerForge;
using PowerForge.ConsoleUi;

namespace PowerForge.Cli;

internal static class PipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(bool outputJson, CliOptions cli)
        => ModulePipelineConsoleUi.ShouldUseInteractiveView(
            isVerbose: cli.Verbose,
            outputJson: outputJson,
            quiet: cli.Quiet,
            noColor: cli.NoColor,
            view: cli.View);

    public static ModulePipelineResult Run(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configPath,
        bool outputJson,
        CliOptions cli)
    {
        if (!ShouldUseInteractiveView(outputJson, cli))
            return runner.Run(spec, plan, progress: null);

        return ModulePipelineConsoleUi.RunInteractive(
            runner,
            spec,
            plan,
            string.IsNullOrWhiteSpace(configPath) ? "(discovered)" : configPath);
    }

    public static void WriteFailureSummary(ModulePipelinePlan plan, Exception error)
        => ModulePipelineSummary.WriteFailure(plan, error);
}
