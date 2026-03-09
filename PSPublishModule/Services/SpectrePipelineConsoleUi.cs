using PowerForge;
using PowerForge.ConsoleShared;

namespace PSPublishModule;

internal static partial class SpectrePipelineConsoleUi
{
    public static bool ShouldUseInteractiveView(bool isVerbose)
        => SpectreModulePipelineConsoleUi.ShouldUseInteractiveView(
            isVerbose: isVerbose,
            outputJson: false,
            quiet: false,
            noColor: false,
            view: ConsoleView.Standard);

    public static ModulePipelineResult RunInteractive(
        ModulePipelineRunner runner,
        ModulePipelineSpec spec,
        ModulePipelinePlan plan,
        string? configLabel)
        => SpectreModulePipelineConsoleUi.RunInteractive(runner, spec, plan, configLabel);
}
