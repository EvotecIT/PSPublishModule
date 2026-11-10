using System;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleUi;

internal static partial class PowerForgeReleaseConsoleUi
{
    public static void WriteSummary(PowerForgeReleaseResult result, TimeSpan duration)
        => WriteSummary(AnsiConsole.Console, result, duration);

    internal static void WriteSummary(IAnsiConsole console, PowerForgeReleaseResult result, TimeSpan duration)
    {
        if (console is null) throw new ArgumentNullException(nameof(console));
        if (result is null) return;
        static string Esc(string? value) => Markup.Escape(value ?? string.Empty);

        var unicode = ConsoleEncoding.ShouldRenderUnicode(console.Profile.Capabilities.Unicode);
        console.Profile.Capabilities.Unicode = unicode;
        var emoji = ConsoleEncoding.ShouldRenderEmoji(console.Profile.Capabilities.Unicode);
        var icon = result.Success ? (emoji ? "✅" : "+") : (emoji ? "❌" : "x");
        var color = result.Success ? "green" : "red";
        console.Write(new Rule($"[{color}]{icon} Unified release summary[/]").LeftJustified());

        var packageVersion = result.Packages?.Result.Release?.ResolvedVersion;
        var moduleVersion = result.ModulePlan?.ModuleVersion;
        var toolSteps = result.ToolPlan?.Targets.Sum(target => target.Combinations.Length)
            ?? result.DotNetToolPlan?.Steps.Length
            ?? 0;
        var table = new Table()
            .Border(unicode ? TableBorder.Rounded : TableBorder.Simple)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));
        table.AddRow("Status", result.Success ? "[green]Succeeded[/]" : "[red]Failed[/]");
        if (!string.IsNullOrWhiteSpace(packageVersion)) table.AddRow("Package build version", Esc(packageVersion));
        if (!string.IsNullOrWhiteSpace(moduleVersion)) table.AddRow("Module version", Esc(moduleVersion));
        if (toolSteps > 0) table.AddRow("Tool output steps", toolSteps.ToString());
        table.AddRow("Release assets", result.ReleaseAssets.Length.ToString());
        var gitHubReleaseUrl = result.UnifiedGitHubRelease?.ReleaseUrl;
        if (!string.IsNullOrWhiteSpace(gitHubReleaseUrl))
        {
            table.AddRow("GitHub release", Esc(gitHubReleaseUrl));
            table.AddRow(
                "GitHub action",
                result.UnifiedGitHubRelease!.ReusedExistingRelease
                    ? "[yellow]Reused existing release[/]"
                    : "[green]Created new release[/]");
            table.AddRow(
                "GitHub assets",
                Esc(
                    $"{result.UnifiedGitHubRelease.UploadedAssets.Length} uploaded, " +
                    $"{result.UnifiedGitHubRelease.SkippedExistingAssets.Length} skipped, " +
                    $"{result.UnifiedGitHubRelease.ReplacedExistingAssets.Length} replaced"));
        }
        if (result.VirusTotalMonitor is { } virusTotal)
        {
            var monitorSummary = !virusTotal.Success
                ? $"[red]Failed: {Esc(virusTotal.ErrorMessage ?? "Monitor registration failed")}[/]"
                : virusTotal.Artifacts.Length == 0
                    ? "[yellow]Skipped: no configured final release artifacts matched[/]"
                    : $"[green]Registered {virusTotal.Artifacts.Length} artifact(s); analysis remains asynchronous[/]";
            table.AddRow("VirusTotal Monitor", monitorSummary);
            if (!string.IsNullOrWhiteSpace(result.VirusTotalMonitorReceiptPath))
                table.AddRow("VirusTotal receipt", Esc(result.VirusTotalMonitorReceiptPath));
        }
        table.AddRow("Duration", Esc(new BufferedLogSupportService().FormatDuration(duration)));
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            table.AddRow("Error", $"[red]{Esc(result.ErrorMessage)}[/]");
        console.Write(table);
    }

}
