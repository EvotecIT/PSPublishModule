using System.Text.Json;

namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Fact]
    public async Task ContendedCaptureRecordsFailureAndPreservesLastSuccess()
    {
        var options = ValidOptions(); Directory.CreateDirectory(options.WorkRoot);
        var success = Path.Combine(_root, "last-success.json");
        File.WriteAllText(options.StatePath, "{\"status\":\"success\"}");
        File.WriteAllText(success, "preserve completed capture");
        using var worker = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => new DatabaseBackupRunner().RunAsync(options));
        using var result = JsonDocument.Parse(File.ReadAllText(options.StatePath));
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("preflight", result.RootElement.GetProperty("stage").GetString());
        Assert.Equal("preserve completed capture", File.ReadAllText(success));
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagingCreationFailureRecordsTheAttemptWithoutTouchingTheObstruction(bool restore)
    {
        var options = ValidOptions(); File.WriteAllText(options.WorkRoot, "preserve existing file");
        var selected = Path.Combine(_root, "download"); Directory.CreateDirectory(selected);
        var status = restore ? Path.Combine(_root, "restore-drill.json") : options.StatePath;
        var attempt = restore ? new RestoreDrill().VerifyAsync(options, selected, new string('0', 64)) : new DatabaseBackupRunner().RunAsync(options);
        await Assert.ThrowsAsync<IOException>(() => attempt);
        using var result = JsonDocument.Parse(File.ReadAllText(status));
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("preserve existing file", File.ReadAllText(options.WorkRoot));
    }

    [Fact]
    public async Task ContendedRestoreRecordsTheFailedAttemptWithoutChangingPriorRecoveryProof()
    {
        var options = ValidOptions();
        var selected = Path.Combine(_root, "download"); Directory.CreateDirectory(selected);
        Directory.CreateDirectory(options.WorkRoot);
        var status = Path.Combine(_root, "restore-drill.json");
        var success = Path.Combine(_root, "restore-drill-success.json");
        File.WriteAllText(status, "{\"healthy\":true}");
        File.WriteAllText(success, "preserve prior completed proof");
        using var worker = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => new RestoreDrill().VerifyAsync(options, selected, new string('0', 64)));
        using var result = JsonDocument.Parse(File.ReadAllText(status));
        Assert.False(result.RootElement.GetProperty("healthy").GetBoolean());
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("IOException", result.RootElement.GetProperty("errorType").GetString());
        Assert.Equal("preserve prior completed proof", File.ReadAllText(success));
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
    }

    [Fact]
    public async Task UnmarkedInterruptedCaptureIsPreservedAndReportedAsHeld()
    {
        var options = ValidOptions(); options.MinimumFreeBytes = long.MaxValue;
        var interrupted = Path.Combine(options.WorkRoot, "20260101T000000Z-01234567");
        Directory.CreateDirectory(interrupted);
        File.WriteAllText(Path.Combine(interrupted, "valuable"), "preserve unrecognized contents");
        await Assert.ThrowsAsync<IOException>(() => new DatabaseBackupRunner().RunAsync(options));
        using var result = JsonDocument.Parse(File.ReadAllText(options.StatePath));
        Assert.Equal(interrupted, Assert.Single(result.RootElement.GetProperty("heldStagingPaths").EnumerateArray()).GetString());
        Assert.Equal("preserve unrecognized contents", File.ReadAllText(Path.Combine(interrupted, "valuable")));
    }
}
