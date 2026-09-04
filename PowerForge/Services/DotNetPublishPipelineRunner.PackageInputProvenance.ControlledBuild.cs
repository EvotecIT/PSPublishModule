using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed partial class VerifiedPackageInputCatalog
    {
        private static bool TryPrimeLockedPackageArchives(
            IEnumerable<string> packageRoots,
            IReadOnlyDictionary<string, string> lockedPackageHashes,
            VerifiedPackageArchiveCache archives,
            out Dictionary<string, string> archivePathsByPackageKey)
        {
            archivePathsByPackageKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (KeyValuePair<string, string> package in lockedPackageHashes)
                {
                    if (string.IsNullOrWhiteSpace(package.Value))
                        return false;
                    int separator = package.Key.LastIndexOf('|');
                    if (separator <= 0 || separator == package.Key.Length - 1)
                        return false;
                    string packageId = package.Key.Substring(0, separator);
                    string packageVersion = package.Key.Substring(separator + 1);
                    string expectedName = packageId + "." + packageVersion + ".nupkg";
                    string? archivePath = packageRoots
                        .Select(root => Path.Combine(
                            root,
                            packageId.ToLowerInvariant(),
                            packageVersion.ToLowerInvariant()))
                        .Where(Directory.Exists)
                        .SelectMany(directory => Directory.EnumerateFiles(
                            directory,
                            "*.nupkg",
                            SearchOption.TopDirectoryOnly))
                        .Where(candidate => Path.GetFileName(candidate).Equals(
                            expectedName,
                            StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault(candidate =>
                            archives.TryGetOrOpen(candidate, package.Value) is not null);
                    if (string.IsNullOrWhiteSpace(archivePath))
                    {
                        return false;
                    }
                    archivePathsByPackageKey.Add(package.Key, Path.GetFullPath(archivePath!));
                }
                return true;
            }
            catch
            {
                archivePathsByPackageKey.Clear();
                return false;
            }
        }

        internal bool TrySetControlledBuildInputs(IEnumerable<string> evaluatedInputs)
        {
            _controlledBuildInputsByArchive.Clear();
            try
            {
                foreach (string input in evaluatedInputs)
                {
                    string fullInput = Path.GetFullPath(input);
                    foreach (string root in _packageRoots)
                    {
                        if (!IsSameOrBelowBuildInputPath(fullInput, root))
                            continue;
                        if (!TryVerifyBelowRoot(fullInput, root))
                            return false;

                        string relative = FrameworkCompatibility.GetRelativePath(root, fullInput)
                            .Replace('\\', '/')
                            .Trim('/');
                        string[] segments = relative.Split(
                            new[] { '/' },
                            StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length < 3 || segments.Any(segment => segment == ".."))
                            return false;

                        string packageKey = segments[0] + "|" + segments[1];
                        if (!_archivePathsByPackageKey.TryGetValue(packageKey, out string? archivePath) ||
                            string.IsNullOrWhiteSpace(archivePath))
                        {
                            return false;
                        }

                        string fullArchivePath = Path.GetFullPath(archivePath!);
                        if (!_controlledBuildInputsByArchive.TryGetValue(
                                fullArchivePath,
                                out HashSet<string>? packageInputs))
                        {
                            packageInputs = new HashSet<string>(
                                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                            _controlledBuildInputsByArchive.Add(fullArchivePath, packageInputs);
                        }
                        packageInputs.Add(string.Join("/", segments.Skip(2)));
                        break;
                    }
                }
                return true;
            }
            catch
            {
                _controlledBuildInputsByArchive.Clear();
                return false;
            }
        }

        internal bool TrySeedControlledPackageSource(
            string destination,
            string controlledSourceRoot,
            string controlledProjectPath,
            out string[] packageSources,
            IReadOnlyDictionary<string, string> evaluatedProperties,
            bool allowSdkManagedToolchainPackages = false)
            => _archives.TrySeedControlledPackageSource(
                destination,
                _archivePathsByPackageKey,
                _sdkManagedArchivePaths,
                _controlledBuildInputsByArchive,
                controlledSourceRoot,
                controlledProjectPath,
                out packageSources,
                evaluatedProperties,
                allowSdkManagedToolchainPackages);
    }

    private sealed partial class VerifiedPackageArchiveCache
    {
        internal bool TrySeedVerifiedRestoreSource(
            string destination,
            IReadOnlyDictionary<string, string> archivePathsByPackageKey)
        {
            try
            {
                Directory.CreateDirectory(destination);
                foreach (KeyValuePair<string, string> package in archivePathsByPackageKey.OrderBy(
                             entry => entry.Key,
                             StringComparer.OrdinalIgnoreCase))
                {
                    string fullArchivePath = Path.GetFullPath(package.Value);
                    if (!_archives.TryGetValue(fullArchivePath, out CacheEntry? cached))
                        return false;

                    int separator = package.Key.LastIndexOf('|');
                    if (separator <= 0 || separator == package.Key.Length - 1)
                        return false;
                    string packageId = package.Key.Substring(0, separator);
                    string packageVersion = package.Key.Substring(separator + 1);
                    string destinationPath = Path.Combine(
                        destination,
                        packageId + "." + packageVersion + ".nupkg");
                    if (File.Exists(destinationPath))
                        return false;
                    cached.Archive.CopyTo(destinationPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal bool TrySeedControlledPackageSource(
            string destination,
            IReadOnlyDictionary<string, string> archivePathsByPackageKey,
            HashSet<string> sdkManagedArchivePaths,
            IReadOnlyDictionary<string, HashSet<string>> controlledBuildInputsByArchive,
            string controlledSourceRoot,
            string controlledProjectPath,
            out string[] packageSources,
            IReadOnlyDictionary<string, string> evaluatedProperties,
            bool allowSdkManagedToolchainPackages)
        {
            packageSources = Array.Empty<string>();
            try
            {
                Directory.CreateDirectory(destination);
                foreach (KeyValuePair<string, string> package in archivePathsByPackageKey.OrderBy(
                             entry => entry.Key,
                             StringComparer.OrdinalIgnoreCase))
                {
                    string archivePath = package.Value;
                    if (!_archives.TryGetValue(Path.GetFullPath(archivePath), out CacheEntry? cached))
                    {
                        return false;
                    }
                    IReadOnlyCollection<string> controlledBuildInputs =
                        controlledBuildInputsByArchive.TryGetValue(
                            Path.GetFullPath(archivePath),
                            out HashSet<string>? inputs)
                            ? inputs
                            : Array.Empty<string>();
                    if (!(allowSdkManagedToolchainPackages &&
                          sdkManagedArchivePaths.Contains(Path.GetFullPath(archivePath))) &&
                        !cached.Archive.HasOnlyControlledBuildInputs(
                            controlledBuildInputs,
                            controlledSourceRoot,
                            controlledProjectPath,
                            evaluatedProperties))
                    {
                        return false;
                    }
                    int separator = package.Key.LastIndexOf('|');
                    if (separator <= 0 || separator == package.Key.Length - 1)
                        return false;
                    string packageId = package.Key.Substring(0, separator);
                    string packageVersion = package.Key.Substring(separator + 1);
                    string destinationPath = Path.Combine(
                        destination,
                        packageId + "." + packageVersion + ".nupkg");
                    if (File.Exists(destinationPath))
                    {
                        return false;
                    }
                    cached.Archive.CopyTo(destinationPath);
                }
                packageSources = [destination];
                return true;
            }
            catch
            {
                packageSources = Array.Empty<string>();
                return false;
            }
        }

    }

    private sealed partial class VerifiedPackageArchive
    {
        internal bool HasOnlyControlledBuildInputs(
            IReadOnlyCollection<string> executableBuildInputs,
            string controlledSourceRoot,
            string controlledProjectPath,
            IReadOnlyDictionary<string, string> evaluatedProperties)
        {
            try
            {
                string controlledProjectDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(controlledProjectPath))!;
                if (!IsSameOrBelowBuildInputPath(controlledProjectDirectory, controlledSourceRoot))
                    return false;
                var executableSeedNames = new HashSet<string>(
                    executableBuildInputs.Select(input => input.Replace('\\', '/').TrimStart('/')),
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                var documents = new List<(
                    string Name,
                    string DeclaringPath,
                    string PackageDirectory,
                    bool ExecutableSeed,
                    XDocument Document)>();
                foreach (KeyValuePair<string, ZipArchiveEntry> pair in _entries)
                {
                    string name = pair.Key.Replace('\\', '/').TrimStart('/');
                    string extension = Path.GetExtension(name);
                    string packageDirectory = Path.Combine(
                        "package-root",
                        Path.GetDirectoryName(name) ?? string.Empty);
                    if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    bool executableSeed = executableSeedNames.Contains(name);
                    XDocument document;
                    try
                    {
                        using Stream stream = pair.Value.Open();
                        document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                    }
                    catch when (!executableSeed)
                    {
                        continue;
                    }
                    if (!executableSeed &&
                        extension.Equals(".resx", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!HasOnlyControlledResourceFileInputs(
                                document,
                                Path.Combine("package-root", name.Replace('/', Path.DirectorySeparatorChar)),
                                "package-root",
                                IsControlledPackageInput))
                        {
                            return false;
                        }
                        continue;
                    }
                    if (document.Root is null ||
                        !document.Root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase))
                    {
                        if (executableSeed)
                            return false;
                        continue;
                    }
                    documents.Add((
                        name,
                        Path.Combine("package-root", name.Replace('/', Path.DirectorySeparatorChar)),
                        packageDirectory,
                        executableSeed,
                        document));
                }

                if (executableSeedNames.Any(seed => !documents.Any(document =>
                        document.Name.Equals(
                            seed,
                            IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                {
                    return false;
                }

                var executableNames = new HashSet<string>(
                    documents.Where(candidate => candidate.ExecutableSeed).Select(candidate => candidate.Name),
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

                var controlledDocuments = new List<XDocument>();
                var controlledDocumentSources = new List<(XDocument Document, string DeclaringPath)>();
                var executableDocuments = new List<(XDocument Document, string DeclaringPath)>();
                foreach ((string name, string declaringPath, string packageDirectory, _, XDocument document) in documents)
                {
                    bool executable = executableNames.Contains(name);
                    if (!executable)
                        continue;
                    controlledDocuments.Add(document);
                    controlledDocumentSources.Add((document, declaringPath));
                    executableDocuments.Add((document, declaringPath));
                    if (document.DescendantNodes()
                            .OfType<XText>()
                            .Select(text => text.Value)
                            .Concat(document.Descendants().Attributes().Select(attribute => attribute.Value))
                            .Any(value => ContainsRootedBuildValue(value, gitRoot: null) ||
                                          ContainsEscapingRelativeBuildValue(
                                              value,
                                              packageDirectory,
                                              "package-root") ||
                                          ContainsUncontrolledEnvironmentReference(value) ||
                                          ContainsUncontrolledAmbientPropertyFunction(value) ||
                                          ContainsUncontrolledFileSystemPropertyFunction(value)))
                    {
                        return false;
                    }
                }
                foreach ((XDocument document, string declaringPath) in executableDocuments)
                {
                    if (ContainsControlledBuildPropertyEscape(document) ||
                        !HasOnlyControlledDocumentTaskFileInputs(
                            document,
                            declaringPath,
                            controlledProjectDirectory,
                            "package-root",
                            controlledSourceRoot,
                            controlledDocumentSources,
                            evaluatedGlobalProperties: evaluatedProperties,
                            controlledProjectPath: controlledProjectPath,
                            isControlledInput: path => IsControlledPackageOrProjectInput(
                                path,
                                controlledSourceRoot),
                            readLines: path => ReadControlledPackageOrProjectTextInput(
                                path,
                                controlledSourceRoot)))
                    {
                        return false;
                    }
                }
                return !controlledDocuments.Any(document =>
                        ContainsUncontrolledControlledBuildTask(
                            document,
                            controlledDocuments,
                            evaluatedProperties));
            }
            catch
            {
                return false;
            }
        }

        private bool IsControlledPackageInput(string path)
        {
            if (!TryGetControlledPackageEntryName(path, out string name))
                return false;
            if (!name.EndsWith("/", StringComparison.Ordinal))
                return _entries.ContainsKey(name);

            return _entries.Keys.Any(entry => entry.StartsWith(
                name,
                IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }

        private bool IsControlledPackageOrProjectInput(string path, string controlledSourceRoot)
        {
            if (IsControlledPackageInput(path))
                return true;
            try
            {
                string fullPath = Path.GetFullPath(path);
                return IsSameOrBelowBuildInputPath(fullPath, controlledSourceRoot) &&
                       (File.Exists(fullPath) || Directory.Exists(fullPath)) &&
                       !HasReparsePointBelowRoot(fullPath, controlledSourceRoot);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetControlledPackageEntryName(string path, out string name)
        {
            name = string.Empty;
            try
            {
                string packageRoot = Path.GetFullPath("package-root");
                string fullPath = Path.GetFullPath(path);
                if (!IsSameOrBelowBuildInputPath(fullPath, packageRoot))
                    return false;
                name = FrameworkCompatibility.GetRelativePath(packageRoot, fullPath)
                    .Replace('\\', '/')
                    .TrimStart('/');
                return name.Length > 0;
            }
            catch
            {
                name = string.Empty;
                return false;
            }
        }

        private string[]? ReadControlledPackageTextInput(string path)
        {
            try
            {
                if (!TryGetControlledPackageEntryName(path, out string relativePath) ||
                    !_entries.TryGetValue(relativePath, out ZipArchiveEntry? entry) ||
                    entry.Length > MaximumControlledBuildTextInputBytes)
                {
                    return null;
                }

                using Stream stream = entry.Open();
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (reader.ReadLine() is string line)
                    lines.Add(line);
                return lines.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private string[]? ReadControlledPackageOrProjectTextInput(
            string path,
            string controlledSourceRoot)
        {
            string[]? packageLines = ReadControlledPackageTextInput(path);
            if (packageLines is not null)
                return packageLines;
            try
            {
                string fullPath = Path.GetFullPath(path);
                var file = new FileInfo(fullPath);
                return IsSameOrBelowBuildInputPath(fullPath, controlledSourceRoot) &&
                       file.Exists &&
                       file.Length <= MaximumControlledBuildTextInputBytes &&
                       !HasReparsePointBelowRoot(fullPath, controlledSourceRoot)
                    ? File.ReadAllLines(fullPath)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        internal void CopyTo(string destination)
        {
            _stream.Position = 0;
            using FileStream output = File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _stream.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
    }
}
