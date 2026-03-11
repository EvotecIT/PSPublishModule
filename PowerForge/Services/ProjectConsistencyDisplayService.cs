using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PowerForge;

internal sealed class ProjectConsistencyDisplayService
{
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
