using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryRefreshLockedRestoreOutputs(ProjectEvaluationRequest request)
    {
        string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
        if (!File.Exists(Path.Combine(projectDirectory, "packages.lock.json")))
            return true;

        string temporaryLockFile = Path.Combine(
            Path.GetTempPath(),
            "powerforge-restore-lock-" + Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string>
        {
            "restore",
            request.ProjectPath,
            "--force-evaluate",
            "--no-cache",
            "--nologo",
            "-p:Configuration=" + request.Configuration,
            "-p:RestorePackagesWithLockFile=true",
            "-p:RestoreLockedMode=false",
            "-p:NuGetLockFilePath=" + temporaryLockFile
        };

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                projectDirectory,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(5));
            return process.ExitCode == 0 && !process.TimedOut;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryLockFile))
                    File.Delete(temporaryLockFile);
            }
            catch
            {
                // A leftover temporary lock cannot make an input trusted.
            }
        }
    }

    private static void AddEffectiveBuildControlInputs(
        string projectPath,
        JsonElement properties,
        HashSet<string> inputs,
        HashSet<string> sourceInputs)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string? gitRoot = ReadGitText(projectDirectory, "rev-parse --show-toplevel");
        string boundary = Path.GetFullPath(string.IsNullOrWhiteSpace(gitRoot) ? projectDirectory : gitRoot!);
        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "NuGet.Config",
            "nuget.config",
            "packages.lock.json"
        ];

        string current = Path.GetFullPath(projectDirectory);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            foreach (string name in names)
                AddBuildControlCandidate(Path.Combine(current, name), inputs, sourceInputs);

            if (string.Equals(current, boundary, comparison))
                break;
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, comparison))
                break;
            current = parent;
        }

        string? evaluatedLockFile = ReadEvaluatedPath(
            properties,
            "NuGetLockFilePath",
            projectDirectory);
        if (!string.IsNullOrWhiteSpace(evaluatedLockFile))
            AddBuildControlCandidate(evaluatedLockFile!, inputs, sourceInputs);
    }

    private static void AddBuildControlCandidate(
        string path,
        HashSet<string> inputs,
        HashSet<string> sourceInputs)
    {
        string fullPath = Path.GetFullPath(path);
        inputs.Add(fullPath);
        if (File.Exists(fullPath))
            sourceInputs.Add(fullPath);
    }

    private static void AddPackageFoldersFromAssets(
        JsonElement properties,
        string projectDirectory,
        HashSet<string> packageRoots)
    {
        string assetsPath = ReadEvaluatedPath(properties, "ProjectAssetsFile", projectDirectory)
            ?? Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            if (!document.RootElement.TryGetProperty("packageFolders", out JsonElement folders) ||
                folders.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty folder in folders.EnumerateObject())
                packageRoots.Add(Path.GetFullPath(folder.Name));
        }
        catch
        {
            // An unreadable assets file leaves package inputs unverified and therefore fail closed.
        }
    }

    private static string? ReadEvaluatedPath(
        JsonElement properties,
        string name,
        string baseDirectory)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return null;
        }

        string value = property.GetString()!;
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
    }

    private static void AddClassifiedEvaluatedInput(
        string path,
        bool isSourceInput,
        HashSet<string> inputs,
        HashSet<string> sourceInputs,
        IEnumerable<string> generatedBuildRoots,
        VerifiedPackageInputCatalog? verifiedPackages)
    {
        string fullPath = Path.GetFullPath(path);
        if (IsGeneratedBuildInfrastructurePath(fullPath, generatedBuildRoots))
            return;

        bool isPackageInput = false;
        if (verifiedPackages?.TryVerify(fullPath, out isPackageInput) is true)
            return;

        inputs.Add(fullPath);
        if (File.Exists(fullPath) &&
            (isPackageInput ||
             (isSourceInput && !IsTrustedExternalBuildInfrastructurePath(fullPath))))
        {
            sourceInputs.Add(fullPath);
        }
    }

    private static bool TryReadPreprocessedProjectImports(
        ProjectEvaluationRequest request,
        out string[] imports)
    {
        imports = Array.Empty<string>();
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            "powerforge-msbuild-imports-" + Guid.NewGuid().ToString("N") + ".xml");
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-preprocess:" + outputPath,
            "-p:Configuration=" + request.Configuration
        };
        if (!string.IsNullOrWhiteSpace(request.TargetFramework))
            arguments.Add("-p:TargetFramework=" + request.TargetFramework);
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + property.Value);
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut || !File.Exists(outputPath))
                return false;

            var resolved = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            bool inComment = false;
            bool describesImport = false;
            foreach (string line in File.ReadLines(outputPath))
            {
                if (line.Contains("<!--", StringComparison.Ordinal))
                {
                    inComment = true;
                    describesImport = false;
                }
                if (inComment && line.Contains("<Import", StringComparison.Ordinal))
                    describesImport = true;
                if (inComment && describesImport)
                {
                    string candidate = line.Trim();
                    if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                        resolved.Add(Path.GetFullPath(candidate));
                }
                if (line.Contains("-->", StringComparison.Ordinal))
                {
                    inComment = false;
                    describesImport = false;
                }
            }
            imports = resolved.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // The provenance result already fails closed if preprocessing did not complete.
            }
        }
    }

    private sealed class VerifiedPackageInputCatalog : IDisposable
    {
        private readonly string[] _packageRoots;
        private readonly IReadOnlyDictionary<string, string> _lockedPackageHashes;
        private readonly Dictionary<string, VerifiedPackageArchive> _archives;

        private VerifiedPackageInputCatalog(
            IEnumerable<string> packageRoots,
            IReadOnlyDictionary<string, string> lockedPackageHashes)
        {
            _packageRoots = packageRoots
                .Select(Path.GetFullPath)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            _lockedPackageHashes = lockedPackageHashes;
            _archives = new Dictionary<string, VerifiedPackageArchive>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        internal static VerifiedPackageInputCatalog? TryCreate(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            string lockFilePath = ReadEvaluatedPath(properties, "NuGetLockFilePath", projectDirectory)
                ?? Path.Combine(projectDirectory, "packages.lock.json");
            string[] roots = packageRoots
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Where(root => !IsSameOrBelowBuildInputPath(projectDirectory, root))
                .ToArray();
            if (roots.Length == 0 || !TryReadLockedPackageHashes(lockFilePath, out Dictionary<string, string> hashes))
                return null;

            return new VerifiedPackageInputCatalog(roots, hashes);
        }

        internal bool TryVerify(string path, out bool isPackageInput)
        {
            string fullPath = Path.GetFullPath(path);
            foreach (string root in _packageRoots)
            {
                if (!IsSameOrBelowBuildInputPath(fullPath, root))
                    continue;

                isPackageInput = true;
                return TryVerifyBelowRoot(fullPath, root);
            }

            isPackageInput = false;
            return false;
        }

        private bool TryVerifyBelowRoot(string path, string root)
        {
            try
            {
                if (IsReparsePoint(root) || HasReparsePointBelowRoot(path, root))
                    return false;

                string relative = FrameworkCompatibility.GetRelativePath(root, path)
                    .Replace('\\', '/')
                    .Trim('/');
                string[] segments = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 3 || segments.Any(segment => segment == ".."))
                    return false;

                string packageId = segments[0];
                string packageVersion = segments[1];
                string packageKey = packageId + "|" + packageVersion;
                if (!_lockedPackageHashes.TryGetValue(packageKey, out string? expectedHash) ||
                    string.IsNullOrWhiteSpace(expectedHash))
                {
                    return false;
                }

                string packageDirectory = Path.Combine(root, packageId, packageVersion);
                string archiveKey = Path.GetFullPath(packageDirectory);
                if (!_archives.TryGetValue(archiveKey, out VerifiedPackageArchive? archive))
                {
                    string expectedName = packageId + "." + packageVersion + ".nupkg";
                    string? archivePath = Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(candidate => Path.GetFileName(candidate).Equals(
                            expectedName,
                            StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(archivePath))
                        return false;
                    archive = VerifiedPackageArchive.TryOpen(archivePath!, expectedHash);
                    if (archive is null)
                        return false;
                    _archives[archiveKey] = archive;
                }

                string packageRelativePath = string.Join("/", segments.Skip(2));
                return archive.VerifyExtractedFile(packageRelativePath, path);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (VerifiedPackageArchive archive in _archives.Values)
                archive.Dispose();
            _archives.Clear();
        }

        private static bool TryReadLockedPackageHashes(
            string lockFilePath,
            out Dictionary<string, string> hashes)
        {
            hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
                if (!document.RootElement.TryGetProperty("dependencies", out JsonElement frameworks) ||
                    frameworks.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty framework in frameworks.EnumerateObject())
                {
                    if (framework.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    foreach (JsonProperty package in framework.Value.EnumerateObject())
                    {
                        if (package.Value.ValueKind != JsonValueKind.Object ||
                            !package.Value.TryGetProperty("resolved", out JsonElement resolved) ||
                            resolved.ValueKind != JsonValueKind.String ||
                            !package.Value.TryGetProperty("contentHash", out JsonElement contentHash) ||
                            contentHash.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string key = package.Name + "|" + resolved.GetString();
                        string value = contentHash.GetString() ?? string.Empty;
                        if (hashes.TryGetValue(key, out string? existing) &&
                            !string.Equals(existing, value, StringComparison.Ordinal))
                        {
                            hashes[key] = string.Empty;
                        }
                        else
                        {
                            hashes[key] = value;
                        }
                    }
                }

                return hashes.Count > 0;
            }
            catch
            {
                hashes.Clear();
                return false;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }
    }

    private sealed class VerifiedPackageArchive : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;
        private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _entries;

        private VerifiedPackageArchive(
            FileStream stream,
            ZipArchive archive,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            _stream = stream;
            _archive = archive;
            _entries = entries;
        }

        internal static VerifiedPackageArchive? TryOpen(string path, string expectedContentHash)
        {
            FileStream? stream = null;
            ZipArchive? archive = null;
            try
            {
                stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (SHA512 sha512 = SHA512.Create())
                {
                    string actualHash = Convert.ToBase64String(sha512.ComputeHash(stream));
                    if (!string.Equals(actualHash, expectedContentHash, StringComparison.Ordinal))
                    {
                        stream.Dispose();
                        return null;
                    }
                }

                stream.Position = 0;
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entries = new Dictionary<string, ZipArchiveEntry>(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
                {
                    string name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (entries.ContainsKey(name))
                    {
                        archive.Dispose();
                        stream.Dispose();
                        return null;
                    }
                    entries.Add(name, entry);
                }

                return new VerifiedPackageArchive(stream, archive, entries);
            }
            catch
            {
                archive?.Dispose();
                stream?.Dispose();
                return null;
            }
        }

        internal bool VerifyExtractedFile(string relativePath, string extractedPath)
        {
            if (!_entries.TryGetValue(relativePath.Replace('\\', '/').TrimStart('/'), out ZipArchiveEntry? entry))
                return false;

            var file = new FileInfo(extractedPath);
            if (!file.Exists || file.Length != entry.Length)
                return false;

            using Stream entryStream = entry.Open();
            using FileStream fileStream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            using SHA256 entryHasher = SHA256.Create();
            using SHA256 fileHasher = SHA256.Create();
            byte[] expected = entryHasher.ComputeHash(entryStream);
            byte[] actual = fileHasher.ComputeHash(fileStream);
            return expected.SequenceEqual(actual);
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }
}
