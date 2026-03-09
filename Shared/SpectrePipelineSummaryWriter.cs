using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;
using Spectre.Console;

namespace PowerForge.ConsoleShared;

internal static class SpectrePipelineSummaryWriter
{
    private static string CountLabel(int count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

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
        var versionText = BuildServices.FormatVersionWithPreRelease(res.Plan.ResolvedVersion, res.Plan.PreRelease);

        AnsiConsole.Write(new Rule($"[green]{(unicode ? "✅" : "OK")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "📦" : "*")} Module", $"{Esc(res.Plan.ModuleName)} [grey]{Esc(versionText)}[/]");
        table.AddRow($"{(unicode ? "🧪" : "*")} Staging", Esc(res.BuildResult.StagingPath));

        var fileConsistencySettings = res.Plan.FileConsistencySettings;
        if (fileConsistencySettings?.Enable == true && fileConsistencySettings.Severity != ValidationSeverity.Off)
        {
            var scope = fileConsistencySettings.ResolveScope();

            if (scope != FileConsistencyScope.ProjectOnly)
            {
                if (res.FileConsistencyReport is not null)
                {
                    var status = res.FileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.FileConsistencyReport.Summary.TotalFiles;
                    var issues = CountIssues(res.FileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "🔎" : "*")} File consistency",
                        $"{StatusMarkup(status)} [grey]{CountLabel(total, "file", "files")} checked, {CountLabel(issues, "issue", "issues")} ({compliance:0.0}% compliant)[/]");
                }
                else
                {
                    table.AddRow($"{(unicode ? "🔎" : "*")} File consistency", "[grey]Disabled[/]");
                }
            }

            if (scope != FileConsistencyScope.StagingOnly)
            {
                if (res.ProjectRootFileConsistencyReport is not null)
                {
                    var status = res.ProjectRootFileConsistencyStatus ?? CheckStatus.Warning;
                    var total = res.ProjectRootFileConsistencyReport.Summary.TotalFiles;
                    var issues = CountIssues(res.ProjectRootFileConsistencyReport, fileConsistencySettings);
                    var compliance = total <= 0 ? 100.0 : Math.Round(((total - issues) / (double)total) * 100.0, 1);
                    table.AddRow(
                        $"{(unicode ? "🔎" : "*")} File consistency (project)",
                        $"{StatusMarkup(status)} [grey]{CountLabel(total, "file", "files")} checked, {CountLabel(issues, "issue", "issues")} ({compliance:0.0}% compliant)[/]");
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
            var detail = s.Status == CheckStatus.Pass
                ? $"{s.CrossCompatibilityPercentage:0.0}% cross-compatible"
                : $"{s.CrossCompatibilityPercentage:0.0}% cross-compatible, {CountLabel(s.FilesWithIssues, "file", "files")} with issues";
            table.AddRow(
                $"{(unicode ? "🔎" : "*")} Compatibility",
                $"{StatusMarkup(s.Status)} [grey]{Esc(detail)}[/]");
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

        if (res.PublishResults is { Length: > 0 })
            table.AddRow($"{(unicode ? "🚀" : "*")} Publish", $"[green]{res.PublishResults.Length}[/] destination(s)");
        else
            table.AddRow($"{(unicode ? "🚀" : "*")} Publish", "[grey]Disabled[/]");

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
                var detail = NormalizeCheckSummary(check.Name, check.Summary);
                details.AddRow(
                    Esc(check.Name),
                    FormatStatusDetail(check.Status, detail));
            }

            if (res.CompatibilityReport is not null && res.Plan.CompatibilitySettings?.Severity != ValidationSeverity.Off)
            {
                var compatibility = res.CompatibilityReport.Summary;
                details.AddRow(
                    "Compatibility",
                    FormatStatusDetail(compatibility.Status, compatibility.Message));
            }

            AnsiConsole.Write(details);

            var validationRecommendations = BuildRecommendations(res, BuildDiagnosticArea.Validation);
            WriteRecommendationTable("Recommended actions", validationRecommendations, border);
        }

        if (res.FileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
            WriteFileConsistencyIssues(
                pipelineResult: res,
                report: res.FileConsistencyReport,
                settings: res.Plan.FileConsistencySettings,
                label: "staging",
                border: border,
                projectReport: res.ProjectRootFileConsistencyReport);

        if (res.ProjectRootFileConsistencyReport is not null && res.Plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
            WriteFileConsistencyIssues(
                pipelineResult: res,
                report: res.ProjectRootFileConsistencyReport,
                settings: res.Plan.FileConsistencySettings,
                label: "project",
                border: border,
                projectReport: null);

        if (res.CompatibilityReport is not null && res.Plan.CompatibilitySettings?.Severity != ValidationSeverity.Off)
            WriteCompatibilityIssues(res, res.CompatibilityReport, border);

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

        if (res.PublishResults is { Length: > 0 })
        {
            AnsiConsole.WriteLine();

            var publishes = new Table()
                .Border(border)
                .AddColumn(new TableColumn("Destination").NoWrap())
                .AddColumn(new TableColumn("Target").NoWrap())
                .AddColumn(new TableColumn("Published").NoWrap())
                .AddColumn(new TableColumn("Reference"));

            foreach (var publish in res.PublishResults)
            {
                var target = BuildPublishTarget(publish);
                var published = BuildPublishLabel(publish);
                var reference = BuildPublishReference(res.Plan, publish);
                publishes.AddRow(Esc(publish.Destination.ToString()), Esc(target), Esc(published), Esc(reference));
            }

            AnsiConsole.Write(publishes);
        }

        if (res.InstallResult is not null && res.InstallResult.InstalledPaths is { Count: > 0 })
        {
            AnsiConsole.MarkupLine($"[grey]{(unicode ? "📍" : "*")} Installed paths:[/]");
            foreach (var path in res.InstallResult.InstalledPaths)
                AnsiConsole.MarkupLine($"  [grey]{(unicode ? "→" : "->")}[/] {Esc(path)}");
        }
    }

    public static void WriteFailureSummary(ModulePipelinePlan plan, Exception error)
    {
        if (plan is null || error is null) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var unicode = ConsoleEncoding.ShouldRenderUnicode(AnsiConsole.Profile.Capabilities.Unicode);
        var border = unicode ? TableBorder.Rounded : TableBorder.Simple;

        AnsiConsole.Write(new Rule($"[red]{(unicode ? "❌" : "!!")} Summary[/]").LeftJustified());

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        table.AddRow($"{(unicode ? "📦" : "*")} Module", $"{Esc(plan.ModuleName)} [grey]{Esc(BuildServices.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease))}[/]");
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

    private static string BuildPublishTarget(ModulePublishResult publish)
    {
        if (publish.Destination == PublishDestination.GitHub)
        {
            var owner = string.IsNullOrWhiteSpace(publish.UserName) ? "(owner?)" : publish.UserName;
            var repo = string.IsNullOrWhiteSpace(publish.RepositoryName) ? "(repo?)" : publish.RepositoryName;
            return $"{owner}/{repo}";
        }

        return string.IsNullOrWhiteSpace(publish.RepositoryName) ? "(repository?)" : publish.RepositoryName!;
    }

    private static string BuildPublishLabel(ModulePublishResult publish)
    {
        var version = string.IsNullOrWhiteSpace(publish.VersionText) ? "(unknown)" : publish.VersionText;
        if (publish.Destination != PublishDestination.GitHub)
            return version;

        var tag = string.IsNullOrWhiteSpace(publish.TagName) ? "(tag?)" : publish.TagName;
        var assetCount = publish.AssetPaths?.Length ?? 0;
        return assetCount > 0 ? $"{tag} ({version}, assets {assetCount})" : $"{tag} ({version})";
    }

    private static string BuildPublishReference(ModulePipelinePlan plan, ModulePublishResult publish)
    {
        if (!string.IsNullOrWhiteSpace(publish.ReleaseUrl))
            return publish.ReleaseUrl!;

        if (publish.Destination == PublishDestination.PowerShellGallery &&
            string.Equals(publish.RepositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(plan.ModuleName) &&
            !string.IsNullOrWhiteSpace(publish.VersionText))
        {
            return $"https://www.powershellgallery.com/packages/{plan.ModuleName}/{publish.VersionText}";
        }

        return string.Empty;
    }

    private static void WriteFileConsistencyIssues(
        ModulePipelineResult pipelineResult,
        ProjectConsistencyReport report,
        FileConsistencySettings? settings,
        string label,
        TableBorder border,
        ProjectConsistencyReport? projectReport)
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
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {CountLabel(summary.TotalFiles, "file", "files")} checked");

        var scope = string.Equals(label, "project", StringComparison.OrdinalIgnoreCase)
            ? BuildDiagnosticScope.Project
            : BuildDiagnosticScope.Staging;
        var recommendations = BuildRecommendations(pipelineResult, BuildDiagnosticArea.FileConsistency, scope);
        WriteRecommendationTable("Recommended actions", recommendations, border);

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

    private static void WriteCompatibilityIssues(ModulePipelineResult pipelineResult, PowerShellCompatibilityReport report, TableBorder border)
    {
        if (report is null) return;

        var summary = report.Summary;
        if (summary.Status == CheckStatus.Pass && summary.FilesWithIssues <= 0) return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var affectedFiles = report.Files.Where(f => f.Issues.Length > 0).ToArray();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[grey]Compatibility details[/]").LeftJustified());
        var detailsTable = new Table()
            .Border(border)
            .AddColumn(new TableColumn("Item").NoWrap())
            .AddColumn(new TableColumn("Value"));

        detailsTable.AddRow("Summary", Esc(summary.Message));

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
            detailsTable.AddRow("Report", Esc(report.ExportPath));

        AnsiConsole.Write(detailsTable);

        var recommendations = BuildRecommendations(pipelineResult, BuildDiagnosticArea.Compatibility);
        WriteRecommendationTable("Recommended actions", recommendations, border);

        if (affectedFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No file-level compatibility items were attached to the report.[/]");
            return;
        }

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn("File").NoWrap())
            .AddColumn(new TableColumn("Target").NoWrap())
            .AddColumn(new TableColumn("Type").NoWrap())
            .AddColumn(new TableColumn("Description"))
            .AddColumn(new TableColumn("Recommendation"));

        const int maxItems = 8;
        foreach (var file in affectedFiles.Take(maxItems))
        {
            var editions = new List<string>(2);
            if (!file.PowerShell51Compatible) editions.Add("PS 5.1");
            if (!file.PowerShell7Compatible) editions.Add("PS 7");
            if (editions.Count == 0) editions.Add("Both");

            var editionLabel = string.Join(", ", editions);
            var issuesToShow = file.Issues.Take(4).ToArray();
            for (var i = 0; i < issuesToShow.Length; i++)
            {
                var issue = issuesToShow[i];
                var description = string.IsNullOrWhiteSpace(issue.Description)
                    ? "No description"
                    : issue.Description;
                var recommendation = string.IsNullOrWhiteSpace(issue.Recommendation)
                    ? "Review this finding"
                    : issue.Recommendation;
                var issueType = issue.Type == PowerShellCompatibilityIssueType.DotNetFramework
                    ? "DotNet"
                    : issue.Type.ToString();

                table.AddRow(
                    Markup.Escape(file.RelativePath),
                    Markup.Escape(editionLabel),
                    Markup.Escape(issueType),
                    Markup.Escape(description),
                    Markup.Escape(recommendation));
            }

            if (file.Issues.Length > issuesToShow.Length)
            {
                table.AddRow(
                    Markup.Escape(file.RelativePath),
                    Markup.Escape(editionLabel),
                    "[grey]More[/]",
                    $"[grey]+{file.Issues.Length - issuesToShow.Length} additional findings[/]",
                    "[grey]Review the full CSV report for the remaining items[/]");
            }
        }

        AnsiConsole.Write(table);

        if (affectedFiles.Length > maxItems)
            AnsiConsole.MarkupLine($"[grey]... {CountLabel(affectedFiles.Length - maxItems, "more file", "more files")} with compatibility issues not shown.[/]");
    }

    private static string NormalizeCheckSummary(string? name, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var text = summary!.Trim();
        if (text.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(name, "Module structure", StringComparison.OrdinalIgnoreCase))
                return "Structure verified";

            return "No issues found";
        }

        return text;
    }

    private static List<(string When, string Action)> BuildRecommendations(
        ModulePipelineResult result,
        BuildDiagnosticArea area,
        BuildDiagnosticScope? scope = null)
    {
        if (result?.Diagnostics is null || result.Diagnostics.Length == 0)
            return new List<(string When, string Action)>();

        return result.Diagnostics
            .Where(diagnostic => diagnostic.Area == area && (!scope.HasValue || diagnostic.Scope == scope.Value))
            .Select(static diagnostic => (When: diagnostic.Summary, Action: BuildRecommendationAction(diagnostic)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.When) && !string.IsNullOrWhiteSpace(item.Action))
            .Distinct()
            .ToList();
    }

    private static string FormatStatusDetail(CheckStatus status, string? detail)
    {
        static string StatusWithSeparator(CheckStatus value)
            => value switch
            {
                CheckStatus.Pass => "[green]Pass:[/]",
                CheckStatus.Warning => "[yellow]Warning:[/]",
                _ => "[red]Fail:[/]"
            };

        return string.IsNullOrWhiteSpace(detail)
            ? StatusWithSeparator(status)
            : $"{StatusWithSeparator(status)} [grey]{Markup.Escape(detail!.Trim())}[/]";
    }

    private static void WriteRecommendationTable(
        string title,
        IReadOnlyList<(string When, string Action)> recommendations,
        TableBorder border)
    {
        if (recommendations is null || recommendations.Count == 0)
            return;

        static string Esc(string? s) => Markup.Escape(s ?? string.Empty);

        var table = new Table()
            .Border(border)
            .AddColumn(new TableColumn(title).NoWrap())
            .AddColumn(new TableColumn("Do this"));

        foreach (var recommendation in recommendations)
        {
            if (string.IsNullOrWhiteSpace(recommendation.When) || string.IsNullOrWhiteSpace(recommendation.Action))
                continue;

            table.AddRow(Esc(recommendation.When), Esc(recommendation.Action));
        }

        if (table.Rows.Count > 0)
            AnsiConsole.Write(table);
    }

    private static string BuildRecommendationAction(BuildDiagnostic diagnostic)
    {
        if (diagnostic is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(diagnostic.SuggestedCommand))
            return $"{diagnostic.RecommendedAction} Suggested command: {diagnostic.SuggestedCommand}";

        return diagnostic.RecommendedAction;
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

    private static string NormalizeFailureMessage(Exception error, int maxLength = 140)
    {
        if (error is null) return string.Empty;
        maxLength = Math.Max(40, maxLength);

        var msg = error.GetBaseException().Message ?? error.Message ?? string.Empty;
        var lines = msg.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var importHeader = lines.FirstOrDefault(static line => line.StartsWith("Import-Module failed", StringComparison.OrdinalIgnoreCase));
        var cause = lines.FirstOrDefault(static line => line.StartsWith("Cause:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(importHeader) && !string.IsNullOrWhiteSpace(cause))
            msg = importHeader + " " + cause;
        else
            msg = string.Join(" ", lines);

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

        if (message.IndexOf("manifest contains one or more members that are not valid", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "The module manifest contains an unsupported top-level key. Move prerelease/package metadata under PrivateData.PSData.";
        }

        return string.Empty;
    }
}
