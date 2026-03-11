using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PowerForge;

internal sealed class ProjectConsistencyDisplayService
{
    public IReadOnlyList<ProjectConsistencyDisplayLine> CreateAnalysisSummary(
        string rootPath,
        string projectType,
        ProjectConsistencyReport report,
        string? exportPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(projectType))
            throw new ArgumentException("Project type is required.", nameof(projectType));
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        var summary = report.Summary;
        var lines = new List<ProjectConsistencyDisplayLine>
        {
            Line("Analyzing project consistency...", ConsoleColor.Cyan),
            Line($"Project: {rootPath}"),
            Line($"Type: {projectType}"),
            Line($"Target encoding: {summary.RecommendedEncoding}"),
            Line($"Target line ending: {summary.RecommendedLineEnding}"),
            Line(string.Empty),
            Line("Project Consistency Summary:", ConsoleColor.Cyan),
            Line($"  Total files analyzed: {summary.TotalFiles}"),
            Line(
                $"  Files compliant with standards: {summary.FilesCompliant} ({summary.CompliancePercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
                ResolveComplianceColor(summary.CompliancePercentage)),
            Line(
                $"  Files needing attention: {summary.FilesWithIssues}",
                summary.FilesWithIssues == 0 ? ConsoleColor.Green : ConsoleColor.Red),
            Line(string.Empty),
            Line("Encoding Issues:", ConsoleColor.Cyan),
            Line(
                $"  Files needing encoding conversion: {summary.FilesNeedingEncodingConversion}",
                summary.FilesNeedingEncodingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow),
            Line($"  Target encoding: {summary.RecommendedEncoding}"),
            Line(string.Empty),
            Line("Line Ending Issues:", ConsoleColor.Cyan),
            Line(
                $"  Files needing line ending conversion: {summary.FilesNeedingLineEndingConversion}",
                summary.FilesNeedingLineEndingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow),
            Line(
                $"  Files with mixed line endings: {summary.FilesWithMixedLineEndings}",
                summary.FilesWithMixedLineEndings == 0 ? ConsoleColor.Green : ConsoleColor.Red),
            Line(
                $"  Files missing final newline: {summary.FilesMissingFinalNewline}",
                summary.FilesMissingFinalNewline == 0 ? ConsoleColor.Green : ConsoleColor.Yellow),
            Line($"  Target line ending: {summary.RecommendedLineEnding}")
        };

        if (summary.ExtensionIssues.Length > 0)
        {
            lines.Add(Line(string.Empty));
            lines.Add(Line("Extensions with Issues:", ConsoleColor.Yellow));
            foreach (var issue in summary.ExtensionIssues.OrderByDescending(i => i.Total))
                lines.Add(Line($"  {issue.Extension}: {issue.Total} files"));
        }

        if (!string.IsNullOrWhiteSpace(exportPath) && File.Exists(exportPath))
        {
            lines.Add(Line(string.Empty));
            lines.Add(Line($"Detailed report exported to: {exportPath}", ConsoleColor.Green));
        }

        return lines;
    }

    public IReadOnlyList<ProjectConsistencyDisplayLine> CreateSummary(
        string rootPath,
        ProjectConsistencyReport report,
        ProjectConversionResult? encodingResult,
        ProjectConversionResult? lineEndingResult,
        string? exportPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        if (report is null)
            throw new ArgumentNullException(nameof(report));

        var summary = report.Summary;
        var lines = new List<ProjectConsistencyDisplayLine>
        {
            Line("Project Consistency Conversion", ConsoleColor.Cyan),
            Line($"Project: {rootPath}"),
            Line($"Target encoding: {summary.RecommendedEncoding}"),
            Line($"Target line ending: {summary.RecommendedLineEnding}")
        };

        if (encodingResult is not null)
        {
            lines.Add(Line(
                $"Encoding conversion: {encodingResult.Converted}/{encodingResult.Total} converted, {encodingResult.Skipped} skipped, {encodingResult.Errors} errors",
                encodingResult.Errors == 0 ? ConsoleColor.Green : ConsoleColor.Red));
        }

        if (lineEndingResult is not null)
        {
            lines.Add(Line(
                $"Line ending conversion: {lineEndingResult.Converted}/{lineEndingResult.Total} converted, {lineEndingResult.Skipped} skipped, {lineEndingResult.Errors} errors",
                lineEndingResult.Errors == 0 ? ConsoleColor.Green : ConsoleColor.Red));
        }

        lines.Add(Line(string.Empty));
        lines.Add(Line("Consistency summary:", ConsoleColor.Cyan));
        lines.Add(Line(
            $"  Files compliant: {summary.FilesCompliant} ({summary.CompliancePercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
            ResolveComplianceColor(summary.CompliancePercentage)));
        lines.Add(Line(
            $"  Files needing attention: {summary.FilesWithIssues}",
            summary.FilesWithIssues == 0 ? ConsoleColor.Green : ConsoleColor.Red));

        if (!string.IsNullOrWhiteSpace(exportPath) && File.Exists(exportPath))
        {
            lines.Add(Line(string.Empty));
            lines.Add(Line($"Detailed report exported to: {exportPath}", ConsoleColor.Green));
        }

        return lines;
    }

    private static ConsoleColor ResolveComplianceColor(double compliancePercentage)
    {
        if (compliancePercentage >= 90)
            return ConsoleColor.Green;
        if (compliancePercentage >= 70)
            return ConsoleColor.Yellow;
        return ConsoleColor.Red;
    }

    private static ProjectConsistencyDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
