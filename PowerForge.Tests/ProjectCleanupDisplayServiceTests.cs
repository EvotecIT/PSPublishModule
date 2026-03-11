using Xunit;

namespace PowerForge.Tests;

public sealed class ProjectCleanupDisplayServiceTests
{
    [Fact]
    public void CreateItemLines_ReturnsInternalWarningForFailure()
    {
        var service = new ProjectCleanupDisplayService();
        var lines = service.CreateItemLines(
            new ProjectCleanupItemResult
            {
                RelativePath = "bin\\App.dll",
                Status = ProjectCleanupStatus.Failed
            },
            current: 1,
            total: 3,
            internalMode: true);

        var line = Assert.Single(lines);
        Assert.Equal("Failed to remove: bin\\App.dll", line.Text);
        Assert.True(line.IsWarning);
    }

    [Fact]
    public void CreateSummaryLines_UsesWhatIfSizeForHostOutput()
    {
        var output = new ProjectCleanupOutput
        {
            Summary = new ProjectCleanupSummary
            {
                ProjectPath = "C:\\Repo",
                ProjectType = "Build",
                TotalItems = 2
            },
            Results =
            [
                new ProjectCleanupItemResult { Type = ProjectCleanupItemType.File, Size = 1048576 },
                new ProjectCleanupItemResult { Type = ProjectCleanupItemType.File, Size = 524288 }
            ]
        };

        var lines = new ProjectCleanupDisplayService().CreateSummaryLines(output, isWhatIf: true, internalMode: false);

        Assert.Contains(lines, line => line.Text == "Cleanup Summary:");
        Assert.Contains(lines, line => line.Text == "  Would remove: 2 items");
        Assert.Contains(lines, line => line.Text == "  Would free: 1.5 MB");
        Assert.Contains(lines, line => line.Text == "Run without -WhatIf to actually remove these items.");
    }

    [Fact]
    public void CreateSummaryLines_IncludesBackupLineForInternalOutput()
    {
        var output = new ProjectCleanupOutput
        {
            Summary = new ProjectCleanupSummary
            {
                ProjectPath = "C:\\Repo",
                ProjectType = "Custom",
                TotalItems = 3,
                Removed = 2,
                Errors = 1,
                SpaceFreedMB = 12.25,
                BackupDirectory = "C:\\Backups"
            }
        };

        var lines = new ProjectCleanupDisplayService().CreateSummaryLines(output, isWhatIf: false, internalMode: true);

        Assert.Contains(lines, line => line.Text == "Cleanup Summary: Project path: C:\\Repo");
        Assert.Contains(lines, line => line.Text == "Successfully removed: 2");
        Assert.Contains(lines, line => line.Text == "Errors: 1");
        Assert.Contains(lines, line => line.Text == "Space freed: 12.25 MB");
        Assert.Contains(lines, line => line.Text == "Backups created in: C:\\Backups");
    }
}
