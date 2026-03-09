#pragma warning disable CS1591
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public static class BuildDiagnosticsFactory
{
    public static BuildDiagnostic[] CreatePipelineDiagnostics(
        ProjectConsistencyReport? fileConsistencyReport,
        FileConsistencySettings? fileConsistencySettings,
        ProjectConsistencyReport? projectRootFileConsistencyReport,
        PowerShellCompatibilityReport? compatibilityReport,
        ModuleValidationReport? validationReport)
    {
        var diagnostics = new List<BuildDiagnostic>();

        if (fileConsistencyReport is not null && fileConsistencySettings is not null)
        {
            diagnostics.AddRange(CreateFileConsistencyDiagnostics(
                fileConsistencyReport,
                fileConsistencySettings,
                BuildDiagnosticScope.Staging,
                projectRootFileConsistencyReport));
        }

        if (projectRootFileConsistencyReport is not null && fileConsistencySettings is not null)
        {
            diagnostics.AddRange(CreateFileConsistencyDiagnostics(
                projectRootFileConsistencyReport,
                fileConsistencySettings,
                BuildDiagnosticScope.Project));
        }

        if (compatibilityReport is not null)
            diagnostics.AddRange(CreateCompatibilityDiagnostics(compatibilityReport));

        if (validationReport is not null)
            diagnostics.AddRange(CreateValidationDiagnostics(validationReport));

        return diagnostics.ToArray();
    }

    public static IReadOnlyList<BuildDiagnostic> CreateValidationDiagnostics(ModuleValidationReport report)
    {
        var diagnostics = new List<BuildDiagnostic>();
        if (report is null || report.Checks.Length == 0)
            return diagnostics;

        foreach (var check in report.Checks)
        {
            if (check is null || check.Status == CheckStatus.Pass)
                continue;

            var normalizedName = check.Name?.Trim() ?? string.Empty;
            var severity = ToBuildSeverity(check.Status);

            switch (normalizedName)
            {
                case "Module structure":
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "VALIDATION-STRUCTURE",
                        area: BuildDiagnosticArea.Validation,
                        severity: severity,
                        scope: BuildDiagnosticScope.Project,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: "Fix module structure and exports",
                        details: BuildCheckDetail(check),
                        recommendedAction: "Align the manifest, exported functions, and referenced files with the module layout in source."));
                    break;

                case "Documentation":
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "VALIDATION-DOCS",
                        area: BuildDiagnosticArea.Validation,
                        severity: severity,
                        scope: BuildDiagnosticScope.Project,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: "Complete command documentation",
                        details: BuildCheckDetail(check),
                        recommendedAction: "Add or improve synopsis, descriptions, and examples for the commands flagged by documentation validation."));
                    break;

                case "PSScriptAnalyzer":
                    diagnostics.AddRange(CreateScriptAnalyzerDiagnostics(check, severity));
                    break;

                case "File integrity":
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "VALIDATION-FILE-INTEGRITY",
                        area: BuildDiagnosticArea.Validation,
                        severity: severity,
                        scope: BuildDiagnosticScope.Project,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: "Fix file integrity issues",
                        details: BuildCheckDetail(check),
                        recommendedAction: "Remove trailing whitespace, fix syntax errors, and review banned-command usage in the files listed by the check."));
                    break;

                case "Functionality tests":
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "VALIDATION-TESTS",
                        area: BuildDiagnosticArea.Validation,
                        severity: severity,
                        scope: BuildDiagnosticScope.Project,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: "Fix failing functionality tests",
                        details: BuildCheckDetail(check),
                        recommendedAction: "Review the failing tests and module behavior, then rerun the validation after fixing the underlying defects."));
                    break;

                default:
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "VALIDATION-GENERAL",
                        area: BuildDiagnosticArea.Validation,
                        severity: severity,
                        scope: BuildDiagnosticScope.Project,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: false,
                        summary: $"Review {normalizedName}",
                        details: BuildCheckDetail(check),
                        recommendedAction: "Review the validation check output and correct the underlying module source or configuration."));
                    break;
            }
        }

        return diagnostics;
    }

    public static IReadOnlyList<BuildDiagnostic> CreateFileConsistencyDiagnostics(
        ProjectConsistencyReport report,
        FileConsistencySettings? settings,
        BuildDiagnosticScope scope,
        ProjectConsistencyReport? projectReport = null)
    {
        var diagnostics = new List<BuildDiagnostic>();
        if (report is null)
            return diagnostics;

        var summary = report.Summary;
        var label = scope == BuildDiagnosticScope.Project ? "project" : "staging";
        var autoFixCommand = settings is null
            ? string.Empty
            : $"New-ConfigurationFileConsistency -Enable -AutoFix{(settings.CreateBackups ? string.Empty : " -CreateBackups")}";

        if (scope == BuildDiagnosticScope.Project)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-SOURCE-FIX",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Info,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: settings?.AutoFix == true,
                summary: "Fix repository files",
                details: $"The {label} report reflects issues in source-controlled files.",
                recommendedAction: "Update the listed files in the repository and commit those changes.",
                suggestedCommand: autoFixCommand));
        }
        else
        {
            if (projectReport is not null)
            {
                var stagingPaths = new HashSet<string>(
                    report.ProblematicFiles.Select(static file => file.RelativePath),
                    System.StringComparer.OrdinalIgnoreCase);
                var mirroredCount = projectReport.ProblematicFiles.Count(file => stagingPaths.Contains(file.RelativePath));
                var stagingOnlyCount = stagingPaths.Count - mirroredCount;

                if (mirroredCount > 0)
                {
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "FC-STAGING-MIRRORS-PROJECT",
                        area: BuildDiagnosticArea.FileConsistency,
                        severity: BuildDiagnosticSeverity.Info,
                        scope: scope,
                        owner: BuildDiagnosticOwner.ModuleAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                        canAutoFix: settings?.AutoFix == true,
                        summary: "Staging issues mirror source files",
                        details: $"{mirroredCount} staging file(s) also have issues in the project report.",
                        recommendedAction: "Fix the matching source files in the repository, then rebuild.",
                        suggestedCommand: autoFixCommand));
                }

                if (stagingOnlyCount > 0)
                {
                    diagnostics.Add(new BuildDiagnostic(
                        ruleId: "FC-STAGING-ONLY",
                        area: BuildDiagnosticArea.FileConsistency,
                        severity: BuildDiagnosticSeverity.Warning,
                        scope: scope,
                        owner: BuildDiagnosticOwner.BuildAuthor,
                        remediationKind: BuildDiagnosticRemediationKind.EngineBug,
                        canAutoFix: false,
                        summary: "Staging-only consistency issues",
                        details: $"{stagingOnlyCount} staging file(s) have issues that were not present in the project report.",
                        recommendedAction: "Review build, merge, packaging, or generation steps that produced those files.",
                        generatedBy: "Module pipeline / generated output"));
                }
            }
            else
            {
                diagnostics.Add(new BuildDiagnostic(
                    ruleId: "FC-STAGING-SCOPE-UNKNOWN",
                    area: BuildDiagnosticArea.FileConsistency,
                    severity: BuildDiagnosticSeverity.Info,
                    scope: scope,
                    owner: BuildDiagnosticOwner.BuildAuthor,
                    remediationKind: BuildDiagnosticRemediationKind.ConfigChange,
                    canAutoFix: false,
                    summary: "Determine whether issues come from source or build output",
                    details: "Only staging files were analyzed for consistency.",
                    recommendedAction: "Run file consistency with Scope StagingAndProject to split repository issues from generated output."));
            }
        }

        if (summary.FilesNeedingEncodingConversion > 0)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-ENCODING",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Warning,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: settings?.AutoFix == true ? BuildDiagnosticRemediationKind.AutoFix : BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: settings?.AutoFix == true,
                summary: "Encoding normalization required",
                details: $"{summary.FilesNeedingEncodingConversion} file(s) do not use the expected {summary.RecommendedEncoding} encoding.",
                recommendedAction: $"Resave the affected files as {summary.RecommendedEncoding}.",
                suggestedCommand: autoFixCommand));
        }

        if (summary.FilesNeedingLineEndingConversion > 0)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-LINE-ENDINGS",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Warning,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: settings?.AutoFix == true ? BuildDiagnosticRemediationKind.AutoFix : BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: settings?.AutoFix == true,
                summary: "Line ending normalization required",
                details: $"{summary.FilesNeedingLineEndingConversion} file(s) do not use the expected {summary.RecommendedLineEnding} line endings.",
                recommendedAction: $"Normalize the affected files to {summary.RecommendedLineEnding} line endings.",
                suggestedCommand: autoFixCommand));
        }

        if (settings?.CheckMixedLineEndings == true && summary.FilesWithMixedLineEndings > 0)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-MIXED-LINE-ENDINGS",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Warning,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: settings.AutoFix ? BuildDiagnosticRemediationKind.AutoFix : BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: settings.AutoFix,
                summary: "Mixed line endings detected",
                details: $"{summary.FilesWithMixedLineEndings} file(s) contain mixed line ending styles.",
                recommendedAction: $"Rewrite the affected files with consistent {summary.RecommendedLineEnding} line endings.",
                suggestedCommand: autoFixCommand));
        }

        if (settings?.CheckMissingFinalNewline == true && summary.FilesMissingFinalNewline > 0)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-MISSING-FINAL-NEWLINE",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Warning,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: settings.AutoFix ? BuildDiagnosticRemediationKind.AutoFix : BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: settings.AutoFix,
                summary: "Final newline missing",
                details: $"{summary.FilesMissingFinalNewline} file(s) are missing a trailing newline.",
                recommendedAction: "Add a final newline to each affected file.",
                suggestedCommand: autoFixCommand));
        }

        if (settings is not null && !settings.AutoFix)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-AUTOFIX-AVAILABLE",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Info,
                scope: scope,
                owner: BuildDiagnosticOwner.BuildAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ConfigChange,
                canAutoFix: true,
                summary: "Auto-fix is available for local cleanup",
                details: "This build reported consistency issues without applying automatic fixes.",
                recommendedAction: "Enable New-ConfigurationFileConsistency -AutoFix for local cleanup runs, then disable it in CI if you want review-only enforcement.",
                suggestedCommand: autoFixCommand));
        }

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "FC-REPORT",
                area: BuildDiagnosticArea.FileConsistency,
                severity: BuildDiagnosticSeverity.Info,
                scope: scope,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Review the full report",
                details: "The console table may not show every affected file.",
                recommendedAction: "Open the CSV report for the complete file list and all issue details.",
                sourcePath: report.ExportPath ?? string.Empty));
        }

        return diagnostics;
    }

    public static IReadOnlyList<BuildDiagnostic> CreateCompatibilityDiagnostics(PowerShellCompatibilityReport report)
    {
        var diagnostics = new List<BuildDiagnostic>();
        if (report is null)
            return diagnostics;

        var affectedFiles = report.Files.Where(static file => file.Issues.Length > 0).ToArray();
        if (affectedFiles.Length == 0)
            return diagnostics;

        diagnostics.Add(new BuildDiagnostic(
            ruleId: "COMPAT-REVIEW-FILES",
            area: BuildDiagnosticArea.Compatibility,
            severity: BuildDiagnosticSeverity.Info,
            scope: BuildDiagnosticScope.Project,
            owner: BuildDiagnosticOwner.ModuleAuthor,
            remediationKind: BuildDiagnosticRemediationKind.ManualFix,
            canAutoFix: false,
            summary: "Review affected files",
            details: $"{affectedFiles.Length} file(s) have compatibility findings.",
            recommendedAction: $"Start with the {CountLabel(affectedFiles.Length, "file", "files")} listed below; those are the actual compatibility blockers or warnings."));

        var issueTypes = affectedFiles
            .SelectMany(static file => file.Issues)
            .Select(static issue => issue.Type)
            .Distinct()
            .ToArray();

        if (issueTypes.Contains(PowerShellCompatibilityIssueType.DotNetFramework))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "COMPAT-DOTNET-FRAMEWORK",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: ".NET Framework APIs",
                details: "One or more files reference assemblies that are not reliably available in PowerShell 7.",
                recommendedAction: "Replace .NET Framework-only assemblies or guard them behind edition-specific logic before loading them in PowerShell 7."));
        }

        if (issueTypes.Contains(PowerShellCompatibilityIssueType.Encoding))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "COMPAT-ENCODING",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.AutoFix,
                canAutoFix: true,
                summary: "Encoding",
                details: "One or more files use an encoding that can cause compatibility issues in Windows PowerShell 5.1.",
                recommendedAction: "Resave the flagged files using the expected encoding before validating Windows PowerShell 5.1 compatibility again."));
        }

        if (issueTypes.Contains(PowerShellCompatibilityIssueType.PowerShell7Feature) ||
            issueTypes.Contains(PowerShellCompatibilityIssueType.PowerShell51Feature))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "COMPAT-EDITION-FEATURES",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Edition-specific features",
                details: "One or more files use commands or syntax that differ between Windows PowerShell 5.1 and PowerShell 7.",
                recommendedAction: "Guard version-specific commands or syntax with edition/version checks, or replace them with cross-version alternatives."));
        }

        if (issueTypes.Contains(PowerShellCompatibilityIssueType.PlatformSpecific))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "COMPAT-PLATFORM",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Platform-specific behavior",
                details: "One or more files use commands that differ across Windows/Linux/macOS.",
                recommendedAction: "Add platform checks or switch to a cross-platform alternative."));
        }

        if (!string.IsNullOrWhiteSpace(report.ExportPath))
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "COMPAT-REPORT",
                area: BuildDiagnosticArea.Compatibility,
                severity: BuildDiagnosticSeverity.Info,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Full report",
                details: "The console table may not show every compatibility finding.",
                recommendedAction: "Open the CSV report for the complete findings list and any files not shown in the table below.",
                sourcePath: report.ExportPath ?? string.Empty));
        }

        return diagnostics;
    }

    private static string CountLabel(int count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

    private static IReadOnlyList<BuildDiagnostic> CreateScriptAnalyzerDiagnostics(
        ModuleValidationCheckResult check,
        BuildDiagnosticSeverity severity)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var detail = BuildCheckDetail(check);
        var issues = check.Issues ?? System.Array.Empty<string>();
        var missingTool = issues.Any(static issue =>
            issue.Contains("PSScriptAnalyzer not found", System.StringComparison.OrdinalIgnoreCase));
        var skippedTool = check.Summary?.IndexOf("skipped", System.StringComparison.OrdinalIgnoreCase) >= 0;
        var runnerFailure = issues.Any(static issue =>
            issue.Contains("runner completed without writing the results file", System.StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("PSScriptAnalyzer failed", System.StringComparison.OrdinalIgnoreCase));

        if (missingTool || skippedTool)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "VALIDATION-PSSA-TOOLING",
                area: BuildDiagnosticArea.Validation,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.BuildConfig,
                owner: BuildDiagnosticOwner.BuildAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ConfigChange,
                canAutoFix: false,
                summary: "Install or enable PSScriptAnalyzer",
                details: detail,
                recommendedAction: "Install PSScriptAnalyzer in the build environment or adjust the validation settings if you intentionally want to skip that check.",
                suggestedCommand: "Install-Module PSScriptAnalyzer -Scope CurrentUser"));
        }
        else if (runnerFailure)
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "VALIDATION-PSSA-RUNNER",
                area: BuildDiagnosticArea.Validation,
                severity: severity,
                scope: BuildDiagnosticScope.BuildConfig,
                owner: BuildDiagnosticOwner.BuildAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ConfigChange,
                canAutoFix: false,
                summary: "Investigate the PSScriptAnalyzer runner",
                details: detail,
                recommendedAction: "Review the analyzer runner output and build environment before treating this as a module code issue."));
        }
        else
        {
            diagnostics.Add(new BuildDiagnostic(
                ruleId: "VALIDATION-PSSA-FINDINGS",
                area: BuildDiagnosticArea.Validation,
                severity: severity,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Fix PSScriptAnalyzer findings",
                details: detail,
                recommendedAction: "Apply the PSScriptAnalyzer fixes in source and rerun validation to confirm the issues are gone."));
        }

        return diagnostics;
    }

    private static BuildDiagnosticSeverity ToBuildSeverity(CheckStatus status)
        => status switch
        {
            CheckStatus.Fail => BuildDiagnosticSeverity.Error,
            CheckStatus.Warning => BuildDiagnosticSeverity.Warning,
            _ => BuildDiagnosticSeverity.Info
        };

    private static string BuildCheckDetail(ModuleValidationCheckResult check)
    {
        if (check is null)
            return string.Empty;

        if (check.Issues is { Length: > 0 })
            return string.Join(" | ", check.Issues.Take(3));

        return check.Summary ?? string.Empty;
    }
}
#pragma warning restore CS1591
