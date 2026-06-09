using System;
using System.Linq;
using PowerForge;
using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PSPublishModule;

internal static partial class SpectrePipelineConsoleUi
{
    public static void WriteSummary(ModulePipelineResult res)
    {
        SpectrePipelineSummaryWriter.WriteSummary(res);
    }

    public static void WriteFailureSummary(ModulePipelinePlan plan, Exception error)
        => SpectrePipelineSummaryWriter.WriteFailureSummary(plan, error);
}
