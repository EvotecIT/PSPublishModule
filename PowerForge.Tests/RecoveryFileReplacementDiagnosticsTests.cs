namespace PowerForge.Tests;

public sealed class RecoveryFileReplacementDiagnosticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_IdentifiesFailingOperationAndPathsWithoutLosingNativeException(bool failBackup)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var original = Path.Combine(root.FullName, "package.nupkg");
            var replacement = Path.Combine(root.FullName, "download.tmp");
            File.WriteAllText(original, "original");
            File.WriteAllText(replacement, "published");
            var rewrite = new RecoveryFileRewrite(original, replacement);
            Exception native = failBackup
                ? new UnauthorizedAccessException("Access denied.")
                : new IOException("Unable to remove the file to be replaced.", unchecked((int)0x80070497));
            var replacementAttempts = 0;

            var exception = Assert.Throws<IOException>(() => RecoveryFileReplacementTransaction.Apply(
                [rewrite], CancellationToken.None,
                copyFile: (source, destination, overwrite) =>
                {
                    if (failBackup) throw native;
                    File.Copy(source, destination, overwrite);
                },
                replaceFile: (_, _) => { replacementAttempts++; throw native; }));

            Assert.Same(native, exception.InnerException);
            Assert.Contains(failBackup ? "Recovery backup failed" : "Recovery replace failed", exception.Message);
            Assert.Contains("rewrite 1/1", exception.Message);
            Assert.Contains(original, exception.Message);
            Assert.Contains(replacement, exception.Message);
            Assert.Contains(rewrite.BackupPath, exception.Message);
            Assert.Contains(failBackup ? "0x80070005" : "0x80070497", exception.Message);
            Assert.Equal(failBackup ? 0 : 5, replacementAttempts);
            Assert.Contains(failBackup ? "after 1 attempt" : "after 5 attempt", exception.Message);
            Assert.Equal("original", File.ReadAllText(original));
            Assert.Equal("published", File.ReadAllText(replacement));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Apply_CompletesAfterTransientUnableToRemoveReplacedError()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var original = Path.Combine(root.FullName, "package.nupkg");
            var replacement = original + ".new";
            File.WriteAllText(original, "rebuilt");
            File.WriteAllText(replacement, "published");
            var attempts = 0;
            var rewrite = new RecoveryFileRewrite(original, replacement);
            RecoveryFileReplacementTransaction.Apply([rewrite], CancellationToken.None,
                replaceFile: (source, destination) =>
                {
                    if (++attempts < 3)
                        throw new IOException("Unable to remove the file to be replaced.", unchecked((int)0x80070497));
                    File.Replace(source, destination, null);
                });

            Assert.Equal("published", File.ReadAllText(original));
            Assert.False(File.Exists(replacement));
            Assert.True(rewrite.ReplacementApplied);
            Assert.True(rewrite.DeleteBackupOnCleanup);
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_DoesNotRetryPartiallyRenamedReplacementFailure(bool failRollback)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var original = Path.Combine(root.FullName, "package.nupkg");
            File.WriteAllText(original, "rebuilt");
            File.WriteAllText(original + ".new", "published");
            var attempts = 0;
            var native = new IOException("Unable to move the replacement file.", unchecked((int)0x80070499));
            var rewrite = new RecoveryFileRewrite(original, original + ".new");
            var exception = Assert.Throws<IOException>(() => RecoveryFileReplacementTransaction.Apply(
                [rewrite], CancellationToken.None,
                copyFile: (source, destination, overwrite) =>
                {
                    if (overwrite && failRollback) throw new IOException("Rollback denied.");
                    File.Copy(source, destination, overwrite);
                },
                replaceFile: (_, destination) =>
                {
                    attempts++;
                    File.Move(destination, destination + ".partially-renamed");
                    throw native;
                }));

            Assert.Equal(1, attempts);
            Assert.Same(native, exception.InnerException);
            if (failRollback)
            {
                Assert.False(File.Exists(original));
                Assert.False(rewrite.DeleteBackupOnCleanup);
                Assert.Equal("rebuilt", File.ReadAllText(rewrite.BackupPath));
            }
            else
            {
                Assert.Equal("rebuilt", File.ReadAllText(original));
                Assert.True(rewrite.DeleteBackupOnCleanup);
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Apply_CancelsRetryAndRollsBackPreviousReplacement()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(root.FullName, "first.nupkg");
            var second = Path.Combine(root.FullName, "second.nupkg");
            File.WriteAllText(first, "first-original");
            File.WriteAllText(second, "second-original");
            File.WriteAllText(first + ".new", "published");
            using var cancellation = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => RecoveryFileReplacementTransaction.Apply(
                [new RecoveryFileRewrite(first, first + ".new"), new RecoveryFileRewrite(second, second + ".new")],
                cancellation.Token, replaceFile: (source, destination) =>
                {
                    if (destination == second)
                    {
                        cancellation.Cancel();
                        throw new IOException("Unable to remove the file to be replaced.", unchecked((int)0x80070497));
                    }
                    File.Replace(source, destination, null);
                }));

            Assert.Equal("first-original", File.ReadAllText(first));
            Assert.Equal("second-original", File.ReadAllText(second));
        }
        finally { root.Delete(recursive: true); }
    }
}
