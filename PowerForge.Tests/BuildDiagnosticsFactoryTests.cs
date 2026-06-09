using System;
using System.Linq;
using Xunit;

namespace PowerForge.Tests;

public sealed class BuildDiagnosticsFactoryTests
{
    [Fact]
    public void CreateFileConsistencyDiagnostics_ProducesProjectFixRecommendations()
    {
        var settings = new FileConsistencySettings
        {
            Enable = true,
            AutoFix = false,
            CreateBackups = false,
            CheckMissingFinalNewline = true,
            RequiredEncoding = FileConsistencyEncoding.UTF8BOM,
            RequiredLineEnding = FileConsistencyLineEnding.CRLF
        };

        var report = CreateConsistencyReport(
            relativePath: "Private\\Get-Test.ps1",
            needsEncodingConversion: true,
            missingFinalNewline: true,
            recommendedEncoding: TextEncodingKind.UTF8BOM,
            recommendedLineEnding: FileConsistencyLineEnding.CRLF);

        var diagnostics = BuildDiagnosticsFactory.CreateFileConsistencyDiagnostics(
            report,
            settings,
            BuildDiagnosticScope.Project);

        Assert.Contains(diagnostics, d => d.RuleId == "FC-SOURCE-FIX");
        Assert.Contains(diagnostics, d => d.RuleId == "FC-ENCODING");
        Assert.Contains(diagnostics, d => d.RuleId == "FC-MISSING-FINAL-NEWLINE");
        Assert.Contains(diagnostics, d => d.RuleId == "FC-AUTOFIX-AVAILABLE");
    }

    [Fact]
    public void CreateFileConsistencyDiagnostics_FlagsStagingOnlyIssues()
    {
        var settings = new FileConsistencySettings
        {
            Enable = true,
            AutoFix = true,
            CreateBackups = true,
            CheckMissingFinalNewline = true,
            RequiredEncoding = FileConsistencyEncoding.UTF8BOM,
            RequiredLineEnding = FileConsistencyLineEnding.CRLF
        };

        var staging = CreateConsistencyReport(
            relativePath: "Generated\\Install-Test.ps1",
            missingFinalNewline: true,
            recommendedEncoding: TextEncodingKind.UTF8BOM,
            recommendedLineEnding: FileConsistencyLineEnding.CRLF);
        var project = CreateConsistencyReport(
            relativePath: "Private\\Get-Test.ps1",
            missingFinalNewline: true,
            recommendedEncoding: TextEncodingKind.UTF8BOM,
            recommendedLineEnding: FileConsistencyLineEnding.CRLF);

        var diagnostics = BuildDiagnosticsFactory.CreateFileConsistencyDiagnostics(
            staging,
            settings,
            BuildDiagnosticScope.Staging,
            project);

        var stagingOnly = Assert.Single(diagnostics, d => d.RuleId == "FC-STAGING-ONLY");
        Assert.Equal(BuildDiagnosticOwner.BuildAuthor, stagingOnly.Owner);
        Assert.Equal(BuildDiagnosticRemediationKind.EngineBug, stagingOnly.RemediationKind);
    }

    [Fact]
    public void CreateCompatibilityDiagnostics_ProducesStructuredRecommendations()
    {
        var report = CreateCompatibilityReport(
            new PowerShellCompatibilityIssue(
                PowerShellCompatibilityIssueType.DotNetFramework,
                "System.Web assembly may not be available in PowerShell 7",
                "Use a PowerShell 7 compatible assembly or guard this code path",
                PowerShellCompatibilitySeverity.Medium),
            new PowerShellCompatibilityIssue(
                PowerShellCompatibilityIssueType.Encoding,
                "UTF8 without BOM may cause issues in PowerShell 5.1 with special characters",
                "Save this file as UTF8BOM for Windows PowerShell 5.1",
                PowerShellCompatibilitySeverity.Medium),
            new PowerShellCompatibilityIssue(
                PowerShellCompatibilityIssueType.PowerShell7Feature,
                "Null coalescing operator (??) is PowerShell 7+ only",
                "Use PowerShell 5.1 compatible syntax",
                PowerShellCompatibilitySeverity.High));

        var diagnostics = BuildDiagnosticsFactory.CreateCompatibilityDiagnostics(report);

        Assert.Contains(diagnostics, d => d.RuleId == "COMPAT-REVIEW-FILES");
        Assert.Contains(diagnostics, d => d.RuleId == "COMPAT-DOTNET-FRAMEWORK");
        Assert.Contains(diagnostics, d => d.RuleId == "COMPAT-ENCODING");
        Assert.Contains(diagnostics, d => d.RuleId == "COMPAT-EDITION-FEATURES");
        Assert.Contains(diagnostics, d => d.RuleId == "COMPAT-REPORT");
    }

