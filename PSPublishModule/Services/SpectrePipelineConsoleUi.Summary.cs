using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

internal static partial class SpectrePipelineConsoleUi
{
    public static void WriteSummary(ModulePipelineResult res)
    {
        if (res is null) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);
        static string StatusMarkup(CheckStatus status)
            => status switch
            {
                CheckStatus.Pass => "[green]Pass[/]",
                CheckStatus.Warning => "[yellow]Warning[/]",
                _ => "[red]Fail[/]"
            };

        static string SigningStatusMarkup(bool ok) => ok ? "[green]Pass[/]" : "[red]Fail[/]";
        static int CountIssues(ProjectConsistencyReport report, FileConsistencySettings? settings)
        {
            if (report is null) return 0;
            if (settings is null) return report.ProblematicFiles.Length;

            int count = 0;
            foreach (var f in report.ProblematicFiles)
            {
                if (f.NeedsEncodingConversion || f.NeedsLineEndingConversion)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMissingFinalNewline && f.MissingFinalNewline)
                {
                    count++;
                    continue;
                }

                if (settings.CheckMixedLineEndings && f.HasMixedLineEndings)
                {
                    count++;
                }
            }

            return count;
        }

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;

        AnsiConsole.Write(new Rule($"[green]{(unicode ? "✅" : "OK")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "📦" : "*")} Module", $"{Esc(res.Plan.ModuleName)} [grey]{Esc(res.Plan.ResolvedVersion)}[/]");
        table.AddRow($"{(unicode ? "🧪" : "*")} Staging", Esc(res.BuildResult.StagingPath));

        var fileConsistencySettings = res.Plan.FileConsistencySettings;
        if (fileConsistencySettings?.Enable == true && fileConsistencySettings.Severity != ValidationSeverity.Off)
        {
            var scope = fileConsistencySettings.ResolveScope();

            if (scope != FileConsistencyScope.ProjectOnly)
            {
                if (res.FileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
                {
                    var status = res.FileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.FileConsistencyReport.Summary.TotalFiles;
                    var issues = CountIssues(res.FileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "🔎" : "*")} File consistency",
                        $"{StatusMarkup(status)} [grey]{issues}/{total} with issues ({compliance:0.0}% compliant)[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "🔎" : "*")} File consistency", "[grey]Disabled[/]");
                }
            }

            if (scope != FileConsistencyScope.StagingOnly)
            {
                if (res.ProjectRootFileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
                {
                    var status = res.ProjectRootFileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.ProjectRootFileConsistencyReport.Summary.TotalFiles;
                    var issues = CountIssues(res.ProjectRootFileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "🔎" : "*")} File consistency (project)",
                        $"{StatusMarkup(status)} [grey]{issues}/{total} with issues ({compliance:0.0}% compliant)[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "🔎" : "*")} File consistency (project)", "[grey]Disabled[/]");
                }
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} File consistency", "[grey]Disabled[/]");
        }

        if (res.CompatibilityReport is not null && res.Plan.CompatibilitySettings?.Severity != ValidationSeverity.Off)
        {
            var s = res.CompatibilityReport.Summary;
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} Compatibility",
                $"{StatusMarkup(s.Status)} [grey]{s.CrossCompatibilityPercentage:0.0}% cross-compatible[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} Compatibility", "[grey]Disabled[/]");
        }

        if (res.ValidationReport is not null)
        {
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} Module validation",
                $"{StatusMarkup(res.ValidationReport.Status)} [grey]{Esc(res.ValidationReport.Summary)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "🔎" : "*")} Module validation", "[grey]Disabled[/]");
        }

        if (res.Plan.Formatting is not null)
        {
            var staging = FormattingSummary.FromResults(res.FormattingStagingResults);
            var status = staging.Status;
            var parts = new List<string>(2) { FormattingSummary.FormatPartMarkup("staging", staging) };

            if (res.Plan.Formatting.Options.UpdateProjectRoot)
            {
                var project = FormattingSummary.FromResults(res.FormattingProjectResults);
                status = FormattingSummary.Worst(status, project.Status);
                parts.Add(FormattingSummary.FormatPartMarkup("project", project));
            }

            table.AddRow(
                $"{(unicode ? "🎨" : "*")} Formatting",
                $"{StatusMarkup(status)} [grey]{string.Join(", ", parts)}[/]");
        }
        else
        {
            table.AddRow($"{(unicode ? "🎨" : "*")} Formatting", "[grey]Disabled[/]");
        }

        if (res.Plan.SignModule)
        {
            if (res.SigningResult is null)
            {
                table.AddRow($"{(unicode ? "🔏" : "*")} Signing", "[yellow]Enabled[/]");
            }
            else
            {
                var s = res.SigningResult;
                var bits = new List<string>(6)
                {
                    $"signed [green]{s.SignedTotal}[/]",
                    $"new [green]{s.SignedNew}[/]",
                    $"re-signed [green]{s.Resigned}[/]",
                    $"already [grey]{s.AlreadySignedOther}[/] 3p",
                    $"already [grey]{s.AlreadySignedByThisCert}[/] ours"
                };

                if (s.Failed > 0) bits.Add($"failed [red]{s.Failed}[/]");

                table.AddRow($"{(unicode ? "🔏" : "*")} Signing", $"{SigningStatusMarkup(s.Success)} [grey]{string.Join(", ", bits)}[/]");
            }
        }
        else
        {
            table.AddRow($"{(unicode ? "🔏" : "*")} Signing", "[grey]Disabled[/]");
        }

        if (res.ArtefactResults is { Length: > 0 })
            table.AddRow($"{(unicode ? "📦" : "*")} Artefacts", $"[green]{res.ArtefactResults.Length}[/]");
        else
            table.AddRow($"{(unicode ? "📦" : "*")} Artefacts", "[grey]None[/]");

        if (res.InstallResult is not null)
            table.AddRow($"{(unicode ? "📥" : "*")} Install", $"[green]{Esc(res.InstallResult.Version)}[/]");
        else
            table.AddRow($"{(unicode ? "📥" : "*")} Install", "[grey]Disabled[/]");

        AnsiConsole.Write(table);

        if (res.ValidationReport is not null && res.ValidationReport.Checks.Length > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[grey]Validation details[/]").LeftJustified());

            var details = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Check").NoWrap())
                .AddColumn(new TableColumn("Result"));

            foreach (var check in res.ValidationReport.Checks)
            {
                if (check is null) continue;
                details.AddRow(
                    Esc(check.Name),
                    $"{StatusMarkup(check.Status)} [grey]{Esc(check.Summary)}[/]");
            }

            AnsiConsole.Write(details);
        }

        if (res.FileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
            WriteFileConsistencyIssues(res.FileConsistencyReport, res.Plan.FileConsistencySettings, "staging", border);

        if (res.ProjectRootFileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
            WriteFileConsistencyIssues(res.ProjectRootFileConsistencyReport, res.Plan.FileConsistencySettings, "project", border);

        if (res.ArtefactResults is { Length: > 0 })
        {
            var artefacts = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Type").NoWrap())
                .AddColumn(new TableColumn("Id").NoWrap())
                .AddColumn(new TableColumn("Path"));

            foreach (var a in res.ArtefactResults)
                artefacts.AddRow(Esc(a.Type.ToString()), Esc(a.Id ?? string.Empty), Esc(a.OutputPath));

            AnsiConsole.Write(artefacts);
        }

        if (res.InstallResult is not null && res.InstallResult.InstalledPaths is { Count: > 0 })
        {
            AnsiConsole.MarkupLine($"[grey]{(unicode ? "📍" : "*")} Installed paths:[/]");
            foreach (var path in res.InstallResult.InstalledPaths)
                AnsiConsole.MarkupLine($"  [grey]{(unicode ? "→" : "->")}[/] {Esc(path)}");
        }
    }

    private static void WriteFileConsistencyIssues(
        ProjectConsistencyReport report,
        FileConsistencySettings? settings,
        string label,
        TableBorder border)
    {
        if (report is null) return;

        var issues = report.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
        if (issues.Length == 0) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[grey]File consistency issues ({Esc(label)})[/]").LeftJustified());

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
            AnsiConsole.MarkupLine($"[grey]Report:[/] {Esc(report.ExportPath)}");

        var summary = report.Summary;
        var parts = new List<string>();
        if (summary.FilesNeedingEncodingConversion > 0)
            parts.Add($"encoding {summary.FilesNeedingEncodingConversion}");
        if (summary.FilesNeedingLineEndingConversion > 0)
            parts.Add($"line endings {summary.FilesNeedingLineEndingConversion}");
        if (settings?.CheckMixedLineEndings == true && summary.FilesWithMixedLineEndings > 0)
            parts.Add($"mixed {summary.FilesWithMixedLineEndings}");
        if (settings?.CheckMissingFinalNewline == true && summary.FilesMissingFinalNewline > 0)
            parts.Add($"missing newline {summary.FilesMissingFinalNewline}");

        if (parts.Count > 0)
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {string.Join(", ", parts)} (total {summary.TotalFiles})");
        else
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {summary.TotalFiles} files scanned");

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Path"))
            .AddColumn(new TableColumn("Issues"));

        const int maxItems = 20;
        var shown = 0;
        foreach (var item in issues)
        {
            var reasons = BuildFileConsistencyReasons(item, settings);
            if (reasons.Count == 0) continue;

            table.AddRow(Esc(item.RelativePath), Esc(string.Join(", ", reasons)));
            if (++shown >= maxItems) break;
        }

        AnsiConsole.Write(table);

        if (issues.Length > maxItems)
            AnsiConsole.MarkupLine($"[grey]... {issues.Length - maxItems} more not shown.[/]");
    }

    private static List<string> BuildFileConsistencyReasons(
        ProjectConsistencyFileDetail file,
        FileConsistencySettings? settings)
    {
        var reasons = new List<string>(4);

        if (file.NeedsEncodingConversion)
        {
            var current = file.CurrentEncoding?.ToString() ?? "Unknown";
            reasons.Add($"encoding {current} (expected {file.RecommendedEncoding})");
        }

        if (file.NeedsLineEndingConversion)
        {
            var current = file.CurrentLineEnding.ToString();
            reasons.Add($"line endings {current} (expected {file.RecommendedLineEnding})");
        }

        if (settings?.CheckMixedLineEndings == true && file.HasMixedLineEndings)
            reasons.Add("mixed line endings");

        if (settings?.CheckMissingFinalNewline == true && file.MissingFinalNewline)
            reasons.Add("missing final newline");

        var error = file.Error;
        if (!string.IsNullOrWhiteSpace(error))
            reasons.Add($"error: {error!.Trim()}");

        return reasons;
    }

    public static void WriteFailureSummary(ModulePipelinePlan plan, Exception error)
    {
        if (plan is null) return;
        if (error is null) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;

        AnsiConsole.Write(new Rule($"[red]{(unicode ? "❌" : "!!")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "📦" : "*")} Module", $"{Esc(plan.ModuleName)} [grey]{Esc(plan.ResolvedVersion)}[/]");
        table.AddRow($"{(unicode ? "📁" : "*")} Project", Esc(plan.ProjectRoot));

        var stagingText = string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath;
        table.AddRow($"{(unicode ? "🧪" : "*")} Staging", Esc(stagingText));

        if (error is ModuleSigningException signingEx && signingEx.Result is not null)
        {
            var s = signingEx.Result;
            table.AddRow(
                $"{(unicode ? "🔏" : "*")} Signing",
                $"[red]Fail[/] [grey]signed {s.SignedTotal}, already {s.AlreadySignedOther} 3p/{s.AlreadySignedByThisCert} ours, failed {s.Failed}[/]");
        }

        var message = NormalizeFailureMessage(error, maxLength: 220);
        if (!string.IsNullOrWhiteSpace(message))
            table.AddRow($"{(unicode ? "💥" : "*")} Error", Esc(message));

        var hint = BuildHint(message);
        if (!string.IsNullOrWhiteSpace(hint))
            table.AddRow($"{(unicode ? "💡" : "*")} Hint", Esc(hint));

        AnsiConsole.Write(table);
    }

    private static string NormalizeFailureMessage(Exception error, int maxLength = 140)
    {
        if (error is null) return string.Empty;
        maxLength = Math.Max(40, maxLength);

        var msg = error.GetBaseException().Message ?? error.Message ?? string.Empty;
        msg = msg.Replace("\r\n", " ").Replace("\n", " ").Trim();
        if (msg.Length <= maxLength) return msg;
        return msg.Substring(0, maxLength - 1) + "…";
    }

    private static string BuildHint(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        if (message.IndexOf("Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Verify a repository is registered and reachable (Get-PSRepository). If PSGallery is missing, run Register-PSRepository -Default.";
        }

        return string.Empty;
    }
}
