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
            VerifiedPackageArchiveCache archives)
        {
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
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed partial class VerifiedPackageArchiveCache
    {
        internal bool TrySeedControlledPackageSource(string destination)
        {
            try
            {
                Directory.CreateDirectory(destination);
                foreach (CacheEntry cached in _archives.Values.OrderBy(
                             entry => entry.ExpectedContentHash,
                             StringComparer.Ordinal))
                {
                    if (!cached.Archive.HasOnlyControlledBuildInputs())
                        return false;
                    string archivePath = Path.Combine(destination, Path.GetFileName(cached.SourcePath));
                    if (File.Exists(archivePath))
                        return false;
                    cached.Archive.CopyTo(archivePath);
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
                foreach (KeyValuePair<string, ZipArchiveEntry> pair in _entries)
                {
                    string name = pair.Key.Replace('\\', '/').TrimStart('/');
                    string extension = Path.GetExtension(name);
                    if (!extension.Equals(".props", StringComparison.OrdinalIgnoreCase) &&
                        !extension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using Stream stream = pair.Value.Open();
                    XDocument document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                    if (ContainsNetworkCapableControlledBuildTask(document) ||
                        ContainsControlledBuildPropertyEscape(document) ||
                        document.DescendantNodes()
                            .OfType<XText>()
                            .Select(text => text.Value)
                            .Concat(document.Descendants().Attributes().Select(attribute => attribute.Value))
                            .Any(value => ContainsRootedBuildValue(value, gitRoot: null)))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
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
