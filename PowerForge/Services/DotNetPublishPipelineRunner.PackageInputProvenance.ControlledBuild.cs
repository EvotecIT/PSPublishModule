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
                var controlledDocuments = new List<XDocument>();
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
                        (document.Root is null ||
                         !document.Root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    controlledDocuments.Add(document);
                    if (ContainsControlledBuildPropertyEscape(document) ||
                        !HasOnlyControlledTaskLoadedFileInputs(
                            document,
                            Path.Combine("package-root", name.Replace('/', Path.DirectorySeparatorChar)),
                            "package-root",
                            ReadControlledPackageTextInput) ||
                        document.DescendantNodes()
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

        private string[]? ReadControlledPackageTextInput(string path)
        {
            try
            {
                string packageRoot = Path.GetFullPath("package-root");
                string relativePath = FrameworkCompatibility.GetRelativePath(
                        packageRoot,
                        Path.GetFullPath(path))
                    .Replace('\\', '/')
                    .TrimStart('/');
                if (relativePath.Length == 0 ||
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
