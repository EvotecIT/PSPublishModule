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
            out string[] archivePaths)
        {
            var paths = new List<string>();
            archivePaths = Array.Empty<string>();
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
                        .FirstOrDefault(candidate => Path.GetFileName(candidate).Equals(
                            expectedName,
                            StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(archivePath) ||
                        archives.TryGetOrOpen(archivePath!, package.Value) is null)
                    {
                        return false;
                    }
                    paths.Add(Path.GetFullPath(archivePath!));
                }
                archivePaths = paths.ToArray();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal bool TrySeedControlledPackageSource(string destination)
            => _archives.TrySeedControlledPackageSource(destination, _archivePaths);
    }

    private sealed partial class VerifiedPackageArchiveCache
    {
        internal bool TrySeedControlledPackageSource(
            string destination,
            IReadOnlyCollection<string> archivePaths)
        {
            try
            {
                Directory.CreateDirectory(destination);
                foreach (string archivePath in archivePaths.OrderBy(path => path, StringComparer.Ordinal))
                {
                    if (!_archives.TryGetValue(Path.GetFullPath(archivePath), out CacheEntry? cached))
                        return false;
                    if (!cached.Archive.HasOnlyControlledBuildInputs())
                        return false;
                    string destinationPath = Path.Combine(destination, Path.GetFileName(cached.SourcePath));
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
    }

    private sealed partial class VerifiedPackageArchive
    {
        internal bool HasOnlyControlledBuildInputs()
        {
            try
            {
                var documents = new List<(
                    string Name,
                    string DeclaringPath,
                    string PackageDirectory,
                    bool KnownProjectExtension,
                    XDocument Document)>();
                foreach (KeyValuePair<string, ZipArchiveEntry> pair in _entries)
                {
                    string name = pair.Key.Replace('\\', '/').TrimStart('/');
                    string extension = Path.GetExtension(name);
                    string packageDirectory = Path.Combine(
                        "package-root",
                        Path.GetDirectoryName(name) ?? string.Empty);
                    if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
                    {
                        using Stream responseStream = pair.Value.Open();
                        using var responseReader = new StreamReader(responseStream);
                        while (responseReader.ReadLine() is string value)
                        {
                            if (ContainsExecutableResponseFileSwitch(value) ||
                                ContainsRootedBuildValue(value, gitRoot: null) ||
                                ContainsEscapingRelativeBuildValue(value, packageDirectory, "package-root") ||
                                ContainsUncontrolledEnvironmentReference(value) ||
                                ContainsUncontrolledFileSystemPropertyFunction(value))
                            {
                                return false;
                            }
                        }
                        continue;
                    }
                    bool knownProjectExtension =
                        extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
                    XDocument document;
                    try
                    {
                        using Stream stream = pair.Value.Open();
                        document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                    }
                    catch when (!knownProjectExtension)
                    {
                        continue;
                    }
                    if (!knownProjectExtension &&
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
                    if (!knownProjectExtension &&
                        (document.Root is null ||
                         !document.Root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    documents.Add((
                        name,
                        Path.Combine("package-root", name.Replace('/', Path.DirectorySeparatorChar)),
                        packageDirectory,
                        knownProjectExtension,
                        document));
                }

                var executableNames = new HashSet<string>(
                    documents.Where(candidate => candidate.KnownProjectExtension).Select(candidate => candidate.Name),
                    StringComparer.OrdinalIgnoreCase);
                bool changed;
                do
                {
                    changed = false;
                    foreach ((string name, string declaringPath, _, _, XDocument document) in documents.Where(
                                 candidate => executableNames.Contains(candidate.Name)).ToArray())
                    {
                        foreach (XElement import in document.Descendants().Where(element =>
                                     element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
                        {
                            string? projectValue = import.Attributes()
                                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                                    "Project",
                                    StringComparison.OrdinalIgnoreCase))?
                                .Value;
                            if (string.IsNullOrWhiteSpace(projectValue) ||
                                projectValue!.IndexOf('*') >= 0 ||
                                projectValue.IndexOf('?') >= 0 ||
                                !TryResolveControlledTaskInputPath(
                                    projectValue,
                                    declaringPath,
                                    "package-root",
                                    out string importedPath))
                            {
                                return false;
                            }
                            if (TryGetControlledPackageEntryName(importedPath, out string importedName) &&
                                documents.Any(candidate => candidate.Name.Equals(
                                    importedName,
                                    StringComparison.OrdinalIgnoreCase)) &&
                                executableNames.Add(importedName))
                            {
                                changed = true;
                            }
                        }
                    }
                } while (changed);

                var controlledDocuments = new List<XDocument>();
                foreach ((string name, string declaringPath, string packageDirectory, _, XDocument document) in documents)
                {
                    bool executable = executableNames.Contains(name);
                    if (executable)
                    {
                        controlledDocuments.Add(document);
                        if (ContainsControlledBuildPropertyEscape(document) ||
                            !HasOnlyControlledTaskLoadedFileInputs(
                                document,
                                declaringPath,
                                "package-root",
                                ReadControlledPackageTextInput) ||
                            !HasOnlyControlledLiteralTaskFileInputs(
                                document,
                                declaringPath,
                                "package-root",
                                IsControlledPackageInput))
                        {
                            return false;
                        }
                    }
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
                                          ContainsUncontrolledFileSystemPropertyFunction(value)))
                    {
                        return false;
                    }
                }
                return !controlledDocuments.Any(document =>
                    ContainsUncontrolledControlledBuildTask(document, controlledDocuments));
            }
            catch
            {
                return false;
            }
        }

        private bool IsControlledPackageInput(string path)
            => TryGetControlledPackageEntryName(path, out string name) && _entries.ContainsKey(name);

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

        internal void CopyTo(string destination)
        {
            _stream.Position = 0;
            using FileStream output = File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _stream.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
    }
}
