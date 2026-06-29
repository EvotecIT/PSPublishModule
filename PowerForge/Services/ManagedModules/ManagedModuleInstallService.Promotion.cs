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
            TimeSpan backupCleanupElapsed)
        {
            Elapsed = elapsed;
            HadExistingTarget = hadExistingTarget;
            BackupMoveElapsed = backupMoveElapsed;
            FinalMoveElapsed = finalMoveElapsed;
            BackupCleanupElapsed = backupCleanupElapsed;
        }

        public TimeSpan Elapsed { get; }

        public bool HadExistingTarget { get; }

        public TimeSpan BackupMoveElapsed { get; }

        public TimeSpan FinalMoveElapsed { get; }

        public TimeSpan BackupCleanupElapsed { get; }
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

    private static ManagedModulePromotionResult PromoteStagedModule(string stageModulePath, string modulePath)
    {
        var backupPath = default(string);
        var backupMoveElapsed = TimeSpan.Zero;
        var finalMoveElapsed = TimeSpan.Zero;
        var backupCleanupElapsed = TimeSpan.Zero;
        var hadExistingTarget = false;

        try
        {
            if (Directory.Exists(modulePath))
            {
                hadExistingTarget = true;
                backupPath = Path.Combine(Path.GetTempPath(), "PFMM.B", NewShortId());
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

                var backupMoveStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Directory.Move(modulePath, backupPath);
                backupMoveStopwatch.Stop();
                backupMoveElapsed = backupMoveStopwatch.Elapsed;
            }

            var finalMoveStopwatch = System.Diagnostics.Stopwatch.StartNew();
            Directory.Move(stageModulePath, modulePath);
            finalMoveStopwatch.Stop();
            finalMoveElapsed = finalMoveStopwatch.Elapsed;

            if (backupPath is not null && Directory.Exists(backupPath))
            {
                var cleanupStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Directory.Delete(backupPath, recursive: true);
                cleanupStopwatch.Stop();
                backupCleanupElapsed = cleanupStopwatch.Elapsed;
            }

            return new ManagedModulePromotionResult(
                backupMoveElapsed + finalMoveElapsed + backupCleanupElapsed,
                hadExistingTarget,
                backupMoveElapsed,
                finalMoveElapsed,
                backupCleanupElapsed);
        }
        catch
        {
            RestoreBackup(modulePath, backupPath);
            throw;
        }
    }

    private static void RestoreBackup(string modulePath, string? backupPath)
    {
        if (backupPath is null || !Directory.Exists(backupPath))
            return;

        if (Directory.Exists(modulePath))
            Directory.Delete(modulePath, recursive: true);

        Directory.Move(backupPath, modulePath);
    }

    private static string NewShortId()
        => Guid.NewGuid().ToString("N").Substring(0, 16);
}
