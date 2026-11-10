using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleMonster.Layout;
using ConsoleMonster.Text;
using ConsoleMonster.Widgets;
using PowerForge;

namespace PowerForge.ConsoleUi;

internal static partial class ModulePipelineSummary
{
    private const int MaxFileRows = 20;
    private const int MaxCompatibilityFiles = 8;

    private static void AddDetails(ModulePipelineResult res, PipelinePublishSummary publishSummary, SummaryFrame summary)
    {
        var plan = res.Plan;
        var compatibilityOn = res.CompatibilityReport is not null && plan.CompatibilitySettings?.Severity != ValidationSeverity.Off;

        if (res.ValidationReport is { Checks.Length: > 0 } validation)
        {
            var rows = validation.Checks.Where(check => check is not null)
                .Select(check => (Name: check.Name, check.Status, Detail: NormalizeCheckSummary(check.Name, check.Summary)))
                .ToList();
            if (compatibilityOn) rows.Add(("Compatibility", res.CompatibilityReport!.Summary.Status, res.CompatibilityReport.Summary.Message));
            var table = new Table().AddColumn("Check").AddColumn("Result");
            foreach (var row in rows) table.AddRow(Markup.Escape(row.Name), SummaryFrame.Word(row.Status) + " [muted]" + Markup.Escape(row.Detail ?? string.Empty) + "[/]");
            summary.Detail("Validation details", table);
        }

        var recommendations = BuildRecommendations(res);
        if (recommendations.Count > 0)
        {
            var hasLocation = recommendations.Any(row => !string.IsNullOrWhiteSpace(row.Where));
            var table = new Table().AddColumn("When");
            if (hasLocation) table.AddColumn("Where");
            // The action ends with the command to run ("Suggested command: ..."); show all of it.
            table.AddColumn(new TableColumn("Do this") { MaxLines = TableColumn.Unlimited });
            foreach (var row in recommendations)
            {
                if (hasLocation) table.AddRow(Markup.Escape(row.When), Markup.Escape(row.Where), Markup.Escape(row.Action));
                else table.AddRow(Markup.Escape(row.When), Markup.Escape(row.Action));
            }
            summary.Detail("Recommended actions", table);
        }

        if (plan.FileConsistencySettings?.Severity != ValidationSeverity.Off)
        {
            AddFileConsistencyIssues(res.FileConsistencyReport, plan.FileConsistencySettings, "staging", summary);
            AddFileConsistencyIssues(res.ProjectRootFileConsistencyReport, plan.FileConsistencySettings, "project", summary);
        }

        if (compatibilityOn) AddCompatibilityIssues(res.CompatibilityReport!, summary);

        if (res.DiagnosticsBaseline is { } baseline)
        {
            var list = new KeyValueList();
            if (!string.IsNullOrWhiteSpace(baseline.BaselinePath)) list.Add("Path", Markup.Escape(baseline.BaselinePath!));
            list.Add("Loaded", baseline.BaselineLoaded ? "Yes" : "No");
            list.Add("Baseline issues", baseline.BaselineDiagnosticCount.ToString());
            list.Add("Current issues", baseline.CurrentDiagnosticCount.ToString());
            list.Add("Known issues", baseline.ExistingDiagnosticCount.ToString());
            list.Add("New issues", baseline.NewDiagnosticCount.ToString());
            list.Add("Resolved issues", baseline.ResolvedDiagnosticCount.ToString());
            if (baseline.BaselineGenerated) list.Add("Action", "Baseline generated from current diagnostics");
            else if (baseline.BaselineUpdated) list.Add("Action", "Baseline updated from current diagnostics");
            summary.Detail("Diagnostics baseline", list);
        }

        if (res.DiagnosticsPolicy is { } policy)
        {
            var list = new KeyValueList();
            list.Add("Fail on new diagnostics", policy.FailOnNewDiagnostics ? "Yes" : "No");
            list.Add("Fail on severity", policy.FailOnSeverity?.ToString() ?? "No");
            list.Add("Current diagnostics", policy.CurrentDiagnosticCount.ToString());
            list.Add("New diagnostics", policy.NewDiagnosticCount.ToString());
            list.Add("Severity matches", policy.SeverityDiagnosticCount.ToString());
            list.Add("Result", policy.PolicyViolated ? "[fail]Fail[/]" : "[pass]Pass[/]");
            if (!string.IsNullOrWhiteSpace(policy.FailureReason)) list.Add("Reason", Markup.Escape(policy.FailureReason!));
            summary.Detail("Diagnostics policy", list);
        }

        AddOwnerNotes(res.OwnerNotes, summary);

        if (res.ArtefactResults is { Length: > 0 } artefacts)
            summary.Detail("Artefacts", Table.For(artefacts)
                .Column("Type", a => a.Type.ToString())
                .Column("Id", a => a.Id ?? string.Empty)
                .Column("Path", a => a.OutputPath));

        if (publishSummary.Rows.Count > 0)
            summary.Detail("Publishes", Table.For(publishSummary.Rows)
                .Column("Channel", row => row.Channel)
                .Column("Target", row => row.Target)
                .Column("Result", row => row.Result)
                .Column("Method", row => row.Method)
                .Column("Reference", row => row.Reference));

        if (res.InstallResult is { InstalledPaths.Count: > 0 } install)
            summary.Detail("Installed paths", new Rows(install.InstalledPaths.Select(path => (IWidget)TextBlock.Plain(path)).ToArray()));
    }

