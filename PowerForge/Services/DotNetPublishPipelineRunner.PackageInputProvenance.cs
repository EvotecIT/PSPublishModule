using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryRefreshLockedRestoreOutputs(ProjectEvaluationRequest request)
    {
        string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
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
            "-p:RestorePackagesWithLockFile=true",
            "-p:RestoreLockedMode=false",
            "-p:NuGetLockFilePath=" + temporaryLockFile
        };
        if (!string.IsNullOrWhiteSpace(request.Configuration))
            arguments.Add("-p:Configuration=" + request.Configuration);
        if (!string.IsNullOrWhiteSpace(request.TargetFramework) &&
            !ProjectDeclaresTargetFramework(request.ProjectPath))
        {
            arguments.Add("-p:TargetFramework=" + request.TargetFramework);
        }
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("NuGetLockFilePath", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("RestoreLockedMode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + property.Value);
        }

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
            "packages.lock.json",
            "Directory.Build.rsp",
            "MSBuild.rsp"
        ];

        string current = Path.GetFullPath(projectDirectory);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            foreach (string name in names)
                AddBuildControlCandidate(
                    Path.Combine(current, name),
                    inputs,
                    sourceInputs,
                    new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

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
            AddBuildControlCandidate(
                evaluatedLockFile!,
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

        string? msBuildToolsPath = ReadEvaluatedPath(properties, "MSBuildToolsPath", projectDirectory);
        if (!string.IsNullOrWhiteSpace(msBuildToolsPath))
            AddBuildControlCandidate(
                Path.Combine(msBuildToolsPath!, "MSBuild.rsp"),
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    }

    private static void AddBuildControlCandidate(
        string path,
        HashSet<string> inputs,
        HashSet<string> sourceInputs,
        HashSet<string> visitedResponseFiles)
    {
        string fullPath = Path.GetFullPath(path);
        inputs.Add(fullPath);
        if (File.Exists(fullPath))
        {
            sourceInputs.Add(fullPath);
            if (Path.GetExtension(fullPath).Equals(".rsp", StringComparison.OrdinalIgnoreCase) &&
                visitedResponseFiles.Add(fullPath))
            {
                foreach (string line in File.ReadLines(fullPath))
                {
                    string value = line.Trim();
                    if (!value.StartsWith("@", StringComparison.Ordinal) || value.Length == 1)
                        continue;
                    string nested = value.Substring(1).Trim().Trim('"');
                    if (nested.Length == 0)
                        continue;
                    AddBuildControlCandidate(
                        Path.IsPathRooted(nested)
                            ? nested
                            : Path.Combine(Path.GetDirectoryName(fullPath)!, nested),
                        inputs,
                        sourceInputs,
                        visitedResponseFiles);
                }
            }
        }
    }

    private static string[] ReadDeclaredBuildInputCandidates(
        string projectPath,
        IEnumerable<string> evaluatedImports)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new HashSet<string>(comparison);
        var pending = new Queue<string>();
        var visited = new HashSet<string>(comparison);
        pending.Enqueue(Path.GetFullPath(projectPath));
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string? gitRoot = ReadGitText(projectDirectory, "rev-parse --show-toplevel");
        foreach (string import in evaluatedImports)
        {
            if (!string.IsNullOrWhiteSpace(gitRoot) &&
                ToGitRelativeExclusion(projectDirectory, gitRoot!, import) is not null)
            {
                pending.Enqueue(Path.GetFullPath(import));
            }
        }

        while (pending.Count > 0)
        {
            string inputFile = pending.Dequeue();
            if (!visited.Add(inputFile) || !File.Exists(inputFile))
                continue;

            try
            {
                XDocument document = XDocument.Load(inputFile, LoadOptions.None);
                foreach (XElement element in document.Descendants())
                {
                    bool isImport = element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase);
                    bool isAnalyzer = element.Name.LocalName.Equals("Analyzer", StringComparison.OrdinalIgnoreCase);
                    if (!isImport && !isAnalyzer)
                        continue;
                    XAttribute? attribute = element.Attribute(isImport ? "Project" : "Include");
                    foreach (string candidate in ResolveDeclaredBuildInputPaths(
                                 attribute?.Value,
                                 inputFile,
                                 projectDirectory))
                    {
                        candidates.Add(candidate);
                        if (isImport && File.Exists(candidate) &&
                            !string.IsNullOrWhiteSpace(gitRoot) &&
                            ToGitRelativeExclusion(projectDirectory, gitRoot!, candidate) is not null)
                        {
                            pending.Enqueue(candidate);
                        }
                    }
                }
            }
            catch
            {
                candidates.Add(inputFile + ".powerforge-provenance-unreadable");
            }
        }

        return candidates.ToArray();
    }

    private static IEnumerable<string> ResolveDeclaredBuildInputPaths(
        string? value,
        string declaringFile,
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        string declaringDirectory = Path.GetDirectoryName(declaringFile)!;
        foreach (string rawPath in value!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = rawPath.Trim()
                .Replace("$(MSBuildThisFileDirectory)", declaringDirectory + Path.DirectorySeparatorChar)
                .Replace("$(MSBuildProjectDirectory)", projectDirectory);
            if (candidate.Contains("$(", StringComparison.Ordinal) ||
                candidate.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(declaringDirectory, candidate));
            yield return fullPath;
        }
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
        VerifiedPackageInputCatalog? verifiedPackages,
        IEnumerable<string>? trustedBuildInfrastructureRoots)
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
             (isSourceInput && !IsTrustedExternalBuildInfrastructurePath(
                 fullPath,
                 trustedBuildInfrastructureRoots))))
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
            "-preprocess:" + outputPath
        };
        if (!string.IsNullOrWhiteSpace(request.Configuration))
            arguments.Add("-p:Configuration=" + request.Configuration);
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

    private sealed class VerifiedPackageInputCatalog
    {
        private readonly string[] _packageRoots;
        private readonly IReadOnlyDictionary<string, string> _lockedPackageHashes;
        private readonly VerifiedPackageArchiveCache _archives;

        private VerifiedPackageInputCatalog(
            IEnumerable<string> packageRoots,
            IReadOnlyDictionary<string, string> lockedPackageHashes,
            VerifiedPackageArchiveCache archives)
        {
            _packageRoots = packageRoots
                .Select(Path.GetFullPath)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            _lockedPackageHashes = lockedPackageHashes;
            _archives = archives;
        }

        internal static VerifiedPackageInputCatalog? TryCreate(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots,
            VerifiedPackageArchiveCache archives)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            string lockFilePath = ReadEvaluatedPath(properties, "NuGetLockFilePath", projectDirectory)
                ?? Path.Combine(projectDirectory, "packages.lock.json");
            string[] roots = packageRoots
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Where(root => !IsSameOrBelowBuildInputPath(projectDirectory, root))
                .ToArray();
            var allRoots = new HashSet<string>(roots, IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string? environmentPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (!string.IsNullOrWhiteSpace(environmentPackages) && Directory.Exists(environmentPackages))
                allRoots.Add(Path.GetFullPath(environmentPackages));
            string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                string defaultPackages = Path.Combine(userProfile, ".nuget", "packages");
                if (Directory.Exists(defaultPackages))
                    allRoots.Add(Path.GetFullPath(defaultPackages));
            }

            TryReadLockedPackageHashes(lockFilePath, out Dictionary<string, string> hashes);
            AddSdkManagedPackageHashes(properties, projectDirectory, allRoots, hashes);
            if (allRoots.Count == 0)
                return null;

            return new VerifiedPackageInputCatalog(allRoots, hashes, archives);
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
                string expectedName = packageId + "." + packageVersion + ".nupkg";
                string? archivePath = Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(candidate => Path.GetFileName(candidate).Equals(
                        expectedName,
                        StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(archivePath))
                    return false;
                VerifiedPackageArchive? archive = _archives.TryGetOrOpen(archivePath!, expectedHash);
                if (archive is null)
                    return false;

                string packageRelativePath = string.Join("/", segments.Skip(2));
                return archive.VerifyExtractedFile(packageRelativePath, path);
            }
            catch
            {
                return false;
            }
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

    private sealed class VerifiedPackageArchiveCache : IDisposable
    {
        private readonly Dictionary<string, CacheEntry> _archives = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal VerifiedPackageArchive? TryGetOrOpen(string path, string expectedContentHash)
        {
            string fullPath = Path.GetFullPath(path);
            if (_archives.TryGetValue(fullPath, out CacheEntry? cached))
            {
                return string.Equals(cached.ExpectedContentHash, expectedContentHash, StringComparison.Ordinal)
                    ? cached.Archive
                    : null;
            }

            VerifiedPackageArchive? archive = VerifiedPackageArchive.TryOpen(fullPath, expectedContentHash);
            if (archive is not null)
                _archives.Add(fullPath, new CacheEntry(expectedContentHash, archive));
            return archive;
        }

        public void Dispose()
        {
            foreach (CacheEntry cached in _archives.Values)
                cached.Archive.Dispose();
            _archives.Clear();
        }

        private sealed class CacheEntry
        {
            internal CacheEntry(string expectedContentHash, VerifiedPackageArchive archive)
            {
                ExpectedContentHash = expectedContentHash;
                Archive = archive;
            }

            internal string ExpectedContentHash { get; }

            internal VerifiedPackageArchive Archive { get; }
        }
    }

    private sealed class VerifiedPackageArchive : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;
        private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _entries;
        private readonly Dictionary<string, byte[]> _entryHashes;
        private readonly Dictionary<string, ExtractedFileHash> _extractedFileHashes;

        private VerifiedPackageArchive(
            FileStream stream,
            ZipArchive archive,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            _stream = stream;
            _archive = archive;
            _entries = entries;
            _entryHashes = new Dictionary<string, byte[]>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            _extractedFileHashes = new Dictionary<string, ExtractedFileHash>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        internal static VerifiedPackageArchive? TryOpen(string path, string expectedContentHash)
        {
            FileStream? stream = null;
            ZipArchive? archive = null;
            try
            {
                using (var packageReader = new PackageArchiveReader(path))
                {
                    PrimarySignature? signature = packageReader
                        .GetPrimarySignatureAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    if (signature is not null)
                    {
                        packageReader
                            .ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                    string actualHash = packageReader.GetContentHash(CancellationToken.None);
                    if (!string.Equals(actualHash, expectedContentHash, StringComparison.Ordinal))
                        return null;
                }

                stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
            string normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            if (!_entries.TryGetValue(normalizedRelativePath, out ZipArchiveEntry? entry))
                return false;

            var file = new FileInfo(extractedPath);
            if (!file.Exists || file.Length != entry.Length)
                return false;

            string fullExtractedPath = file.FullName;
            long lastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks;
            if (_extractedFileHashes.TryGetValue(fullExtractedPath, out ExtractedFileHash? cached) &&
                cached.Length == file.Length &&
                cached.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks)
            {
                return GetEntryHash(normalizedRelativePath, entry).SequenceEqual(cached.Hash);
            }

            using FileStream fileStream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            using SHA256 fileHasher = SHA256.Create();
            byte[] actual = fileHasher.ComputeHash(fileStream);
            byte[] expected = GetEntryHash(normalizedRelativePath, entry);
            if (expected.SequenceEqual(actual))
            {
                _extractedFileHashes[fullExtractedPath] = new ExtractedFileHash(
                    file.Length,
                    lastWriteTimeUtcTicks,
                    actual);
                return true;
            }

            _extractedFileHashes.Remove(fullExtractedPath);
            return false;
        }

        private byte[] GetEntryHash(string relativePath, ZipArchiveEntry entry)
        {
            if (_entryHashes.TryGetValue(relativePath, out byte[]? hash))
                return hash;

            using Stream entryStream = entry.Open();
            using SHA256 entryHasher = SHA256.Create();
            hash = entryHasher.ComputeHash(entryStream);
            _entryHashes.Add(relativePath, hash);
            return hash;
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }

        private sealed class ExtractedFileHash
        {
            internal ExtractedFileHash(long length, long lastWriteTimeUtcTicks, byte[] hash)
            {
                Length = length;
                LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
                Hash = hash;
            }

            internal long Length { get; }

            internal long LastWriteTimeUtcTicks { get; }

            internal byte[] Hash { get; }
        }
    }
}
