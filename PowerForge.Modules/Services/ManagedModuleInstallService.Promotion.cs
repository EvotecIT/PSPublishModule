namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    private readonly struct ManagedModulePromotionResult
    {
        public ManagedModulePromotionResult(
            TimeSpan elapsed,
            bool hadExistingTarget,
            TimeSpan backupMoveElapsed,
            TimeSpan finalMoveElapsed,
            TimeSpan backupCleanupElapsed,
            string? backupCleanupWarning)
        {
            Elapsed = elapsed;
            HadExistingTarget = hadExistingTarget;
            BackupMoveElapsed = backupMoveElapsed;
            FinalMoveElapsed = finalMoveElapsed;
            BackupCleanupElapsed = backupCleanupElapsed;
            BackupCleanupWarning = backupCleanupWarning;
        }

        public TimeSpan Elapsed { get; }

        public bool HadExistingTarget { get; }

        public TimeSpan BackupMoveElapsed { get; }

        public TimeSpan FinalMoveElapsed { get; }

        public TimeSpan BackupCleanupElapsed { get; }
        public string? BackupCleanupWarning { get; }
    }

    private static void CleanupEmptyStage(string stageRoot)
    {
        try
        {
            if (!Directory.Exists(stageRoot))
                return;

            foreach (var directory in Directory.EnumerateDirectories(stageRoot, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static path => path.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<ManagedModulePromotionResult> PromoteStagedModuleAsync(
        string stageModulePath,
        string modulePath,
        CancellationToken cancellationToken)
    {
        var backupPath = default(string);
        var backupMoveElapsed = TimeSpan.Zero;
        var finalMoveElapsed = TimeSpan.Zero;
        var backupCleanupElapsed = TimeSpan.Zero;
        string? backupCleanupWarning = null;
        var hadExistingTarget = false;

        try
        {
            if (Directory.Exists(modulePath))
            {
                hadExistingTarget = true;
                var candidateBackupPath = CreateBackupPath(modulePath);
                Directory.CreateDirectory(Path.GetDirectoryName(candidateBackupPath)!);

                var backupMoveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await MovePromotionDirectoryAsync(modulePath, candidateBackupPath, cancellationToken).ConfigureAwait(false);
                backupPath = candidateBackupPath;
                backupMoveStopwatch.Stop();
                backupMoveElapsed = backupMoveStopwatch.Elapsed;
            }

            var finalMoveStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await MovePromotionDirectoryAsync(stageModulePath, modulePath, cancellationToken).ConfigureAwait(false);
            finalMoveStopwatch.Stop();
            finalMoveElapsed = finalMoveStopwatch.Elapsed;
        }
        catch (Exception promotionError)
        {
            if (backupPath is not null)
                await RestoreBackupAsync(modulePath, backupPath, promotionError).ConfigureAwait(false);
            throw;
        }

        // The staged directory is committed. A busy backup must not cause a
        // rollback after cleanup may already have removed part of it.
        if (backupPath is not null && Directory.Exists(backupPath))
        {
            var cleanupStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Directory.Delete(backupPath, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                backupCleanupWarning = $"Installed module at '{modulePath}', but could not finish removing its backup at '{backupPath}': {exception.Message}";
            }
            backupCleanupElapsed = cleanupStopwatch.Elapsed;
        }

        return new ManagedModulePromotionResult(
            backupMoveElapsed + finalMoveElapsed + backupCleanupElapsed,
            hadExistingTarget,
            backupMoveElapsed,
            finalMoveElapsed,
            backupCleanupElapsed,
            backupCleanupWarning);
    }

    private async Task MovePromotionDirectoryAsync(
        string source, string destination, CancellationToken cancellationToken, bool reportRetry = true)
    {
        const int InitialDelayMs = 15;
        const int MaximumRetryDelayMs = 240;
        for (var delay = InitialDelayMs; ; delay *= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when (delay <= MaximumRetryDelayMs &&
                                               IsWindowsDirectorySharingError(exception) &&
                                               Directory.Exists(source) &&
                                               !Directory.Exists(destination) && !File.Exists(destination))
            {
                // Windows directory renames can report access denied when a
                // descendant is temporarily open without delete sharing.
                if (reportRetry)
                    _logger.Verbose($"Managed module directory move blocked (0x{exception.HResult:X8}): '{source}'. Retrying in {delay} ms.");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsWindowsDirectorySharingError(Exception exception)
        => FrameworkCompatibility.IsWindows() && (exception is IOException or UnauthorizedAccessException) &&
           (exception.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021));

    private static IDisposable AcquirePromotionLock(
        string moduleRoot,
        CancellationToken cancellationToken,
        out TimeSpan waitElapsed)
        => AcquireInstallLock(moduleRoot, ".promotion", cancellationToken, out waitElapsed);

    private async Task RestoreBackupAsync(string modulePath, string backupPath, Exception promotionError)
    {
        try
        {
            // The final rename failed, so a directory at the destination is
            // someone else's. Directory.Move fails without replacing it.
            await MovePromotionDirectoryAsync(backupPath, modulePath, CancellationToken.None, reportRetry: false).ConfigureAwait(false);
        }
        catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Module promotion failed at '{modulePath}' and its previous version could not be restored from '{backupPath}'.",
                new AggregateException(promotionError, rollbackError));
        }
    }

    private static string CreateBackupPath(string modulePath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(modulePath))
                     ?? throw new InvalidOperationException($"Unable to resolve module parent for '{modulePath}'.");
        return Path.Combine(parent, ".pfmm-backup-" + NewShortId());
    }

    private static string NewShortId()
        => Guid.NewGuid().ToString("N").Substring(0, 16);
}