    [Fact]
    public void CreateValidationDiagnostics_ProducesModuleAuthorRecommendations()
    {
        var report = new ModuleValidationReport(new[]
        {
            new ModuleValidationCheckResult(
                name: "Documentation",
                severity: ValidationSeverity.Warning,
                status: CheckStatus.Warning,
                summary: "synopsis 5/7, description 5/7, examples 4/7",
                issues: new[] { "2 command(s) missing required examples" }),
            new ModuleValidationCheckResult(
                name: "File integrity",
                severity: ValidationSeverity.Error,
                status: CheckStatus.Fail,
                summary: "Checked 12 files, found 2 issues",
                issues: new[] { "[Private\\Get-Test.ps1] syntax errors: 1" })
        });

        var diagnostics = BuildDiagnosticsFactory.CreateValidationDiagnostics(report);

        var docs = Assert.Single(diagnostics, d => d.RuleId == "VALIDATION-DOCS");
        Assert.Equal(BuildDiagnosticOwner.ModuleAuthor, docs.Owner);
        Assert.Equal(BuildDiagnosticRemediationKind.ManualFix, docs.RemediationKind);

        var integrity = Assert.Single(diagnostics, d => d.RuleId == "VALIDATION-FILE-INTEGRITY");
        Assert.Equal(BuildDiagnosticSeverity.Error, integrity.Severity);
    }

    [Fact]
    public void CreateValidationDiagnostics_DetectsMissingPssaAsBuildConfigurationIssue()
    {
        var report = new ModuleValidationReport(new[]
        {
            new ModuleValidationCheckResult(
                name: "PSScriptAnalyzer",
                severity: ValidationSeverity.Warning,
                status: CheckStatus.Warning,
                summary: "missing",
                issues: new[] { "PSScriptAnalyzer not found." })
        });

        var diagnostics = BuildDiagnosticsFactory.CreateValidationDiagnostics(report);

        var pssa = Assert.Single(diagnostics, d => d.RuleId == "VALIDATION-PSSA-TOOLING");
        Assert.Equal(BuildDiagnosticOwner.BuildAuthor, pssa.Owner);
        Assert.Equal(BuildDiagnosticScope.BuildConfig, pssa.Scope);
        Assert.Contains("Install-Module PSScriptAnalyzer", pssa.SuggestedCommand);
    }

    [Fact]
    public void CreatePipelineDiagnostics_AggregatesConfiguredAreas()
    {
        var settings = new FileConsistencySettings
        {
            Enable = true,
            AutoFix = false,
            CreateBackups = true,
            CheckMissingFinalNewline = true,
            RequiredEncoding = FileConsistencyEncoding.UTF8BOM,
            RequiredLineEnding = FileConsistencyLineEnding.CRLF
        };

        var project = CreateConsistencyReport(
            relativePath: "Private\\Get-Test.ps1",
            missingFinalNewline: true,
            recommendedEncoding: TextEncodingKind.UTF8BOM,
            recommendedLineEnding: FileConsistencyLineEnding.CRLF);

        var compatibility = CreateCompatibilityReport(
            new PowerShellCompatibilityIssue(
                PowerShellCompatibilityIssueType.DotNetFramework,
                "System.Web assembly may not be available in PowerShell 7",
                "Use a PowerShell 7 compatible assembly or guard this code path",
                PowerShellCompatibilitySeverity.Medium));

        var validation = new ModuleValidationReport(new[]
        {
            new ModuleValidationCheckResult(
                name: "Documentation",
                severity: ValidationSeverity.Warning,
                status: CheckStatus.Warning,
                summary: "synopsis 5/7, description 5/7, examples 4/7",
                issues: new[] { "2 command(s) missing required examples" })
        });

        var diagnostics = BuildDiagnosticsFactory.CreatePipelineDiagnostics(
            fileConsistencyReport: null,
            fileConsistencySettings: settings,
            projectRootFileConsistencyReport: project,
            compatibilityReport: compatibility,
            validationReport: validation);

        Assert.Contains(diagnostics, d => d.Area == BuildDiagnosticArea.FileConsistency);
        Assert.Contains(diagnostics, d => d.Area == BuildDiagnosticArea.Compatibility);
        Assert.Contains(diagnostics, d => d.Area == BuildDiagnosticArea.Validation);
    }

