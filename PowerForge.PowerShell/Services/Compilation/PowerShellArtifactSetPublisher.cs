using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Publishes a complete compilation artifact set with rollback when a durable replacement fails.
/// </summary>
internal static class PowerShellArtifactSetPublisher
{
    internal static string CreateStagingDirectory(string outputDirectory, string artifactName)
    {
        EnsureArtifactNameIsNotReserved(artifactName, nameof(artifactName));
        var path = Path.Combine(outputDirectory, "." + artifactName + ".artifact-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static string RebasePath(string path, string stagingDirectory, string outputDirectory)
    {
        var relativePath = FrameworkCompatibility.GetRelativePath(stagingDirectory, path);
        if (relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("A staged artifact path escaped its publication directory.");
        return Path.Combine(outputDirectory, relativePath);
    }

    internal static PowerShellCompilationArtifactFile[] RebaseFiles(
        IEnumerable<PowerShellCompilationArtifactFile> files,
        string stagingDirectory,
        string outputDirectory)
        => files.Select(file => new PowerShellCompilationArtifactFile
        {
            Path = RebasePath(file.Path, stagingDirectory, outputDirectory),
            Role = file.Role,
            Sha256 = file.Sha256,
            SizeBytes = file.SizeBytes
        }).ToArray();

    internal static void Commit(string stagingDirectory, string outputDirectory, string artifactName, IEnumerable<string> protectedSourcePaths)
    {
        EnsureArtifactNameIsNotReserved(artifactName, nameof(artifactName));
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
            outputDirectory,
            $"PowerShell compilation output directory '{outputDirectory}' must not be a symbolic link or junction.");
        var stagingPath = NormalizeDirectoryPath(stagingDirectory);
        var outputPath = NormalizeDirectoryPath(outputDirectory);
        var protectedPaths = protectedSourcePaths
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        foreach (var protectedPath in protectedPaths)
        {
            if (!File.Exists(protectedPath) && !Directory.Exists(protectedPath))
                continue;
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
                protectedPath,
                $"Protected compilation source '{protectedPath}' must not traverse a symbolic link or junction.");
        }
        if (!string.Equals(Path.GetDirectoryName(stagingPath), outputPath, PowerShellCompilationPathSafety.PathComparison))
            throw new InvalidOperationException("Artifact staging must be a direct child of the durable output directory.");
        using var publicationLock = AcquirePublicationLock(outputPath, artifactName);

        var stagedEntries = Directory.EnumerateFileSystemEntries(stagingPath).ToArray();
        if (stagedEntries.Length == 0)
            throw new InvalidOperationException("The staged artifact set is empty.");

        var ownedNames = new[]
        {
            artifactName,
            artifactName + ".exe",
            artifactName + ".dll",
            artifactName + ".pdb",
            artifactName + ".generated",
            artifactName + ".powerforge-compilation.json"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        EnsureOwnedEntriesDoNotContainSource(outputPath, ownedNames, protectedPaths);
        var backupDirectory = Path.Combine(outputPath, "." + artifactName + ".artifact-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        var backups = new List<(string Backup, string Target)>();
        var installed = new List<string>();
        var preserveBackup = false;

        try
        {
            foreach (var name in ownedNames)
            {
                var target = Path.Combine(outputPath, name);
                if (!EntryExists(target)) continue;
                var backup = Path.Combine(backupDirectory, name);
                MoveEntry(target, backup);
                backups.Add((backup, target));
            }

            foreach (var stagedEntry in stagedEntries)
            {
                var target = Path.Combine(outputPath, Path.GetFileName(stagedEntry));
                MoveEntry(stagedEntry, target);
                installed.Add(target);
            }
        }
        catch (Exception publicationError)
        {
            Exception? rollbackError = null;
            foreach (var target in installed.AsEnumerable().Reverse())
            {
                try { DeleteEntry(target); } catch (Exception ex) { rollbackError ??= ex; }
            }
            foreach (var backup in backups.AsEnumerable().Reverse())
            {
                try { MoveEntry(backup.Backup, backup.Target); } catch (Exception ex) { rollbackError ??= ex; }
            }
            if (rollbackError is not null)
            {
                preserveBackup = true;
                throw new InvalidOperationException("Artifact publication and rollback both failed; inspect the output and backup directories before reuse.", new AggregateException(publicationError, rollbackError));
            }
            throw new InvalidOperationException("Artifact publication failed; the previous durable artifact set was restored.", publicationError);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
            if (!preserveBackup)
                TryDeleteDirectory(backupDirectory);
        }
    }

    internal static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    /// <summary>Rejects durable artifact names that can occupy publisher lock, staging, or rollback paths.</summary>
    /// <param name="artifactName">Sanitized artifact name to validate.</param>
    /// <param name="parameterName">Public parameter name to report when validation fails.</param>
    internal static void EnsureArtifactNameIsNotReserved(string artifactName, string parameterName)
    {
        if (!artifactName.StartsWith(".", StringComparison.Ordinal))
            return;
        var marker = artifactName.IndexOf(".artifact-", 1, StringComparison.OrdinalIgnoreCase);
        if (marker < 2)
            return;
        var controlName = artifactName.Substring(marker + ".artifact-".Length);
        if (controlName.Equals("publish.lock", StringComparison.OrdinalIgnoreCase) ||
            controlName.StartsWith("staging-", StringComparison.OrdinalIgnoreCase) ||
            controlName.StartsWith("backup-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Artifact name overlaps the reserved publication-control namespace used for locks, staging, and rollback.",
                parameterName);
        }
    }

    private static bool EntryExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void EnsureOwnedEntriesDoNotContainSource(string outputDirectory, IEnumerable<string> ownedNames, IEnumerable<string> sourcePaths)
    {
        var protectedPaths = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        foreach (var name in ownedNames)
        {
            var target = Path.GetFullPath(Path.Combine(outputDirectory, name));
            if (!EntryExists(target)) continue;
            var protectedPath = protectedPaths.FirstOrDefault(path =>
                string.Equals(target, path, PowerShellCompilationPathSafety.PathComparison) ||
                Directory.Exists(target) && IsSameOrDescendant(path, target));
            if (protectedPath is not null)
                throw new InvalidOperationException($"Artifact publication target '{target}' contains the input source '{protectedPath}' and cannot be replaced.");
        }
    }

    private static bool IsSameOrDescendant(string path, string directory)
    {
        var normalizedDirectory = NormalizeDirectoryPath(directory);
        return string.Equals(path, normalizedDirectory, PowerShellCompilationPathSafety.PathComparison) ||
               path.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, PowerShellCompilationPathSafety.PathComparison) ||
               path.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, PowerShellCompilationPathSafety.PathComparison);
    }

    private static IDisposable AcquirePublicationLock(string outputDirectory, string artifactName)
    {
        var lockPath = Path.Combine(outputDirectory, "." + artifactName + ".artifact-publish.lock");
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new PublicationLock(
                    new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(50);
            }
            catch (IOException exception)
            {
                throw new TimeoutException($"Timed out waiting to publish artifact '{artifactName}' to '{outputDirectory}'.", exception);
            }
        }
    }

    private sealed class PublicationLock : IDisposable
    {
        private FileStream? _stream;

        internal PublicationLock(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
                return;
            stream.Dispose();
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length > root.Length
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
    }

    private static void MoveEntry(string source, string destination)
    {
        if (File.Exists(source))
            File.Move(source, destination);
        else if (Directory.Exists(source))
            Directory.Move(source, destination);
        else
            throw new FileNotFoundException("Artifact publication entry was not found.", source);
    }

    private static void DeleteEntry(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        else if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
