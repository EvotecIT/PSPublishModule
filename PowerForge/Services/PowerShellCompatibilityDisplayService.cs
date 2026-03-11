using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PowerForge;

internal sealed class PowerShellCompatibilityDisplayService
{
    public IReadOnlyList<PowerShellCompatibilityDisplayLine> CreateHeader(string inputPath, string psEdition, string psVersion)
    {
        return new[]
        {
            Line("🔎 Analyzing PowerShell compatibility...", ConsoleColor.Cyan),
            Line($"📁 Path: {inputPath}", ConsoleColor.White),
            Line($"💻 Current PowerShell: {psEdition} {psVersion}".TrimEnd(), ConsoleColor.White)
        };
    }

    public IReadOnlyList<string> CreateInternalSummaryMessages(PowerShellCompatibilityReport report, bool showDetails, string? exportPath)
    {
        var messages = new List<string>
        {
            $"Found {report.Files.Length} PowerShell files to analyze",
            $"PowerShell Compatibility: {report.Summary.Status} - {report.Summary.Message}"
        };

        if (report.Summary.Recommendations.Length > 0)
        {
            var joined = string.Join("; ", report.Summary.Recommendations.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(joined))
                messages.Add($"Recommendations: {joined}");
        }

        if (showDetails)
        {
            foreach (var file in report.Files)
            {
                if (file.Issues.Length == 0)
                    continue;

                var issueTypes = string.Join(
                    ", ",
                    file.Issues.Select(i => i.Type.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                messages.Add($"Issues in {file.RelativePath}: {issueTypes}");
            }
        }

        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            messages.Add(report.ExportPath is not null
                ? $"Detailed report exported to: {exportPath}"
                : $"Failed to export detailed report to: {exportPath}");
        }

        return messages;
    }

    public IReadOnlyList<PowerShellCompatibilityDisplayLine> CreateSummary(PowerShellCompatibilitySummary summary)
    {
        var lines = new List<PowerShellCompatibilityDisplayLine>
        {
            Line(string.Empty)
        };

        var color = summary.Status switch
        {
            CheckStatus.Pass => ConsoleColor.Green,
            CheckStatus.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        var statusEmoji = summary.Status switch
        {
            CheckStatus.Pass => "✅",
            CheckStatus.Warning => "⚠️",
            _ => "❌"
        };

        lines.Add(Line($"{statusEmoji} Status: {summary.Status}", color));
        lines.Add(Line(summary.Message, ConsoleColor.White));
        lines.Add(Line($"PS 5.1 compatible: {summary.PowerShell51Compatible}/{summary.TotalFiles}", ConsoleColor.White));
        lines.Add(Line($"PS 7 compatible:   {summary.PowerShell7Compatible}/{summary.TotalFiles}", ConsoleColor.White));
        lines.Add(Line(
            $"Cross-compatible: {summary.CrossCompatible}/{summary.TotalFiles} ({summary.CrossCompatibilityPercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
            ConsoleColor.White));

        if (summary.Recommendations.Length > 0)
        {
            lines.Add(Line(string.Empty));
            lines.Add(Line("Recommendations:", ConsoleColor.Cyan));
            foreach (var recommendation in summary.Recommendations.Where(s => !string.IsNullOrWhiteSpace(s)))
                lines.Add(Line($"- {recommendation}", ConsoleColor.White));
        }

        return lines;
    }

    public IReadOnlyList<PowerShellCompatibilityDisplayLine> CreateDetails(PowerShellCompatibilityFileResult[] results)
    {
        var lines = new List<PowerShellCompatibilityDisplayLine>();
        if (results.Length == 0)
            return lines;

        lines.Add(Line(string.Empty));
        lines.Add(Line("Detailed Analysis:", ConsoleColor.Cyan));

        foreach (var result in results)
        {
            lines.Add(Line(string.Empty));
            lines.Add(Line(result.RelativePath, ConsoleColor.White));
            lines.Add(Line(
                $"  PS 5.1: {(result.PowerShell51Compatible ? "Compatible" : "Not compatible")}",
                result.PowerShell51Compatible ? ConsoleColor.Green : ConsoleColor.Red));
            lines.Add(Line(
                $"  PS 7:   {(result.PowerShell7Compatible ? "Compatible" : "Not compatible")}",
                result.PowerShell7Compatible ? ConsoleColor.Green : ConsoleColor.Red));

            if (result.Issues.Length == 0)
                continue;

            lines.Add(Line("  Issues:", ConsoleColor.Yellow));
            foreach (var issue in result.Issues)
            {
                lines.Add(Line($"    - {issue.Type}: {issue.Description}", ConsoleColor.Red));
                if (!string.IsNullOrWhiteSpace(issue.Recommendation))
                    lines.Add(Line($"      - {issue.Recommendation}", ConsoleColor.Cyan));
            }
        }

        return lines;
    }

    public PowerShellCompatibilityDisplayLine CreateExportStatus(string exportPath, bool exportSucceeded)
    {
        return exportSucceeded
            ? Line($"✅ Detailed report exported to: {exportPath}", ConsoleColor.Green)
            : Line($"❌ Failed to export detailed report to: {exportPath}", ConsoleColor.Red);
    }

    public static int CalculatePercent(PowerShellCompatibilityProgress progress)
    {
        if (progress is null)
            throw new ArgumentNullException(nameof(progress));

        return progress.Total == 0
            ? 0
            : (int)Math.Round((progress.Current / (double)progress.Total) * 100.0, 0);
    }

    private static PowerShellCompatibilityDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
