namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Fact]
    public async Task CaptureBudgetsMainWalRecoveryAndGrowthBeforeWriting()
    {
        var options = ValidOptions();
        File.WriteAllBytes(options.Databases[0].Database, new byte[1024]);
        File.WriteAllBytes(options.Databases[0].Database + "-wal", new byte[2048]);
        var recovery = Path.Combine(_root, "service.json"); File.WriteAllText(recovery, "{}");
        options.RecoveryFiles = [new() { Id = "service", Path = recovery }];
        var estimated = await CaptureCapacity.EstimateSetAsync(options, CancellationToken.None);
        Assert.Equal(1024 * 1024 + CaptureCapacity.WithHeadroom(3072) + 2, estimated);
        Assert.Throws<OverflowException>(() => CaptureCapacity.WithHeadroom(long.MaxValue));
        Assert.Throws<InvalidDataException>(() => CaptureCapacity.WithHeadroom(0));

        foreach (var path in new[] { options.Destination.SshExecutable, options.Destination.IdentityFile, options.Destination.KnownHostsFile })
            File.WriteAllText(path, "synthetic input");
        // Enough for the configured reserve alone, but not the full capture plus that reserve.
        options.MinimumFreeBytes = new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace - 1024 * 1024;
        var stages = new List<string>();
        await Assert.ThrowsAsync<IOException>(() => new DatabaseBackupRunner().RunAsync(options, stages.Add));
        Assert.Equal(["preflight"], stages);
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
        Assert.Equal(1024, new FileInfo(options.Databases[0].Database).Length);
    }

    [Fact]
    public void TransferHeaderIsBoundedIncludingItsProtocolNewline()
    {
        // Exercise the actual serializer with query-count metadata, including the exact line boundary.
        var header = new { validationCounts = Enumerable.Repeat(long.MaxValue, 4000).ToArray() };
        Assert.Throws<InvalidDataException>(() => DatabaseBackupRunner.SerializeTransferHeader(header));
        Assert.Equal(65535, DatabaseBackupRunner.SerializeTransferHeader(new string('x', 65533)).Length);
        Assert.Throws<InvalidDataException>(() => DatabaseBackupRunner.SerializeTransferHeader(new string('x', 65534)));
    }
}
