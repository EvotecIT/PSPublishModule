using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleMonster.Frames;
using ConsoleMonster.Text;
using PowerForge;

namespace PowerForge.ConsoleUi;

/// <summary>The module build summary: outcome, check meters, findings that need attention and the build details.</summary>
internal static partial class ModulePipelineSummary
{
    private static string CountLabel(int count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

    public static void Write(ModulePipelineResult res)
    {
        if (res is null) return;

        var plan = res.Plan;
        var versionText = BuildServices.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease);
        var publishSummary = PipelinePublishSummaryBuilder.Build(res);
        var summary = new SummaryFrame($"{plan.ModuleName} {versionText}", success: true, outcome: "built")
            .Fact(res.BuildResult.StagingPath);

        var checks = new List<CheckStatus>();
        void Check(string area, CheckStatus status, string detail)
        {
            checks.Add(status);
            summary.LineMarkup(area, SummaryFrame.Word(status) + " [muted]" + Markup.Escape(detail) + "[/]");
            if (status != CheckStatus.Pass) summary.Attention.Add(SummaryFrame.From(status), detail, area);
        }
        void Disabled(string area) => summary.LineMarkup(area, "[muted]Disabled[/]");

        AddFileConsistency(res, Check, Disabled);

        if (res.CompatibilityReport is not null && plan.CompatibilitySettings?.Severity != ValidationSeverity.Off)
        {
            var s = res.CompatibilityReport.Summary;
            Check("Compatibility", s.Status, s.Status == CheckStatus.Pass
                ? $"{s.CrossCompatibilityPercentage:0.0}% cross-compatible"
                : $"{s.CrossCompatibilityPercentage:0.0}% cross-compatible, {CountLabel(s.FilesWithIssues, "file", "files")} with issues");
        }
        else Disabled("Compatibility");

        if (res.ValidationReport is not null) Check("Module validation", res.ValidationReport.Status, res.ValidationReport.Summary);
        else Disabled("Module validation");

        if (plan.Formatting is not null)
        {
            var staging = FormattingSummary.FromResults(res.FormattingStagingResults);
            var status = staging.Status;
            var parts = new List<string> { FormattingSummary.FormatPartPlain("staging", staging) };
            if (plan.Formatting.Options.UpdateProjectRoot)
            {
                var project = FormattingSummary.FromResults(res.FormattingProjectResults);
                status = FormattingSummary.Worst(status, project.Status);
                parts.Add(FormattingSummary.FormatPartPlain("project", project));
            }
            Check("Formatting", status, string.Join(", ", parts));
        }
        else Disabled("Formatting");

        if (!plan.SignModule) Disabled("Signing");
        else if (res.SigningResult is null) summary.LineMarkup("Signing", "[warn]Enabled[/]");
        else
        {
            var s = res.SigningResult;
            var detail = $"signed {s.SignedTotal}, new {s.SignedNew}, re-signed {s.Resigned}, already {s.AlreadySignedOther} 3p, already {s.AlreadySignedByThisCert} ours";
            if (s.Failed > 0) detail += $", failed {s.Failed}";
            Check("Signing", s.Success ? CheckStatus.Pass : CheckStatus.Fail, detail);
        }

        summary.Line("Artefacts", res.ArtefactResults is { Length: > 0 } ? res.ArtefactResults.Length.ToString() : "None");
        if (publishSummary.Rows.Count > 0)
        {
            summary.Line("Publish", $"{publishSummary.Rows.Count} result(s) across {publishSummary.ChannelCount} channel(s)");
            foreach (var channel in publishSummary.Channels) summary.Line(channel.Channel, channel.Label);
        }
        else Disabled("Publish");

        AddDiagnostics(res, summary);
        if (res.InstallResult is not null) summary.Line("Install", res.InstallResult.Version);
        else Disabled("Install");

        var passing = checks.Count(status => status == CheckStatus.Pass);
        var warning = checks.Count(status => status == CheckStatus.Warning);
        if (checks.Count > 0) summary.Meter(Meter.Composition("Checks passed", passing, warning, checks.Count - passing - warning));
        summary.Meter(Meter.Count("Artefacts", res.ArtefactResults?.Length ?? 0));
        summary.Meter(Meter.Count("Published", publishSummary.Rows.Count));

        AddRecommendations(res, summary);
        AddDetails(res, publishSummary, summary);
        summary.Write();
    }

    public static void WriteFailure(ModulePipelinePlan plan, Exception error)
    {
        if (plan is null || error is null) return;

        var summary = new SummaryFrame(
                $"{plan.ModuleName} {BuildServices.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease)}",
                success: false)
            .Fact(plan.ProjectRoot)
            .Line("Project", plan.ProjectRoot)
            .Line("Staging", string.IsNullOrWhiteSpace(plan.BuildSpec.StagingPath) ? "(temp)" : plan.BuildSpec.StagingPath);

        if (error is ModuleSigningException { Result: not null } signing)
        {
            var s = signing.Result;
            var detail = $"signed {s.SignedTotal}, already {s.AlreadySignedOther} 3p/{s.AlreadySignedByThisCert} ours, failed {s.Failed}";
            summary.LineMarkup("Signing", "[fail]Fail[/] [muted]" + Markup.Escape(detail) + "[/]");
            summary.Attention.Add(HealthState.Failing, detail, "Signing");
        }

        var hint = BuildHint(error.GetBaseException().Message ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(hint))
            summary.Attention.Add(HealthState.Warning, hint, "Hint");

        summary.Write(error);
    }

