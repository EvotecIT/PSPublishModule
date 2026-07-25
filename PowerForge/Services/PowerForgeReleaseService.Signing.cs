using System.IO.Compression;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Rebuilds tool archives from output directories that were signed after the unified build,
    /// refreshes staged copies, and rewrites release summary files whose hashes changed.
    /// </summary>
    internal static IReadOnlyList<string> RefreshBuiltToolArchivesAfterSigning(
        PowerForgeReleaseResult result,
        IReadOnlyCollection<string> signedOutputDirectories)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (signedOutputDirectories is null)
            throw new ArgumentNullException(nameof(signedOutputDirectories));

        var signedDirectories = signedOutputDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (signedDirectories.Count == 0)
            return [];

        var archives = (result.Tools?.Artefacts ?? [])
            .Select(static artifact => (
                OutputDirectory: artifact.OutputPath,
                artifact.ZipPath,
                Runtime: artifact.Runtime,
                ExecutablePath: (string?)artifact.ExecutablePath,
                AliasPath: artifact.CommandAliasPath,
                IsLegacy: true))
            .Concat((result.DotNetTools?.Artefacts ?? [])
                .Select(static artifact => (
                    OutputDirectory: artifact.OutputDir,
                    artifact.ZipPath,
                    Runtime: string.Empty,
                    ExecutablePath: (string?)null,
                    AliasPath: (string?)null,
                    IsLegacy: false)))
            .Where(static artifact =>
                !string.IsNullOrWhiteSpace(artifact.OutputDirectory) &&
                !string.IsNullOrWhiteSpace(artifact.ZipPath))
            .Select(static artifact => (
                OutputDirectory: Path.GetFullPath(artifact.OutputDirectory),
                ZipPath: Path.GetFullPath(artifact.ZipPath!),
                artifact.Runtime,
                artifact.ExecutablePath,
                artifact.AliasPath,
                artifact.IsLegacy))
            .Where(artifact => signedDirectories.Contains(artifact.OutputDirectory))
            .GroupBy(static artifact => artifact.ZipPath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var refreshed = new List<string>(archives.Length);
        foreach (var archive in archives)
        {
            if (!Directory.Exists(archive.OutputDirectory))
                throw new DirectoryNotFoundException($"Signed tool output directory was not found: {archive.OutputDirectory}");

            RecreateArchive(archive.OutputDirectory, archive.ZipPath);
            if (archive.IsLegacy)
            {
                PowerForgeToolReleaseService.ApplyArchiveExecutablePermissions(
                    archive.Runtime,
                    archive.OutputDirectory,
                    archive.ZipPath,
                    archive.ExecutablePath,
                    archive.AliasPath);
            }
            refreshed.Add(archive.ZipPath);
            refreshed.AddRange(RefreshStagedCopies(result, archive.ZipPath));
        }

        if (refreshed.Count > 0)
            RewriteReleaseSummaryFiles(result);

        return refreshed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RecreateArchive(string sourceDirectory, string archivePath)
    {
        var archiveDirectory = Path.GetDirectoryName(archivePath)
            ?? throw new InvalidOperationException($"Archive path has no parent directory: {archivePath}");
        Directory.CreateDirectory(archiveDirectory);

        var temporaryArchive = Path.Combine(
            Path.GetTempPath(),
            $"powerforge-signed-{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(
                sourceDirectory,
                temporaryArchive,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            File.Copy(temporaryArchive, archivePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
                File.Delete(temporaryArchive);
        }
    }

    private static IReadOnlyList<string> RefreshStagedCopies(PowerForgeReleaseResult result, string sourcePath)
    {
        var refreshed = new List<string>();
        foreach (var entry in result.ReleaseAssetEntries.Where(entry =>
                     !string.IsNullOrWhiteSpace(entry.Path) &&
                     string.Equals(Path.GetFullPath(entry.Path), sourcePath, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(entry.StagedPath)))
        {
            var stagedPath = Path.GetFullPath(entry.StagedPath!);
            if (string.Equals(stagedPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            File.Copy(sourcePath, stagedPath, overwrite: true);
            refreshed.Add(stagedPath);
        }

        return refreshed;
    }
}
