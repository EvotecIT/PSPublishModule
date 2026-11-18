using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConsoleMonster.Frames;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;
using PowerForge.ConsoleUi;

namespace PSPublishModule;

public sealed partial class InvokeModuleTestSuiteCommand
{
    private const int MaxListedFailures = 10;

    private void WriteHeader(string projectRoot, string psVersion, string psEdition, ModuleInformation module)
    {
        var info = new KeyValueList();
        info.Add("Module", Markup.Escape($"{module.ModuleName} {module.ModuleVersion}".Trim()));
        info.Add("Project", Markup.Escape(projectRoot));
        info.Add("Manifest", Markup.Escape(module.ManifestPath));
        info.Add("PowerShell", Markup.Escape($"{psVersion} {psEdition}".Trim()));
        info.Add("Test host", WindowsPowerShell.IsPresent ? "Windows PowerShell (out of process)" : "PowerShell (out of process)");
        info.Add("Required modules", (module.RequiredModules?.Length ?? 0).ToString(CultureInfo.InvariantCulture));

        var title = CICD.IsPresent ? "CI module test suite" : "Module test suite";
        ConsoleOutput.Write(new Rows(new Rule($"[bold accent]{Markup.Escape(title)}[/]"), info, new TextBlock(string.Empty)));
    }

    private static void WriteSummary(ModuleTestSuiteResult result, bool success, bool includeDependencies)
    {
        var title = $"Module tests - {result.ModuleName} {result.ModuleVersion}".Trim();
        var outcome = success ? "passed" : result.FailedCount > 0 ? $"{result.FailedCount} failed" : "failed";
        var summary = new SummaryFrame(title, success, outcome);

        if (result.TotalCount > 0)
            summary.Meter(Meter.Composition("Tests passed", result.PassedCount, result.SkippedCount, result.FailedCount));

        summary.Line("Tests", $"{result.PassedCount}/{result.TotalCount} passed, {result.FailedCount} failed, {result.SkippedCount} skipped");
        if (result.Duration.HasValue) summary.Line("Duration", DotNetRepositoryReleaseService.FormatDuration(result.Duration.Value));
        if (!string.IsNullOrWhiteSpace(result.PesterVersion)) summary.Line("Pester", result.PesterVersion);
        if (result.CoveragePercent.HasValue) summary.Line("Coverage", result.CoveragePercent.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
        if (!success && result.FailedCount == 0) summary.Line("Exit code", result.ExitCode.ToString(CultureInfo.InvariantCulture));

        var failures = result.FailureAnalysis?.FailedTests ?? Array.Empty<ModuleTestFailureInfo>();
        foreach (var failure in failures.Take(MaxListedFailures))
            summary.Attention.Add(HealthState.Failing, FirstLine(failure.ErrorMessage) ?? "Failed", failure.Name);
        if (failures.Length > MaxListedFailures)
            summary.Attention.Add(HealthState.Failing, $"{failures.Length - MaxListedFailures} more failed test(s)", "Tests");

        var dependencies = includeDependencies ? result.DependencyResults ?? Array.Empty<ModuleDependencyInstallResult>() : Array.Empty<ModuleDependencyInstallResult>();
        foreach (var dependency in dependencies.Where(static dependency => dependency.Status == ModuleDependencyInstallStatus.Failed))
            summary.Attention.Add(HealthState.Failing, dependency.Message ?? "Install failed", dependency.Name);
        if (dependencies.Length > 0)
            summary.Detail("Dependencies", DependencyTable(dependencies));

        summary.Write();
    }

    private static Table DependencyTable(IEnumerable<ModuleDependencyInstallResult> dependencies)
    {
        var table = new Table().AddColumn("Module").AddColumn("Status").AddColumn("Version").AddColumn("Details");
        foreach (var dependency in dependencies)
        {
            var (role, version) = dependency.Status switch
            {
                ModuleDependencyInstallStatus.Failed => ("fail", dependency.RequestedVersion),
                ModuleDependencyInstallStatus.Skipped => ("muted", dependency.RequestedVersion),
                ModuleDependencyInstallStatus.Satisfied => ("pass", dependency.InstalledVersion),
                _ => ("pass", dependency.ResolvedVersion)
            };
            var details = dependency.Status is ModuleDependencyInstallStatus.Installed or ModuleDependencyInstallStatus.Updated
                ? $"via {dependency.Installer ?? "installer"}"
                : dependency.Message;
            table.AddRow(
                Markup.Escape(dependency.Name),
                $"[{role}]{Markup.Escape(dependency.Status.ToString())}[/]",
                Markup.Escape(version ?? string.Empty),
                Markup.Escape(details ?? string.Empty));
        }

        return table;
    }

    private static void WriteFailureSummary(IReadOnlyList<ModuleTestSuiteDisplayLine> lines)
    {
        foreach (var line in lines)
        {
            var text = Markup.Escape(line.Text);
            var role = Role(line.Color);
            ConsoleOutput.WriteMarkupLine(role is null || text.Length == 0 ? text : $"[{role}]{text}[/]");
        }
    }

    private static string? Role(ConsoleColor? color) => color switch
    {
        ConsoleColor.Red or ConsoleColor.DarkRed => "fail",
        ConsoleColor.Yellow or ConsoleColor.DarkYellow => "warn",
        ConsoleColor.Green or ConsoleColor.DarkGreen => "pass",
        ConsoleColor.Cyan or ConsoleColor.Magenta or ConsoleColor.Blue => "info",
        ConsoleColor.Gray or ConsoleColor.DarkGray => "muted",
        _ => null
    };

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(line) ? null : line;
    }
}