    [Fact]
    public void CreateBinaryConflictDiagnostics_ProducesBuildWarnings()
    {
        var result = new BinaryConflictDetectionResult(
            powerShellEdition: "Core",
            moduleRoot: @"C:\Repo\TestModule",
            assemblyRootPath: @"C:\Repo\TestModule\Lib\Core",
            assemblyRootRelativePath: @"Lib\Core",
            issues: new[]
            {
                new BinaryConflictDetectionIssue(
                    powerShellEdition: "Core",
                    assemblyName: "Microsoft.Identity.Client",
                    payloadAssemblyFileName: "Microsoft.Identity.Client.dll",
                    payloadAssemblyVersion: "4.79.1.0",
                    installedModuleName: "ExchangeOnlineManagement",
                    installedModuleVersion: "3.7.0",
                    installedAssemblyVersion: "4.61.3.0",
                    installedAssemblyRelativePath: "ExchangeOnlineManagement/3.7.0/bin/Microsoft.Identity.Client.dll",
                    installedAssemblyPath: @"C:\Modules\ExchangeOnlineManagement\3.7.0\bin\Microsoft.Identity.Client.dll",
                    versionComparison: 1)
            },
            summary: "1 conflict across 1 module root");

        var diagnostics = BuildDiagnosticsFactory.CreateBinaryConflictDiagnostics(result);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BUILD-BINARY-CONFLICT", diagnostic.RuleId);
        Assert.Equal(BuildDiagnosticArea.Build, diagnostic.Area);
        Assert.Equal(BuildDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(BuildDiagnosticScope.Staging, diagnostic.Scope);
        Assert.Equal(BuildDiagnosticOwner.ModuleAuthor, diagnostic.Owner);
        Assert.Contains("ExchangeOnlineManagement 3.7.0", diagnostic.Details);
        Assert.Equal("ExchangeOnlineManagement/3.7.0/bin/Microsoft.Identity.Client.dll", diagnostic.SourcePath);
    }

    private static ProjectConsistencyReport CreateConsistencyReport(
        string relativePath,
        bool needsEncodingConversion = false,
        bool needsLineEndingConversion = false,
        bool hasMixedLineEndings = false,
        bool missingFinalNewline = false,
        TextEncodingKind recommendedEncoding = TextEncodingKind.UTF8BOM,
        FileConsistencyLineEnding recommendedLineEnding = FileConsistencyLineEnding.CRLF)
    {
        var file = new ProjectConsistencyFileDetail(
            relativePath: relativePath,
            fullPath: @"C:\Repo\" + relativePath.Replace('/', '\\'),
            extension: ".ps1",
            currentEncoding: needsEncodingConversion ? TextEncodingKind.Ascii : recommendedEncoding,
            currentLineEnding: needsLineEndingConversion ? DetectedLineEndingKind.LF : DetectedLineEndingKind.CRLF,
            hasFinalNewline: !missingFinalNewline,
            recommendedEncoding: recommendedEncoding,
            recommendedLineEnding: recommendedLineEnding,
            needsEncodingConversion: needsEncodingConversion,
            needsLineEndingConversion: needsLineEndingConversion,
            hasMixedLineEndings: hasMixedLineEndings,
            missingFinalNewline: missingFinalNewline,
            size: 42,
            lastModified: DateTime.Now,
            directory: "Private",
            error: null);

        var summary = new ProjectConsistencySummary(
            projectPath: @"C:\Repo",
            projectType: "PowerShell",
            kind: ProjectKind.PowerShell,
            analysisDate: DateTime.Now,
            totalFiles: 1,
            filesCompliant: file.HasIssues ? 0 : 1,
            filesWithIssues: file.HasIssues ? 1 : 0,
            compliancePercentage: file.HasIssues ? 0 : 100,
            currentEncodingDistribution: Array.Empty<ProjectEncodingDistributionItem>(),
            filesNeedingEncodingConversion: needsEncodingConversion ? 1 : 0,
            recommendedEncoding: recommendedEncoding,
            currentLineEndingDistribution: Array.Empty<ProjectLineEndingDistributionItem>(),
            filesNeedingLineEndingConversion: needsLineEndingConversion ? 1 : 0,
            filesWithMixedLineEndings: hasMixedLineEndings ? 1 : 0,
            filesMissingFinalNewline: missingFinalNewline ? 1 : 0,
            recommendedLineEnding: recommendedLineEnding,
            extensionIssues: Array.Empty<ProjectConsistencyExtensionIssue>());

        var lineEndingSummary = new ProjectLineEndingSummary(
            status: file.HasIssues ? CheckStatus.Warning : CheckStatus.Pass,
            projectPath: @"C:\Repo",
            projectType: "PowerShell",
            kind: ProjectKind.PowerShell,
            totalFiles: 1,
            errorFiles: 0,
            mostCommonLineEnding: file.CurrentLineEnding,
            uniqueLineEndings: new[] { file.CurrentLineEnding },
            inconsistentExtensions: Array.Empty<string>(),
            problemFiles: hasMixedLineEndings ? 1 : 0,
            filesMissingFinalNewline: missingFinalNewline ? 1 : 0,
            message: string.Empty,
            recommendations: Array.Empty<string>(),
            distribution: Array.Empty<ProjectLineEndingDistributionItem>(),
            extensionMap: Array.Empty<ProjectLineEndingExtensionDistribution>(),
            analysisDate: DateTime.Now);

        return new ProjectConsistencyReport(
            summary: summary,
            encodingReport: new ProjectEncodingReport(
                new ProjectEncodingSummary(
                    projectPath: @"C:\Repo",
                    projectType: "PowerShell",
                    kind: ProjectKind.PowerShell,
                    totalFiles: 1,
                    errorFiles: 0,
                    mostCommonEncoding: file.CurrentEncoding,
                    uniqueEncodings: file.CurrentEncoding.HasValue ? new[] { file.CurrentEncoding.Value } : Array.Empty<TextEncodingKind>(),
                    inconsistentExtensions: Array.Empty<string>(),
                    distribution: Array.Empty<ProjectEncodingDistributionItem>(),
                    extensionMap: Array.Empty<ProjectEncodingExtensionDistribution>(),
                    analysisDate: DateTime.Now),
                files: null,
                groupedByEncoding: null,
                exportPath: null),
            lineEndingReport: new ProjectLineEndingReport(
                summary: lineEndingSummary,
                files: null,
                problemFiles: hasMixedLineEndings ? new[]
                {
                    new ProjectLineEndingFileDetail(
                        relativePath: file.RelativePath,
                        fullPath: file.FullPath,
                        extension: file.Extension,
                        lineEnding: file.CurrentLineEnding,
                        hasFinalNewline: file.HasFinalNewline,
                        size: file.Size,
                        lastModified: file.LastModified,
                        directory: file.Directory,
                        error: file.Error)
                } : Array.Empty<ProjectLineEndingFileDetail>(),
                groupedByLineEnding: null,
                exportPath: null),
            files: new[] { file },
            problematicFiles: file.HasIssues ? new[] { file } : Array.Empty<ProjectConsistencyFileDetail>(),
            exportPath: @"C:\Repo\Artefacts\FileConsistencyReport.csv");
    }

    private static PowerShellCompatibilityReport CreateCompatibilityReport(params PowerShellCompatibilityIssue[] issues)
    {
        var file = new PowerShellCompatibilityFileResult(
            fullPath: @"C:\Repo\Module.ps1",
            relativePath: "Module.ps1",
            powerShell51Compatible: false,
            powerShell7Compatible: false,
            encoding: TextEncodingKind.UTF8,
            issues: issues);

        var summary = new PowerShellCompatibilitySummary(
            status: CheckStatus.Warning,
            analysisDate: DateTime.Now,
            totalFiles: 1,
            powerShell51Compatible: 0,
            powerShell7Compatible: 0,
            crossCompatible: 0,
            filesWithIssues: 1,
            crossCompatibilityPercentage: 0,
            message: "1 file has compatibility issues, but requirements are met",
            recommendations: issues.Select(i => i.Recommendation).ToArray());

        return new PowerShellCompatibilityReport(
            summary: summary,
            files: new[] { file },
            exportPath: @"C:\Repo\Artefacts\PowerShellCompatibilityReport.csv");
    }
}
