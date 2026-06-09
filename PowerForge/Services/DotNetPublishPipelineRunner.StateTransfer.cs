using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const string DefaultStateStoragePathTemplate =
        "Artifacts/DotNetPublish/State/{target}/{rid}/{framework}/{style}";

    private DotNetPublishStateTransferResult? PreserveStateBeforePublish(
        DotNetPublishPlan plan,
        string outputDir,
        DotNetPublishStatePreservationOptions? state,
        IReadOnlyDictionary<string, string> tokens,
        string contextLabel)
    {
        if (state is null || !state.Enabled)
            return null;

        var rules = state.Rules ?? Array.Empty<DotNetPublishStateRule>();
        if (rules.Length == 0)
            throw new InvalidOperationException("State preservation is enabled but no rules were configured.");

        var storageTemplate = string.IsNullOrWhiteSpace(state.StoragePath)
            ? DefaultStateStoragePathTemplate
            : state.StoragePath!;
        var storagePath = ResolvePath(plan.ProjectRoot, ApplyTemplate(storageTemplate, tokens));
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, storagePath, "State storage path");

        if (state.ClearStorage && Directory.Exists(storagePath))
        {
            try { Directory.Delete(storagePath, recursive: true); }
            catch { /* best effort */ }
        }
        Directory.CreateDirectory(storagePath);

        var result = new DotNetPublishStateTransferResult
        {
            StoragePath = storagePath,
            OnRestoreFailure = state.OnRestoreFailure,
            Entries = rules
                .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.SourcePath))
                .Select(r => new DotNetPublishStateTransferEntry
                {
                    SourcePath = r.SourcePath.Trim(),
                    DestinationPath = string.IsNullOrWhiteSpace(r.DestinationPath) ? r.SourcePath.Trim() : r.DestinationPath!.Trim(),
                    Overwrite = r.Overwrite
                })
                .ToArray()
        };

        if (!Directory.Exists(outputDir))
        {
            _logger.Info($"State preserve skipped for {contextLabel}: output directory does not exist yet.");
            return result;
        }

        foreach (var entry in result.Entries)
        {
            var sourcePath = ResolvePath(outputDir, entry.SourcePath);
            var storageEntryPath = ResolvePath(storagePath, entry.DestinationPath);
            EnsurePathWithinRoot(outputDir, sourcePath, $"State source '{entry.SourcePath}'");
            EnsurePathWithinRoot(storagePath, storageEntryPath, $"State storage destination '{entry.DestinationPath}'");

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                HandlePolicy(
                    state.OnMissingSource,
                    $"State source was not found for {contextLabel}: {sourcePath}");
                continue;
            }

            entry.PreservedFiles = CopyPathWithMode(sourcePath, storageEntryPath, overwrite: true);
            result.PreservedFiles += entry.PreservedFiles;
        }

        _logger.Info(
            $"State preserve for {contextLabel} -> {storagePath} ({result.PreservedFiles} file(s) preserved).");
        return result;
    }

    private void RestorePreservedState(string outputDir, DotNetPublishStateTransferResult? state)
    {
        if (state is null)
            return;

        if (!Directory.Exists(state.StoragePath))
            return;

        foreach (var entry in state.Entries ?? Array.Empty<DotNetPublishStateTransferEntry>())
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.DestinationPath))
                continue;

            var sourcePath = ResolvePath(state.StoragePath, entry.DestinationPath);
            var destinationPath = ResolvePath(outputDir, entry.DestinationPath);
            EnsurePathWithinRoot(state.StoragePath, sourcePath, $"State restore source '{entry.DestinationPath}'");
            EnsurePathWithinRoot(outputDir, destinationPath, $"State restore destination '{entry.DestinationPath}'");

            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                continue;

            try
            {
                entry.RestoredFiles = CopyPathWithMode(sourcePath, destinationPath, entry.Overwrite);
                state.RestoredFiles += entry.RestoredFiles;
            }
            catch (Exception ex)
            {
                HandlePolicy(
                    state.OnRestoreFailure,
                    $"State restore failed for '{entry.DestinationPath}'. {ex.Message}");
            }
        }

        _logger.Info(
            $"State restore completed from {state.StoragePath} ({state.RestoredFiles} file(s) restored).");
    }

    private static int CopyPathWithMode(string sourcePath, string destinationPath, bool overwrite)
    {
        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (!overwrite && File.Exists(destinationPath))
                return 0;
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return 1;
        }

        if (!Directory.Exists(sourcePath))
            return 0;

        Directory.CreateDirectory(destinationPath);
        var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToArray();
        var copied = 0;
        foreach (var file in files)
        {
            var relative = GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (!overwrite && File.Exists(destinationFile))
                continue;

            File.Copy(file, destinationFile, overwrite: true);
            copied++;
        }

        return copied;
    }
}
