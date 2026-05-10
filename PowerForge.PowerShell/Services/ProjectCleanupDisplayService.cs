using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PowerForge;

internal sealed class ProjectCleanupDisplayService
{
    public IReadOnlyList<ProjectCleanupDisplayLine> CreateHeader(string projectPath)
    {
        return new[]
        {
            Line($"Processing project cleanup for: {projectPath}", ConsoleColor.Cyan)
        };
    }

    public IReadOnlyList<ProjectCleanupDisplayLine> CreateNoMatchesLines(bool internalMode)
    {
        return new[]
        {
            internalMode
                ? Line("No files or folders found matching the specified criteria.")
                : Line("No files or folders found matching the specified criteria.", ConsoleColor.Yellow)
        };
    }

    public IReadOnlyList<ProjectCleanupDisplayLine> CreateItemLines(ProjectCleanupItemResult item, int current, int total, bool internalMode)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (internalMode)
        {
            return item.Status switch
            {
                ProjectCleanupStatus.WhatIf => new[] { Line($"Would remove: {item.RelativePath}") },
                ProjectCleanupStatus.Removed => new[] { Line($"Removed: {item.RelativePath}") },
                ProjectCleanupStatus.Failed => new[] { Warning($"Failed to remove: {item.RelativePath}") },
                ProjectCleanupStatus.Error => new[] { Warning($"Failed to remove: {item.RelativePath}") },
                _ => Array.Empty<ProjectCleanupDisplayLine>()
            };
        }

        return item.Status switch
        {
            ProjectCleanupStatus.WhatIf => new[] { Line($"  [WOULD REMOVE] {item.RelativePath}", ConsoleColor.Yellow) },
            ProjectCleanupStatus.Removed => new[] { Line($"  [{current}/{total}] [REMOVED] {item.RelativePath}", ConsoleColor.Red) },
            ProjectCleanupStatus.Failed => new[] { Line($"  [{current}/{total}] [FAILED] {item.RelativePath}", ConsoleColor.Red) },
            ProjectCleanupStatus.Error => new[] { Line($"  [{current}/{total}] [ERROR] {item.RelativePath}: {item.Error}", ConsoleColor.Red) },
            _ => Array.Empty<ProjectCleanupDisplayLine>()
        };
    }

    public IReadOnlyList<ProjectCleanupDisplayLine> CreateSummaryLines(ProjectCleanupOutput output, bool isWhatIf, bool internalMode)
    {
        if (output is null)
            throw new ArgumentNullException(nameof(output));

        var summary = output.Summary;
        var totalSizeMb = FormatMegabytes(Math.Round(output.Results.Where(r => r.Type == ProjectCleanupItemType.File).Sum(r => r.Size) / (1024d * 1024d), 2));
        var spaceFreedMb = FormatMegabytes(summary.SpaceFreedMB);
        var lines = new List<ProjectCleanupDisplayLine>();

        if (internalMode)
        {
            lines.Add(Line($"Cleanup Summary: Project path: {summary.ProjectPath}"));
            lines.Add(Line($"Cleanup type: {summary.ProjectType}"));
            lines.Add(Line($"Total items processed: {summary.TotalItems}"));

            if (isWhatIf)
            {
                lines.Add(Line($"Would remove: {summary.TotalItems} items"));
                lines.Add(Line($"Would free: {totalSizeMb} MB"));
            }
            else
            {
                lines.Add(Line($"Successfully removed: {summary.Removed}"));
                lines.Add(Line($"Errors: {summary.Errors}"));
                lines.Add(Line($"Space freed: {spaceFreedMb} MB"));
                if (!string.IsNullOrWhiteSpace(summary.BackupDirectory))
                    lines.Add(Line($"Backups created in: {summary.BackupDirectory}"));
            }

            return lines;
        }

        lines.Add(Line(string.Empty, ConsoleColor.White));
        lines.Add(Line("Cleanup Summary:", ConsoleColor.Cyan));
        lines.Add(Line($"  Project path: {summary.ProjectPath}", ConsoleColor.White));
        lines.Add(Line($"  Cleanup type: {summary.ProjectType}", ConsoleColor.White));
        lines.Add(Line($"  Total items processed: {summary.TotalItems}", ConsoleColor.White));

        if (isWhatIf)
        {
            lines.Add(Line($"  Would remove: {summary.TotalItems} items", ConsoleColor.Yellow));
            lines.Add(Line($"  Would free: {totalSizeMb} MB", ConsoleColor.Yellow));
            lines.Add(Line("Run without -WhatIf to actually remove these items.", ConsoleColor.Cyan));
        }
        else
        {
            lines.Add(Line($"  Successfully removed: {summary.Removed}", ConsoleColor.Green));
            lines.Add(Line($"  Errors: {summary.Errors}", ConsoleColor.Red));
            lines.Add(Line($"  Space freed: {spaceFreedMb} MB", ConsoleColor.Green));
            if (!string.IsNullOrWhiteSpace(summary.BackupDirectory))
                lines.Add(Line($"  Backups created in: {summary.BackupDirectory}", ConsoleColor.Blue));
        }

        return lines;
    }

    private static ProjectCleanupDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };

    private static ProjectCleanupDisplayLine Warning(string text)
        => new() { Text = text, IsWarning = true };

    private static string FormatMegabytes(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