    private static void AddFileConsistency(
        ModulePipelineResult res,
        Action<string, CheckStatus, string> check,
        Action<string> disabled)
    {
        var settings = res.Plan.FileConsistencySettings;
        if (settings?.Enable != true || settings.Severity == ValidationSeverity.Off)
        {
            disabled("File consistency");
            return;
        }

        void Report(string area, ProjectConsistencyReport? report, CheckStatus? status)
        {
            if (report is null)
            {
                disabled(area);
                return;
            }

            var total = report.Summary.TotalFiles;
            var issues = CountIssues(report, settings);
            var compliance = total <= 0 ? 100.0 : Math.Round((total - issues) / (double)total * 100.0, 1);
            check(area, status ?? CheckStatus.Warning,
                $"{CountLabel(total, "file", "files")} checked, {CountLabel(issues, "issue", "issues")} ({compliance:0.0}% compliant)");
        }

        var scope = settings.ResolveScope();
        if (scope != FileConsistencyScope.ProjectOnly) Report("File consistency", res.FileConsistencyReport, res.FileConsistencyStatus);
        if (scope != FileConsistencyScope.StagingOnly) Report("File consistency (project)", res.ProjectRootFileConsistencyReport, res.ProjectRootFileConsistencyStatus);
    }

    private static void AddDiagnostics(ModulePipelineResult res, SummaryFrame summary)
    {
        if (res.DiagnosticsBaseline is { } baseline)
            summary.Line("Diagnostics baseline", baseline.BaselineLoaded
                ? $"known {baseline.BaselineDiagnosticCount}, new {baseline.NewDiagnosticCount}, resolved {baseline.ResolvedDiagnosticCount}"
                : $"current {baseline.CurrentDiagnosticCount}");

        if (res.DiagnosticsPolicy is { } policy)
        {
            var parts = new List<string>();
            if (policy.FailOnNewDiagnostics) parts.Add($"new {policy.NewDiagnosticCount}");
            if (policy.FailOnSeverity.HasValue) parts.Add($"{policy.FailOnSeverity.Value}+ {policy.SeverityDiagnosticCount}");
            var detail = parts.Count == 0 ? "configured" : string.Join(", ", parts);
            summary.LineMarkup("Diagnostics policy", (policy.PolicyViolated ? "[fail]Fail[/]" : "[pass]Pass[/]") + " [muted]" + Markup.Escape(detail) + "[/]");
            if (policy.PolicyViolated)
                summary.Attention.Add(HealthState.Failing, policy.FailureReason ?? "Diagnostics policy violated", "Diagnostics policy");
        }

        if (res.Diagnostics is { Length: > 0 } diagnostics)
        {
            var parts = new List<string>();
            void Count(BuildDiagnosticSeverity severity, string word)
            {
                var count = diagnostics.Count(diagnostic => diagnostic.Severity == severity);
                if (count > 0) parts.Add($"{count} {word}");
            }
            Count(BuildDiagnosticSeverity.Info, "info");
            Count(BuildDiagnosticSeverity.Warning, "warning");
            Count(BuildDiagnosticSeverity.Error, "error");
            summary.Line("Diagnostics", parts.Count == 0 ? "none" : string.Join(", ", parts));
        }

        if (res.OwnerNotes is { Length: > 0 } notes)
        {
            var actionable = notes.Count(note => note is not null && note.Severity == ModuleOwnerNoteSeverity.Warning);
            var fyi = notes.Count(note => note is not null && note.Severity != ModuleOwnerNoteSeverity.Warning);
            var parts = new List<string>();
            if (actionable > 0) parts.Add($"[warn]{actionable} action needed[/]");
            if (fyi > 0) parts.Add($"[info]{fyi} FYI[/]");
            summary.LineMarkup("Owner notes", parts.Count == 0 ? "[muted]None[/]" : string.Join(", ", parts));
            foreach (var note in notes.Where(note => note is not null && note.Severity == ModuleOwnerNoteSeverity.Warning))
                summary.Attention.Add(HealthState.Warning, note.Summary ?? note.Title, note.Title);
        }
    }

    // Recommendations become findings: the diagnostic summary is the reason, its location the identity.
    private static void AddRecommendations(ModulePipelineResult res, SummaryFrame summary)
    {
        if (res.Diagnostics is not { Length: > 0 } diagnostics) return;
        foreach (var diagnostic in diagnostics.Where(diagnostic => diagnostic is not null && diagnostic.Severity != BuildDiagnosticSeverity.Info))
        {
            var state = diagnostic.Severity == BuildDiagnosticSeverity.Error ? HealthState.Failing : HealthState.Warning;
            summary.Attention.Add(state, diagnostic.Summary, string.IsNullOrWhiteSpace(diagnostic.SourcePath) ? diagnostic.Area.ToString() : diagnostic.SourcePath!);
        }
    }

    private static int CountIssues(ProjectConsistencyReport report, FileConsistencySettings? settings)
        => report.ProblematicFiles.Count(file =>
            file.NeedsEncodingConversion || file.NeedsLineEndingConversion ||
            (settings is null) ||
            (settings.CheckMissingFinalNewline && file.MissingFinalNewline) ||
            (settings.CheckMixedLineEndings && file.HasMixedLineEndings));

    private static string BuildHint(string message)
    {
        if (message.IndexOf("Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Verify a repository is registered and reachable (Get-PSRepository). If PSGallery is missing, run Register-PSRepository -Default.";

        if (message.IndexOf("manifest contains one or more members that are not valid", StringComparison.OrdinalIgnoreCase) >= 0)
            return "The module manifest contains an unsupported top-level key. Move prerelease/package metadata under PrivateData.PSData.";

        return string.Empty;
    }
}
