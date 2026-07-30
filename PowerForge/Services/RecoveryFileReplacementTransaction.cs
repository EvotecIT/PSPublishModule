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
            foreach (var rewrite in rewrites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                copyFile(rewrite.OriginalPath, rewrite.BackupPath, false);
                rewrite.BackupCreated = true;
            }

            foreach (var rewrite in rewrites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                replaceFile(rewrite.ReplacementPath, rewrite.OriginalPath);
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
                if (!rewrite.ReplacementApplied)
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

    internal bool DeleteBackupOnCleanup { get; set; }
}
