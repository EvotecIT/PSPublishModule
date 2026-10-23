namespace PowerForge;

/// <summary>
/// Applies a set of local recovery replacements with pre-created backups and best-effort rollback.
/// </summary>
internal static class RecoveryFileReplacementTransaction
{
    internal static void Apply(
        IReadOnlyList<RecoveryFileRewrite> rewrites,
        CancellationToken cancellationToken,
        Action<string, string, bool>? copyFile = null,
        Action<string, string>? replaceFile = null)
    {
        if (rewrites is null)
            throw new ArgumentNullException(nameof(rewrites));
        copyFile ??= static (source, destination, overwrite) => File.Copy(source, destination, overwrite);
        replaceFile ??= static (source, destination) =>
            File.Replace(source, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);

        try
        {
            for (var index = 0; index < rewrites.Count; index++)
            {
                var rewrite = rewrites[index];
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    copyFile(rewrite.OriginalPath, rewrite.BackupPath, false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw CreateFailure("backup", rewrite, index, rewrites.Count, exception);
                }
                rewrite.BackupCreated = true;
            }

            for (var index = 0; index < rewrites.Count; index++)
            {
                var rewrite = rewrites[index];
                cancellationToken.ThrowIfCancellationRequested();
                var attempts = 0;
                try
                {
                    ReplaceWithRetry(rewrite, replaceFile, cancellationToken, out attempts);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw CreateFailure("replace", rewrite, index, rewrites.Count, exception, attempts);
                }
                rewrite.ReplacementApplied = true;
            }

            foreach (var rewrite in rewrites)
                rewrite.DeleteBackupOnCleanup = true;
        }
        catch
        {
            foreach (var rewrite in rewrites)
            {
                if (!rewrite.BackupCreated)
                    continue;
                if (!rewrite.ReplacementAttempted)
                {
                    rewrite.DeleteBackupOnCleanup = true;
                    continue;
                }

                try
                {
                    copyFile(rewrite.BackupPath, rewrite.OriginalPath, true);
                    rewrite.DeleteBackupOnCleanup = true;
                }
                catch
                {
                    // Keep the only known original payload for operator recovery.
                    rewrite.DeleteBackupOnCleanup = false;
                }
            }
            throw;
        }
    }

    private static void ReplaceWithRetry(
        RecoveryFileRewrite rewrite,
        Action<string, string> replaceFile,
        CancellationToken cancellationToken,
        out int attempts)
    {
        // ERROR_UNABLE_TO_REMOVE_REPLACED retains both original file names.
        // Other ReplaceFile errors can leave a partially renamed transaction;
        // they must reach rollback instead of being retried.
        for (attempts = 1; ; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                rewrite.ReplacementAttempted = true;
                replaceFile(rewrite.ReplacementPath, rewrite.OriginalPath);
                return;
            }
            catch (IOException exception) when (
                exception.HResult == unchecked((int)0x80070497) && attempts < 5)
            {
                if (cancellationToken.WaitHandle.WaitOne(100 * (1 << (attempts - 1))))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static IOException CreateFailure(
        string operation,
        RecoveryFileRewrite rewrite,
        int index,
        int count,
        Exception exception,
        int attempts = 1)
        => new(
            $"Recovery {operation} failed at rewrite {index + 1}/{count} after {attempts} attempt(s): " +
            $"original '{rewrite.OriginalPath}', replacement '{rewrite.ReplacementPath}', " +
            $"backup '{rewrite.BackupPath}'; native error 0x{exception.HResult:X8}. " +
            exception.Message,
            exception);
}

/// <summary>One prepared local replacement in a recovery transaction.</summary>
internal sealed class RecoveryFileRewrite
{
    internal RecoveryFileRewrite(string originalPath, string replacementPath)
    {
        OriginalPath = originalPath;
        ReplacementPath = replacementPath;
        BackupPath = originalPath + ".pre-recovery-" + Guid.NewGuid().ToString("N") + ".bak";
    }

    internal string OriginalPath { get; }

    internal string ReplacementPath { get; }

    internal string BackupPath { get; }

    internal bool BackupCreated { get; set; }

    internal bool ReplacementApplied { get; set; }

    internal bool ReplacementAttempted { get; set; }

    internal bool DeleteBackupOnCleanup { get; set; }
}
