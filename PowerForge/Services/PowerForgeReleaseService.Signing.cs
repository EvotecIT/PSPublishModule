using System.IO.Compression;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Rebuilds tool and module archives from output directories that were signed after the unified build,
    /// refreshes staged copies, and rewrites release summary files whose hashes changed.
    /// </summary>
    internal static IReadOnlyList<string> RefreshBuiltArchivesAfterSigning(
        PowerForgeReleaseResult result,
        IReadOnlyCollection<string> signedOutputDirectories,
        IReadOnlyCollection<string>? signedFiles = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (signedOutputDirectories is null)
            throw new ArgumentNullException(nameof(signedOutputDirectories));

        var signedDirectories = signedOutputDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signedFilePaths = (signedFiles ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (signedDirectories.Count == 0 && signedFilePaths.Count == 0)
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

        refreshed.AddRange(RefreshModuleArchives(result, signedDirectories));
        foreach (var signedFile in signedFilePaths)
        {
            refreshed.AddRange(RefreshStagedCopies(result, signedFile));
        }

        if (refreshed.Count > 0)
            RewriteReleaseSummaryFiles(result);

        return refreshed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> RefreshModuleArchives(
        PowerForgeReleaseResult result,
        ISet<string> signedDirectories)
    {
        var archivePaths = result.ReleaseAssetEntries
            .Where(static entry => entry.Category == PowerForgeReleaseAssetCategory.Module)
            .Select(static entry => entry.Path)
            .Concat((result.ModuleAssets ?? [])
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .SelectMany(static path => File.Exists(path)
                    ? new[] { path }
                    : Directory.Exists(path)
                        ? Directory.EnumerateFiles(path, "*.zip", SearchOption.TopDirectoryOnly)
                        : Array.Empty<string>()))
            .Where(static path =>
                !string.IsNullOrWhiteSpace(path) &&
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var refreshed = new List<string>();
        foreach (var archivePath in archivePaths)
        {
            var sourceDirectory = ResolveModuleArchiveSource(archivePath, signedDirectories);
            if (sourceDirectory is null)
            {
                throw new InvalidOperationException(
                    $"Signed module output matching archive '{archivePath}' was not found. The unsigned archive cannot be published.");
            }

            RecreateArchiveFromExistingEntries(sourceDirectory, archivePath);
            refreshed.Add(archivePath);
            refreshed.AddRange(RefreshStagedCopies(result, archivePath));
        }

        return refreshed;
    }

    private static void RecreateArchiveFromExistingEntries(string sourceDirectory, string archivePath)
    {
        var entries = new List<(string FullName, DateTimeOffset LastWriteTime, bool IsDirectory)>();
        using (var existing = ZipFile.OpenRead(archivePath))
        {
            entries.AddRange(existing.Entries.Select(static entry => (
                entry.FullName,
                entry.LastWriteTime,
                IsDirectory: string.IsNullOrWhiteSpace(entry.Name))));
        }

        var temporaryArchive = Path.Combine(
            Path.GetTempPath(),
            $"powerforge-signed-module-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    var rebuilt = archive.CreateEntry(
                        entry.FullName,
                        entry.IsDirectory ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    rebuilt.LastWriteTime = entry.LastWriteTime;
                    if (entry.IsDirectory)
                        continue;

                    var sourcePath = Path.Combine(
                        sourceDirectory,
                        entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    using var source = File.OpenRead(sourcePath);
                    using var destination = rebuilt.Open();
                    source.CopyTo(destination);
                }
            }

            File.Copy(temporaryArchive, archivePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
                File.Delete(temporaryArchive);
        }
    }

    private static string? ResolveModuleArchiveSource(
        string archivePath,
        ISet<string> signedDirectories)
    {
        string[] archiveEntries;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            archiveEntries = archive.Entries
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(static entry => entry.FullName.Replace('\\', '/'))
                .ToArray();
        }

        var manifestEntries = archiveEntries
            .Where(static entry => entry.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.Count(character => character == '/'))
            .ThenBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (manifestEntries.Length == 0)
            return null;
        var archiveManifestVersions = ReadArchiveManifestVersions(archivePath, manifestEntries);

        foreach (var signedDirectory in signedDirectories.Where(Directory.Exists))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(signedDirectory, "*.psd1", SearchOption.AllDirectories))
            {
                foreach (var manifestEntry in manifestEntries)
                {
                    if (!archiveManifestVersions.TryGetValue(manifestEntry, out var archiveVersion) ||
                        !ModuleManifestValueReader.TryGetTopLevelString(manifestPath, "ModuleVersion", out var candidateVersion) ||
                        string.IsNullOrWhiteSpace(candidateVersion) ||
                        !ModuleVersionsMatch(archiveVersion, candidateVersion!))
                    {
                        continue;
                    }

                    var sourceRoot = ResolveArchiveRootFromManifest(manifestPath, manifestEntry);
                    if (sourceRoot is null || !Directory.Exists(sourceRoot))
                        continue;
                    if (archiveEntries.All(entry => File.Exists(Path.Combine(
                            sourceRoot,
                            entry.Replace('/', Path.DirectorySeparatorChar)))))
                    {
                        return sourceRoot;
                    }
                }
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadArchiveManifestVersions(
        string archivePath,
        IReadOnlyCollection<string> manifestEntries)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var manifestEntry in manifestEntries)
        {
            var entry = archive.GetEntry(manifestEntry);
            if (entry is null)
                continue;

            using var reader = new StreamReader(entry.Open());
            var manifestText = reader.ReadToEnd();
            if (ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(
                    manifestText,
                    "ModuleVersion",
                    out var version) &&
                !string.IsNullOrWhiteSpace(version))
            {
                versions[manifestEntry] = version!;
            }
        }

        return versions;
    }

    private static bool ModuleVersionsMatch(string archiveVersion, string candidateVersion)
    {
        if (Version.TryParse(archiveVersion, out var parsedArchive) &&
            Version.TryParse(candidateVersion, out var parsedCandidate))
        {
            return parsedArchive.Equals(parsedCandidate);
        }

        return string.Equals(archiveVersion, candidateVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveArchiveRootFromManifest(string manifestPath, string manifestEntry)
    {
        if (!string.Equals(
                Path.GetFileName(manifestPath),
                Path.GetFileName(manifestEntry),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(manifestPath)!);
        var entryDirectories = manifestEntry
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Length - 1;
        for (var index = 0; index < entryDirectories; index++)
        {
            directory = directory.Parent;
            if (directory is null)
                return null;
        }

        var expectedManifest = Path.Combine(
            directory.FullName,
            manifestEntry.Replace('/', Path.DirectorySeparatorChar));
        return string.Equals(
            Path.GetFullPath(expectedManifest),
            Path.GetFullPath(manifestPath),
            StringComparison.OrdinalIgnoreCase)
            ? directory.FullName
            : null;
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
