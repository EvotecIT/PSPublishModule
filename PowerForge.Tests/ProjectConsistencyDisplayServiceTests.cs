using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ProjectConsistencyDisplayServiceTests
{
    [Fact]
    public void CreateAnalysisSummary_FormatsIssueBreakdownAndExportDetails()
    {
        var report = CreateReport(
            filesCompliant: 7,
            filesWithIssues: 3,
            compliancePercentage: 70.0,
            filesNeedingEncodingConversion: 2,
            filesNeedingLineEndingConversion: 1,
            filesWithMixedLineEndings: 1,
            filesMissingFinalNewline: 1,
            extensionIssues: new[]
            {
                new ProjectConsistencyExtensionIssue(".ps1", 2, 1, 1, 0, 1),
                new ProjectConsistencyExtensionIssue(".cs", 1, 1, 0, 1, 0)
            });
        var exportPath = Path.Combine(Path.GetTempPath(), $"consistency-{Guid.NewGuid():N}.csv");
        File.WriteAllText(exportPath, "report");

        try
        {
            var lines = new ProjectConsistencyDisplayService().CreateAnalysisSummary(
                rootPath: @"C:\Repo",
                projectType: "Mixed",
                report: report,
                exportPath: exportPath);

            Assert.Contains(lines, line => line.Text == "Analyzing project consistency..." && line.Color == ConsoleColor.Cyan);
            Assert.Contains(lines, line => line.Text == "Project: C:\\Repo");
            Assert.Contains(lines, line => line.Text == "Type: Mixed");
            Assert.Contains(lines, line => line.Text == "  Total files analyzed: 10");
            Assert.Contains(lines, line => line.Text == "  Files compliant with standards: 7 (70.0%)" && line.Color == ConsoleColor.Yellow);
            Assert.Contains(lines, line => line.Text == "  Files needing attention: 3" && line.Color == ConsoleColor.Red);
            Assert.Contains(lines, line => line.Text == "  Files needing encoding conversion: 2" && line.Color == ConsoleColor.Yellow);
            Assert.Contains(lines, line => line.Text == "  Files with mixed line endings: 1" && line.Color == ConsoleColor.Red);
            Assert.Contains(lines, line => line.Text == "Extensions with Issues:" && line.Color == ConsoleColor.Yellow);
            Assert.Contains(lines, line => line.Text == "  .ps1: 2 files");
            Assert.Contains(lines, line => line.Text == $"Detailed report exported to: {exportPath}" && line.Color == ConsoleColor.Green);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [Fact]
    public void CreateSummary_FormatsConversionAndComplianceDetails()
    {
        var report = CreateReport(filesCompliant: 9, filesWithIssues: 1, compliancePercentage: 90.0);
        var encoding = new ProjectConversionResult(4, 3, 1, 0, Array.Empty<FileConversion>());
        var lineEndings = new ProjectConversionResult(2, 1, 0, 1, Array.Empty<FileConversion>());

        var lines = new ProjectConsistencyDisplayService().CreateSummary(
            rootPath: @"C:\Repo",
            report: report,
            encodingResult: encoding,
            lineEndingResult: lineEndings,
            exportPath: null);

        Assert.Contains(lines, line => line.Text == "Project Consistency Conversion" && line.Color == ConsoleColor.Cyan);
        Assert.Contains(lines, line => line.Text == "Project: C:\\Repo");
        Assert.Contains(lines, line => line.Text == "Encoding conversion: 3/4 converted, 1 skipped, 0 errors" && line.Color == ConsoleColor.Green);
        Assert.Contains(lines, line => line.Text == "Line ending conversion: 1/2 converted, 0 skipped, 1 errors" && line.Color == ConsoleColor.Red);
        Assert.Contains(lines, line => line.Text == "  Files compliant: 9 (90.0%)" && line.Color == ConsoleColor.Green);
        Assert.Contains(lines, line => line.Text == "  Files needing attention: 1" && line.Color == ConsoleColor.Red);
    }

    [Fact]
    public void CreateSummary_UsesYellowComplianceBand()
    {
        var report = CreateReport(filesCompliant: 7, filesWithIssues: 3, compliancePercentage: 75.0);

        var lines = new ProjectConsistencyDisplayService().CreateSummary(
            rootPath: @"C:\Repo",
            report: report,
            encodingResult: null,
            lineEndingResult: null,
            exportPath: null);

        Assert.Contains(lines, line => line.Text == "  Files compliant: 7 (75.0%)" && line.Color == ConsoleColor.Yellow);
    }

    private static ProjectConsistencyReport CreateReport(
        int filesCompliant,
        int filesWithIssues,
        double compliancePercentage,
        int filesNeedingEncodingConversion = 0,
        int filesNeedingLineEndingConversion = 0,
        int filesWithMixedLineEndings = 0,
        int filesMissingFinalNewline = 0,
        ProjectConsistencyExtensionIssue[]? extensionIssues = null)
    {
        var summary = new ProjectConsistencySummary(
            projectPath: @"C:\Repo",
            projectType: "Mixed",
            kind: ProjectKind.Mixed,
            analysisDate: DateTime.Now,
            totalFiles: filesCompliant + filesWithIssues,
            filesCompliant: filesCompliant,
            filesWithIssues: filesWithIssues,
            compliancePercentage: compliancePercentage,
            currentEncodingDistribution: Array.Empty<ProjectEncodingDistributionItem>(),
            filesNeedingEncodingConversion: filesNeedingEncodingConversion,
            recommendedEncoding: TextEncodingKind.UTF8BOM,
            currentLineEndingDistribution: Array.Empty<ProjectLineEndingDistributionItem>(),
            filesNeedingLineEndingConversion: filesNeedingLineEndingConversion,
            filesWithMixedLineEndings: filesWithMixedLineEndings,
            filesMissingFinalNewline: filesMissingFinalNewline,
            recommendedLineEnding: FileConsistencyLineEnding.CRLF,
            extensionIssues: extensionIssues ?? Array.Empty<ProjectConsistencyExtensionIssue>());

        return new ProjectConsistencyReport(
            summary,
            new ProjectEncodingReport(
                new ProjectEncodingSummary(
                    projectPath: summary.ProjectPath,
                    projectType: summary.ProjectType,
                    kind: summary.Kind,
                    totalFiles: summary.TotalFiles,
                    errorFiles: 0,
                    mostCommonEncoding: summary.RecommendedEncoding,
                    uniqueEncodings: Array.Empty<TextEncodingKind>(),
                    inconsistentExtensions: Array.Empty<string>(),
                    distribution: Array.Empty<ProjectEncodingDistributionItem>(),
                    extensionMap: Array.Empty<ProjectEncodingExtensionDistribution>(),
                    analysisDate: summary.AnalysisDate),
                files: null,
                groupedByEncoding: null,
                exportPath: null),
            new ProjectLineEndingReport(
                new ProjectLineEndingSummary(
                    status: CheckStatus.Pass,
                    projectPath: summary.ProjectPath,
                    projectType: summary.ProjectType,
                    kind: summary.Kind,
                    totalFiles: summary.TotalFiles,
                    errorFiles: 0,
                    mostCommonLineEnding: DetectedLineEndingKind.CRLF,
                    uniqueLineEndings: Array.Empty<DetectedLineEndingKind>(),
                    inconsistentExtensions: Array.Empty<string>(),
                    problemFiles: summary.FilesWithMixedLineEndings,
                    filesMissingFinalNewline: summary.FilesMissingFinalNewline,
                    message: string.Empty,
                    recommendations: Array.Empty<string>(),
                    distribution: Array.Empty<ProjectLineEndingDistributionItem>(),
                    extensionMap: Array.Empty<ProjectLineEndingExtensionDistribution>(),
                    analysisDate: summary.AnalysisDate),
                files: null,
                problemFiles: Array.Empty<ProjectLineEndingFileDetail>(),
                groupedByLineEnding: null,
                exportPath: null),
            files: null,
            problematicFiles: Array.Empty<ProjectConsistencyFileDetail>(),
            exportPath: null);
    }
}
