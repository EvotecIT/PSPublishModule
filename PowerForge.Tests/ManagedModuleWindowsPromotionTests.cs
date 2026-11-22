using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleWindowsPromotionTests
{
    [WindowsTheory]
    [InlineData("release")]
    [InlineData("cancel")]
    [InlineData("competing-install")]
    [InlineData("busy-backup")]
    public async Task InstallAsync_preserves_promotion_ownership_when_the_staged_directory_is_busy(string scenario)
    {
        const string name = "Company.Module";
        const string version = "1.0.0";
        using var feed = new TemporaryDirectory();
        using var root = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        TestPackageFactory.Create(Path.Combine(feed.Path, $"{name}.{version}.nupkg"), name, version,
            files: new Dictionary<string, string>
            {
                [name + ".psd1"] = "@{ ModuleVersion = '1.0.0' }",
                ["payload.txt"] = "new payload"
            });

        var target = Path.Combine(root.Path, name, version);
        var targetFile = Path.Combine(target, "payload.txt");
        var parent = Path.GetDirectoryName(target)!;
        var replace = scenario != "release";
        if (replace)
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, name + ".psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(targetFile, "previous payload");
        }

        FileStream? stagedHandle = null;
        FileStream? backupHandle = null;
        Task? releaseBackup = null;
        var logger = new PromotionRetryLogger(() =>
        {
            if (scenario == "cancel")
            {
                var lockedBackup = File.Open(BackupPayload(parent), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                releaseBackup = ReleaseBackupAsync(lockedBackup);
                cancellation.Cancel();
            }
            if (scenario == "competing-install")
            {
                Directory.CreateDirectory(target);
                File.WriteAllText(targetFile, "competing payload");
            }
            if (scenario == "busy-backup")
                backupHandle = File.Open(BackupPayload(parent), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stagedHandle?.Dispose();
        }, onWarning: () =>
        {
            var receipt = new ManagedModuleReceiptStore().TryReadReceipt(target);
            Assert.NotNull(receipt);
            Assert.Equal(version, receipt.Version);
            Assert.Equal("new payload", File.ReadAllText(targetFile));
        });
        var service = new ManagedModuleInstallService(logger);
        using var promotionGate = ManagedModuleInstallLock.Acquire(root.Path, ".promotion", cancellation.Token);
        var installation = Task.Run(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = name,
            Version = version,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = root.Path,
            Force = replace,
            AllowClobber = true
        }, cancellation.Token));

        try
        {
            stagedHandle = await HoldStagedPayloadAsync(root.Path, version, cancellation.Token);
            promotionGate.Dispose();

            if (scenario == "cancel")
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installation);
                Assert.Equal("previous payload", File.ReadAllText(targetFile));
                Assert.Empty(Directory.EnumerateDirectories(parent, ".pfmm-backup-*"));
            }
            else if (scenario == "competing-install")
            {
                var error = await Assert.ThrowsAsync<IOException>(() => installation);
                Assert.Contains(".pfmm-backup-", error.Message, StringComparison.Ordinal);
                Assert.Equal("competing payload", File.ReadAllText(targetFile));
                Assert.Equal("previous payload", File.ReadAllText(BackupPayload(parent)));
            }
            else
            {
                var result = await installation;
                Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
                Assert.Equal("new payload", File.ReadAllText(targetFile));
                if (scenario == "busy-backup")
                {
                    Assert.True(logger.WarningObserved);
                    Assert.Equal("previous payload", File.ReadAllText(BackupPayload(parent)));
                }
            }

            Assert.True(logger.RetryObserved);
        }
        finally
        {
            cancellation.Cancel();
            stagedHandle?.Dispose();
            backupHandle?.Dispose();
            promotionGate.Dispose();
            // Observe and finish the installer before disposing its fixture directories.
            try { await installation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { }
            if (releaseBackup is not null)
                await releaseBackup;
        }
    }

    private static string BackupPayload(string parent)
        => Path.Combine(Assert.Single(Directory.EnumerateDirectories(parent, ".pfmm-backup-*")), "payload.txt");

    private static async Task ReleaseBackupAsync(FileStream handle)
    {
        using (handle)
            await Task.Delay(75);
    }

    private static async Task<FileStream> HoldStagedPayloadAsync(
        string root, string version, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var stage in Directory.EnumerateDirectories(root, ".pfmm-stage-*"))
            {
                var path = Path.Combine(stage, version, "payload.txt");
                try { return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                catch (IOException) { }
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class PromotionRetryLogger(Action onRetry, Action onWarning) : ILogger
    {
        public bool RetryObserved { get; private set; }
        public bool WarningObserved { get; private set; }
        public bool IsVerbose => true;
        public void Info(string message) { }
        public void Success(string message) { }
        public void Warn(string message)
        {
            WarningObserved = true;
            onWarning();
        }
        public void Error(string message) { }

        public void Verbose(string message)
        {
            if (!message.StartsWith("Managed module directory move blocked (", StringComparison.Ordinal))
                return;
            if (message.Contains(".pfmm-backup-", StringComparison.Ordinal))
                throw new InvalidOperationException("A rollback retry must not depend on the canceled caller's logger.");
            if (RetryObserved || !message.Contains(".pfmm-stage-", StringComparison.Ordinal))
                return;

            RetryObserved = true;
            onRetry();
        }
    }
}