    internal sealed class RecommendationRow(string when, string where, string action)
    {
        public string When { get; } = when ?? string.Empty;
        public string Where { get; } = where ?? string.Empty;
        public string Action { get; } = action ?? string.Empty;
    }

    private static IReadOnlyList<RecommendationRow> BuildRecommendations(ModulePipelineResult result)
        => result?.Diagnostics is { Length: > 0 } diagnostics ? BuildRecommendationRows(diagnostics) : Array.Empty<RecommendationRow>();

    /// <summary>One row per distinct diagnostic with a recommended action: summary, location and what to do.</summary>
    internal static IReadOnlyList<RecommendationRow> BuildRecommendationRows(IEnumerable<BuildDiagnostic> diagnostics)
    {
        if (diagnostics is null) return Array.Empty<RecommendationRow>();
        return diagnostics
            .Where(static diagnostic => diagnostic is not null)
            .Select(static diagnostic => new RecommendationRow(
                diagnostic.Summary,
                diagnostic.SourcePath ?? string.Empty,
                string.IsNullOrWhiteSpace(diagnostic.SuggestedCommand)
                    ? diagnostic.RecommendedAction
                    : $"{diagnostic.RecommendedAction} Suggested command: {diagnostic.SuggestedCommand}"))
            .Where(static row => !string.IsNullOrWhiteSpace(row.When) && !string.IsNullOrWhiteSpace(row.Action))
            .GroupBy(static row => string.Join("|", row.When, row.Where, row.Action), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static void AddFileConsistencyIssues(ProjectConsistencyReport? report, FileConsistencySettings? settings, string label, SummaryFrame summary)
    {
        var files = report?.ProblematicFiles ?? Array.Empty<ProjectConsistencyFileDetail>();
        var rows = files.Select(file => (file.RelativePath, Reasons: BuildFileConsistencyReasons(file, settings)))
            .Where(row => row.Reasons.Count > 0)
            .ToArray();
        if (rows.Length == 0) return;

        foreach (var row in rows)
            summary.Attention.Add(ConsoleMonster.Frames.HealthState.Warning, $"File consistency ({label}): " + row.Reasons[0], row.RelativePath);

        var table = new Table().AddColumn("Path").AddColumn("Issues");
        foreach (var row in rows.Take(MaxFileRows)) table.AddRow(Markup.Escape(row.RelativePath), Markup.Escape(string.Join(", ", row.Reasons)));
        var blocks = new List<IWidget>();
        if (!string.IsNullOrWhiteSpace(report!.ExportPath)) blocks.Add(new TextBlock("[muted]Report:[/] " + Markup.Escape(report.ExportPath!)));
        blocks.Add(table);
        if (rows.Length > MaxFileRows) blocks.Add(new TextBlock($"[muted]... {rows.Length - MaxFileRows} more not shown.[/]"));
        summary.Detail($"File consistency issues ({label})", new Rows(blocks.ToArray()));
    }

    private static void AddCompatibilityIssues(PowerShellCompatibilityReport report, SummaryFrame summary)
    {
        var overall = report.Summary;
        if (overall.Status == CheckStatus.Pass && overall.FilesWithIssues <= 0) return;

        var affected = report.Files.Where(file => file.Issues.Length > 0).ToArray();
        var blocks = new List<IWidget> { TextBlock.Plain(overall.Message ?? string.Empty) };
        if (!string.IsNullOrWhiteSpace(report.ExportPath)) blocks.Add(new TextBlock("[muted]Report:[/] " + Markup.Escape(report.ExportPath!)));
        if (affected.Length == 0)
        {
            blocks.Add(new TextBlock("[muted]No file-level compatibility items were attached to the report.[/]"));
            summary.Detail("Compatibility details", new Rows(blocks.ToArray()));
            return;
        }

        var table = new Table().AddColumn("File").AddColumn("Target").AddColumn("Type").AddColumn("Description").AddColumn("Recommendation");
        foreach (var file in affected.Take(MaxCompatibilityFiles))
        {
            var editions = new List<string>(2);
            if (!file.PowerShell51Compatible) editions.Add("PS 5.1");
            if (!file.PowerShell7Compatible) editions.Add("PS 7");
            var target = editions.Count == 0 ? "Both" : string.Join(", ", editions);
            foreach (var issue in file.Issues.Take(4))
            {
                var type = issue.Type == PowerShellCompatibilityIssueType.DotNetFramework ? "DotNet" : issue.Type.ToString();
                var description = string.IsNullOrWhiteSpace(issue.Description) ? "No description" : issue.Description;
                table.AddRow(Markup.Escape(file.RelativePath), Markup.Escape(target), Markup.Escape(type), Markup.Escape(description),
                    Markup.Escape(string.IsNullOrWhiteSpace(issue.Recommendation) ? "Review this finding" : issue.Recommendation));
                summary.Attention.Add(ConsoleMonster.Frames.HealthState.Warning, $"Compatibility ({target}): {type}", file.RelativePath);
            }
            if (file.Issues.Length > 4)
                table.AddRow(Markup.Escape(file.RelativePath), Markup.Escape(target), "[muted]More[/]",
                    $"[muted]+{file.Issues.Length - 4} additional findings[/]", "[muted]Review the full CSV report for the remaining items[/]");
        }
        blocks.Add(table);
        if (affected.Length > MaxCompatibilityFiles)
            blocks.Add(new TextBlock($"[muted]... {CountLabel(affected.Length - MaxCompatibilityFiles, "more file", "more files")} with compatibility issues not shown.[/]"));
        summary.Detail("Compatibility details", new Rows(blocks.ToArray()));
    }

    // Action-needed notes are findings; the rest are listed with their reasons and next steps.
    private static void AddOwnerNotes(IReadOnlyList<ModuleOwnerNote>? notes, SummaryFrame summary)
    {
        var fyi = (notes ?? Array.Empty<ModuleOwnerNote>())
            .Where(note => note is not null && note.Severity != ModuleOwnerNoteSeverity.Warning)
            .ToArray();
        if (fyi.Length == 0) return;

        var rows = new List<IWidget>();
        foreach (var note in fyi)
        {
            var lines = new List<string> { "[bold]" + Markup.Escape(note.Title) + "[/]" };
            if (!string.IsNullOrWhiteSpace(note.Summary)) lines.Add(Markup.Escape(note.Summary.Trim()));
            if (!string.IsNullOrWhiteSpace(note.WhyItMatters)) lines.Add("[muted]When it matters:[/] " + Markup.Escape(note.WhyItMatters.Trim()));
            if (!string.IsNullOrWhiteSpace(note.NextStep)) lines.Add("[muted]What to do:[/] " + Markup.Escape(note.NextStep.Trim()));
            lines.AddRange((note.Details ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.Trim().Equals(note.Summary?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(line => "[muted]-[/] " + Markup.Escape(line.Trim())));
            rows.Add(new TextBlock(string.Join("\n", lines)));
        }
        summary.Detail("Owner notes", new Rows(rows.ToArray()) { Gap = 1 });
    }

    private static string NormalizeCheckSummary(string? name, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
        var text = summary!.Trim();
        if (!text.Equals("ok", StringComparison.OrdinalIgnoreCase)) return text;
        return string.Equals(name, "Module structure", StringComparison.OrdinalIgnoreCase) ? "Structure verified" : "No issues found";
    }

    private static List<string> BuildFileConsistencyReasons(ProjectConsistencyFileDetail file, FileConsistencySettings? settings)
    {
        var reasons = new List<string>(4);
        if (file.NeedsEncodingConversion)
            reasons.Add($"encoding {file.CurrentEncoding?.ToString() ?? "Unknown"} (expected {file.RecommendedEncoding})");
        if (file.NeedsLineEndingConversion)
            reasons.Add($"line endings {file.CurrentLineEnding} (expected {file.RecommendedLineEnding})");
        if (settings?.CheckMixedLineEndings == true && file.HasMixedLineEndings) reasons.Add("mixed line endings");
        if (settings?.CheckMissingFinalNewline == true && file.MissingFinalNewline) reasons.Add("missing final newline");
        if (!string.IsNullOrWhiteSpace(file.Error)) reasons.Add($"error: {file.Error!.Trim()}");
        return reasons;
    }
}
