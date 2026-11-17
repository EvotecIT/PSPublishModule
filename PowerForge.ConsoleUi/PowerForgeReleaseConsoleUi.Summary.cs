using System;
using System.Linq;
using ConsoleMonster.Frames;
using ConsoleMonster.Text;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal static partial class PowerForgeReleaseConsoleUi
{
    /// <summary>The unified release summary, in the same layout as the module and project build summaries.</summary>
    public static void WriteSummary(PowerForgeReleaseResult result, TimeSpan duration)
    {
        if (result is null) return;

        var summary = new SummaryFrame("Unified release", result.Success)
            .Fact(new BufferedLogSupportService().FormatDuration(duration));
        var packageVersion = result.Packages?.Result.Release?.ResolvedVersion;
        var moduleVersion = result.ModulePlan?.ModuleVersion;
        var toolSteps = result.ToolPlan?.Targets.Sum(target => target.Combinations.Length)
            ?? result.DotNetToolPlan?.Steps.Length
            ?? 0;

        if (!string.IsNullOrWhiteSpace(packageVersion)) summary.Line("Package build version", packageVersion);
        if (!string.IsNullOrWhiteSpace(moduleVersion)) summary.Line("Module version", moduleVersion);
        if (toolSteps > 0) summary.Line("Tool output steps", toolSteps.ToString());
        summary.Line("Release assets", result.ReleaseAssets.Length.ToString());
        summary.Meter(Meter.Count("Release assets", result.ReleaseAssets.Length));

        if (result.UnifiedGitHubRelease is { } github && !string.IsNullOrWhiteSpace(github.ReleaseUrl))
        {
            summary.Line("GitHub release", github.ReleaseUrl);
            summary.LineMarkup("GitHub action", github.ReusedExistingRelease ? "[warn]Reused existing release[/]" : "[pass]Created new release[/]");
            summary.Line("GitHub assets",
                $"{github.UploadedAssets.Length} uploaded, {github.SkippedExistingAssets.Length} skipped, {github.ReplacedExistingAssets.Length} replaced");
            summary.Meter(Meter.Count("Uploaded", github.UploadedAssets.Length));
        }

        if (result.VirusTotalMonitor is { } virusTotal)
        {
            if (!virusTotal.Success)
            {
                var message = virusTotal.ErrorMessage ?? "Monitor registration failed";
                summary.LineMarkup("VirusTotal Monitor", "[fail]Failed: " + Markup.Escape(message) + "[/]");
                summary.Attention.Add(HealthState.Failing, message, "VirusTotal Monitor");
            }
            else if (virusTotal.Artifacts.Length == 0)
            {
                summary.LineMarkup("VirusTotal Monitor", "[warn]Skipped: no configured final release artifacts matched[/]");
                summary.Attention.Add(HealthState.Warning, "Skipped: no configured final release artifacts matched", "VirusTotal Monitor");
            }
            else
            {
                summary.LineMarkup("VirusTotal Monitor", $"[pass]Registered {virusTotal.Artifacts.Length} artifact(s); analysis remains asynchronous[/]");
            }

            if (!string.IsNullOrWhiteSpace(result.VirusTotalMonitorReceiptPath))
                summary.Line("VirusTotal receipt", result.VirusTotalMonitorReceiptPath);
        }

        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            summary.Attention.Add(HealthState.Failing, result.ErrorMessage!, "Release");

        summary.Write();
    }
}
