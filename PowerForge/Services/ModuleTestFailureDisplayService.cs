using System;
using System.Collections.Generic;
using System.Globalization;

namespace PowerForge;

internal sealed class ModuleTestFailureDisplayService
{
    public IReadOnlyList<ModuleTestFailureDisplayLine> CreateSummary(ModuleTestFailureAnalysis analysis, bool showSuccessful)
    {
        if (analysis is null)
            throw new ArgumentNullException(nameof(analysis));

        var lines = new List<ModuleTestFailureDisplayLine>
        {
            Line("=== Module Test Results Summary ===", ConsoleColor.Cyan),
            Line($"Source: {analysis.Source}", ConsoleColor.DarkGray),
            Line(string.Empty),
            Line("Test Statistics:", ConsoleColor.Yellow),
            Line($"   Total Tests: {analysis.TotalCount}"),
            Line($"   Passed: {analysis.PassedCount}", ConsoleColor.Green),
            Line($"   Failed: {analysis.FailedCount}", analysis.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green)
        };

        if (analysis.SkippedCount > 0)
            lines.Add(Line($"   Skipped: {analysis.SkippedCount}", ConsoleColor.Yellow));

        if (analysis.TotalCount > 0)
        {
            var rate = Math.Round((double)analysis.PassedCount / analysis.TotalCount * 100, 1);
            var color = rate == 100 ? ConsoleColor.Green : (rate >= 80 ? ConsoleColor.Yellow : ConsoleColor.Red);
            lines.Add(Line($"   Success Rate: {rate.ToString("0.0", CultureInfo.InvariantCulture)}%", color));
        }

        lines.Add(Line(string.Empty));
        if (analysis.FailedCount > 0)
        {
            lines.Add(Line("Failed Tests:", ConsoleColor.Red));
            foreach (var failure in analysis.FailedTests)
                lines.Add(Line($"   - {failure.Name}", ConsoleColor.Red));
            lines.Add(Line(string.Empty));
        }
        else if (showSuccessful && analysis.PassedCount > 0)
        {
            lines.Add(Line("All tests passed successfully!", ConsoleColor.Green));
        }

        return lines;
    }

    public IReadOnlyList<ModuleTestFailureDisplayLine> CreateDetailed(ModuleTestFailureAnalysis analysis)
    {
        if (analysis is null)
            throw new ArgumentNullException(nameof(analysis));

        var lines = new List<ModuleTestFailureDisplayLine>
        {
            Line("=== Module Test Failure Analysis ===", ConsoleColor.Cyan),
            Line($"Source: {analysis.Source}", ConsoleColor.DarkGray),
            Line($"Analysis Time: {analysis.Timestamp}", ConsoleColor.DarkGray),
            Line(string.Empty)
        };

        if (analysis.TotalCount == 0)
        {
            lines.Add(Line("No test results found", ConsoleColor.Yellow));
            return lines;
        }

        var summaryColor = analysis.FailedCount == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        lines.Add(Line($"Summary: {analysis.PassedCount}/{analysis.TotalCount} tests passed", summaryColor));
        lines.Add(Line(string.Empty));

        if (analysis.FailedCount == 0)
        {
            lines.Add(Line("All tests passed successfully!", ConsoleColor.Green));
            return lines;
        }

        lines.Add(Line($"Failed Tests ({analysis.FailedCount}):", ConsoleColor.Red));
        lines.Add(Line(string.Empty));

        foreach (var failure in analysis.FailedTests)
        {
            lines.Add(Line($"- {failure.Name}", ConsoleColor.Red));

            if (!string.IsNullOrWhiteSpace(failure.ErrorMessage) &&
                !string.Equals(failure.ErrorMessage, "No error message available", StringComparison.Ordinal))
            {
                foreach (var text in failure.ErrorMessage.Split(new[] { '\n' }, StringSplitOptions.None))
                {
                    var trimmed = text.Trim();
                    if (trimmed.Length > 0)
                        lines.Add(Line($"   {trimmed}", ConsoleColor.Yellow));
                }
            }

            if (failure.Duration.HasValue)
                lines.Add(Line($"   Duration: {failure.Duration.Value}", ConsoleColor.DarkGray));

            lines.Add(Line(string.Empty));
        }

        lines.Add(Line($"=== Summary: {analysis.FailedCount} test{(analysis.FailedCount != 1 ? "s" : string.Empty)} failed ===", ConsoleColor.Red));
        return lines;
    }

    private static ModuleTestFailureDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
