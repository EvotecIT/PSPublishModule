using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

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
        if (!PowerShellCompilationPathSafety.PathEquals(Path.GetDirectoryName(stagingPath), outputPath))
            throw new InvalidOperationException("Artifact staging must be a direct child of the durable output directory.");
        using var publicationLock = AcquirePublicationLock(outputPath, artifactName);

        var stagedFiles = Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(stagingPath, path), PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        var stagedEmptyDirectories = Directory.EnumerateDirectories(stagingPath, "*", SearchOption.AllDirectories)
            .Where(static directory => !Directory.EnumerateFileSystemEntries(directory).Any())
            .ToArray();
        if (stagedFiles.Length == 0 && stagedEmptyDirectories.Length == 0)
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
        string[] previousOwnedFiles;
        try
        {
            previousOwnedFiles = ReadPreviousOwnedFiles(outputPath, artifactName, protectedPaths);
            EnsureFixedEntriesAreOwned(outputPath, artifactName, ownedNames, previousOwnedFiles);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Artifact publication failed; the previous durable artifact set was restored.", exception);
        }
        var backupDirectory = Path.Combine(outputPath, "." + artifactName + ".artifact-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        var backups = new List<(string Backup, string Target)>();
        var installed = new List<string>();
        var createdDirectories = new List<string>();
        var preserveBackup = false;

        try
        {
            var previousManifestPath = Path.Combine(outputPath, artifactName + ".powerforge-compilation.json");
            if (File.Exists(previousManifestPath))
            {
                var backup = Path.Combine(backupDirectory, Path.GetFileName(previousManifestPath));
                File.Move(previousManifestPath, backup);
                backups.Add((backup, previousManifestPath));
            }

            foreach (var priorFile in previousOwnedFiles)
            {
                if (!File.Exists(priorFile) || PowerShellCompilationPathSafety.PathEquals(priorFile, previousManifestPath))
                    continue;
                PowerShellCompilationPathSafety.EnsureNoLinks(
                    outputPath,
                    priorFile,
                    $"Previously published artifact file '{priorFile}' traverses a symbolic link or junction.");
                var relative = FrameworkCompatibility.GetRelativePath(outputPath, priorFile);
                var backup = Path.Combine(backupDirectory, "files", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(priorFile, backup);
                backups.Add((backup, priorFile));
            }

            // Prune directories emptied by the previous owned set before installing the
            // replacement. This permits an owned path to change from Directory/File to File.
            RemoveEmptyPreviousDirectories(previousOwnedFiles, outputPath);

            foreach (var stagedFile in stagedFiles)
            {
                var relative = FrameworkCompatibility.GetRelativePath(stagingPath, stagedFile);
                var target = Path.GetFullPath(Path.Combine(outputPath, relative));
                PowerShellCompilationPathSafety.EnsureContained(outputPath, target, "A staged artifact file escaped the durable output directory.");
                EnsureExistingParentHasNoLinks(outputPath, target);
                if (EntryExists(target))
                    throw new InvalidOperationException($"Artifact publication target '{target}' already exists and is not owned by the previous artifact manifest.");
                CreateDirectoriesTracked(outputPath, Path.GetDirectoryName(target)!, createdDirectories);
                File.Move(stagedFile, target);
                installed.Add(target);
            }

            foreach (var stagedDirectory in stagedEmptyDirectories)
            {
                var relative = FrameworkCompatibility.GetRelativePath(stagingPath, stagedDirectory);
                var target = Path.GetFullPath(Path.Combine(outputPath, relative));
                PowerShellCompilationPathSafety.EnsureContained(outputPath, target, "A staged artifact directory escaped the durable output directory.");
                EnsureExistingParentHasNoLinks(outputPath, target);
                if (File.Exists(target))
                    throw new InvalidOperationException($"Artifact publication directory '{target}' collides with an existing file.");
                if (Directory.Exists(target)) continue;
                Directory.CreateDirectory(target);
                createdDirectories.Add(target);
            }
        }
        catch (Exception publicationError)
        {
            Exception? rollbackError = null;
            foreach (var target in installed.AsEnumerable().Reverse())
            {
                try { DeleteEntry(target); } catch (Exception ex) { rollbackError ??= ex; }
            }
            foreach (var directory in createdDirectories.OrderByDescending(static directory => directory.Length))
            {
                try
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch (Exception ex) { rollbackError ??= ex; }
            }
            foreach (var backup in backups.AsEnumerable().Reverse())
            {
                try
                {
                    CreateDirectoriesTracked(outputPath, Path.GetDirectoryName(backup.Target) ?? outputPath, createdDirectories: null);
                    MoveEntry(backup.Backup, backup.Target);
                }
                catch (Exception ex) { rollbackError ??= ex; }
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
        if (artifactName.EndsWith(".", StringComparison.Ordinal) || IsWindowsDeviceName(artifactName))
        {
            throw new ArgumentException(
                "Artifact name is not stable under Windows file-name normalization or uses a reserved Windows device name.",
                parameterName);
        }
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

    private static bool IsWindowsDeviceName(string artifactName)
    {
        var stem = artifactName.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        return stem.Length == 4 &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               stem[3] is >= '1' and <= '9';
    }

    private static bool EntryExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string[] ReadPreviousOwnedFiles(string outputDirectory, string artifactName, IReadOnlyCollection<string> protectedPaths)
    {
        var manifestPath = Path.Combine(outputDirectory, artifactName + ".powerforge-compilation.json");
        if (!File.Exists(manifestPath)) return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("artifactName", out var artifactNameElement) ||
                artifactNameElement.ValueKind != JsonValueKind.String ||
                !string.Equals(artifactNameElement.GetString(), artifactName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The prior compilation manifest does not identify artifact '{artifactName}'.");
            }
            if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The prior compilation manifest does not contain a valid files array.");
            var owned = new List<string>();
            foreach (var item in files.EnumerateArray())
            {
                if (!item.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("The prior compilation manifest contains an invalid file path entry.");
                var value = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                    throw new InvalidOperationException("The prior compilation manifest contains a non-absolute file path.");
                if (!item.TryGetProperty("sha256", out var hashElement) || hashElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(hashElement.GetString()))
                    throw new InvalidOperationException("The prior compilation manifest contains a file without SHA-256 ownership evidence.");
                var path = Path.GetFullPath(value);
                PowerShellCompilationPathSafety.EnsureContained(outputDirectory, path, $"Prior artifact ownership path '{path}' escapes the durable output directory.");
                if (protectedPaths.Any(protectedPath => PowerShellCompilationPathSafety.PathEquals(path, protectedPath)))
                    throw new InvalidOperationException($"Prior artifact ownership path '{path}' overlaps a protected compilation source.");
                if (Directory.Exists(path))
                    throw new InvalidOperationException($"Prior artifact ownership path '{path}' identifies a directory instead of a file.");
                if (File.Exists(path) && !ComputeSha256(path).Equals(hashElement.GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Prior artifact ownership path '{path}' no longer matches its recorded SHA-256 and will not be replaced.");
                owned.Add(path);
            }
            return owned.Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Prior compilation manifest '{manifestPath}' is invalid and cannot be used for safe artifact replacement.", exception);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static void EnsureFixedEntriesAreOwned(
        string outputDirectory,
        string artifactName,
        IEnumerable<string> ownedNames,
        IReadOnlyCollection<string> previousOwnedFiles)
    {
        var manifestPath = Path.GetFullPath(Path.Combine(outputDirectory, artifactName + ".powerforge-compilation.json"));
        foreach (var name in ownedNames)
        {
            var target = Path.GetFullPath(Path.Combine(outputDirectory, name));
            if (!EntryExists(target))
                continue;

            if (PowerShellCompilationPathSafety.PathEquals(target, manifestPath) && File.Exists(target))
                continue;

            var isOwned = File.Exists(target)
                ? previousOwnedFiles.Any(path => PowerShellCompilationPathSafety.PathEquals(path, target))
                : previousOwnedFiles.Any(path => IsSameOrDescendant(path, target));
            if (!isOwned)
            {
                throw new InvalidOperationException(
                    $"Artifact publication target '{target}' already exists and is not owned by the previous artifact manifest.");
            }
        }
    }

    private static void EnsureExistingParentHasNoLinks(string outputDirectory, string target)
    {
        var current = Path.GetDirectoryName(target);
        while (!string.IsNullOrWhiteSpace(current) && !Directory.Exists(current))
            current = Path.GetDirectoryName(current);
        if (string.IsNullOrWhiteSpace(current))
            throw new InvalidOperationException($"Artifact publication target '{target}' has no existing parent directory.");
        PowerShellCompilationPathSafety.EnsureNoLinks(
            outputDirectory,
            current,
            $"Artifact publication target '{target}' traverses a symbolic link or junction.");
    }

    private static void CreateDirectoriesTracked(string outputDirectory, string directory, ICollection<string>? createdDirectories)
    {
        var targetDirectory = Path.GetFullPath(directory);
        if (!PowerShellCompilationPathSafety.PathEquals(targetDirectory, outputDirectory))
        {
            PowerShellCompilationPathSafety.EnsureContained(
                outputDirectory,
                targetDirectory,
                $"Artifact publication directory '{targetDirectory}' escapes the durable output directory.");
        }
        var missing = new Stack<string>();
        var current = targetDirectory;
        while (!Directory.Exists(current) && !PowerShellCompilationPathSafety.PathEquals(current, outputDirectory))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current)
                      ?? throw new InvalidOperationException($"Artifact publication directory '{targetDirectory}' has no durable parent.");
        }
        if (!Directory.Exists(current))
            throw new DirectoryNotFoundException($"Artifact publication output directory '{outputDirectory}' does not exist.");
        while (missing.Count > 0)
        {
            var created = missing.Pop();
            Directory.CreateDirectory(created);
            createdDirectories?.Add(created);
        }
    }

    private static void RemoveEmptyPreviousDirectories(IEnumerable<string> previousFiles, string outputDirectory)
    {
        foreach (var directory in previousFiles
                     .Select(Path.GetDirectoryName)
                     .Where(static directory => !string.IsNullOrWhiteSpace(directory))
                     .Select(static directory => Path.GetFullPath(directory!))
                     .Where(directory => !PowerShellCompilationPathSafety.PathEquals(directory, outputDirectory))
                     .Distinct(PowerShellCompilationPathSafety.PathComparer)
                     .OrderByDescending(static directory => directory.Length))
        {
            var current = directory;
            while (!PowerShellCompilationPathSafety.PathEquals(current, outputDirectory) && Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current) ?? outputDirectory;
            }
        }
    }

    private static void EnsureOwnedEntriesDoNotContainSource(string outputDirectory, IEnumerable<string> ownedNames, IEnumerable<string> sourcePaths)
    {
        var protectedPaths = sourcePaths.Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        foreach (var name in ownedNames)
        {
            var target = Path.GetFullPath(Path.Combine(outputDirectory, name));
            if (!EntryExists(target)) continue;
            var protectedPath = protectedPaths.FirstOrDefault(path =>
                PowerShellCompilationPathSafety.PathEquals(target, path) ||
                Directory.Exists(target) && IsSameOrDescendant(path, target));
            if (protectedPath is not null)
                throw new InvalidOperationException($"Artifact publication target '{target}' contains the input source '{protectedPath}' and cannot be replaced.");
        }
    }

    private static bool IsSameOrDescendant(string path, string directory)
    {
        var normalizedDirectory = NormalizeDirectoryPath(directory);
        return PowerShellCompilationPathSafety.PathEquals(path, normalizedDirectory) ||
               PowerShellCompilationPathSafety.PathStartsWith(path, normalizedDirectory + Path.DirectorySeparatorChar) ||
               PowerShellCompilationPathSafety.PathStartsWith(path, normalizedDirectory + Path.AltDirectorySeparatorChar);
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
