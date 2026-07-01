using System.IO;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void TryWriteTypeAcceleratorSurfaceReport(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var mode = AssemblyTypeAcceleratorOptions.ResolveMode(
            plan.BuildSpec.AssemblyTypeAcceleratorMode,
            plan.BuildSpec.AssemblyTypeAccelerators,
            plan.BuildSpec.AssemblyTypeAcceleratorAssemblies);
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return;

        try
        {
            var reportPath = BuildArtefactsReportPath(
                plan.ProjectRoot,
                reportFileName: null,
                fallbackFileName: Path.Combine("Reports", "TypeAccelerators.Core.txt"));
            var reporter = new ModuleTypeAcceleratorSurfaceReporter(_logger);
            var report = reporter.WriteReport(plan, buildResult, reportPath);
            if (report is null)
                return;

            state.TypeAcceleratorSurfaceReport = report;
            _logger.Info(
                $"ALC type accelerator surface: {report.Mode} mode, {report.TotalRegisteredTypeCount} registered name(s), " +
                $"{report.SkippedNonEnumTypeCount} public non-enum type(s) skipped. Report: {report.ReportPath}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to write ALC type accelerator surface report. {ex.Message}");
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
        }
    }
}
