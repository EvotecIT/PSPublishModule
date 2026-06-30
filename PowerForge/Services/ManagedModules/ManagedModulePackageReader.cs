using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Reads managed module package metadata without executing package content.
/// </summary>
public sealed class ManagedModulePackageReader
{
    private const int MaxEntries = 5000;
    private const long MaxManifestBytes = 5L * 1024L * 1024L;
    private const long MaxPackageBytes = 1024L * 1024L * 1024L;
    private const long MaxUncompressedPackageBytes = 2L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Reads nuspec metadata from a NuGet package.
    /// </summary>
    /// <param name="packagePath">Path to the package.</param>
    /// <returns>Package metadata.</returns>
    public ManagedModulePackageMetadata ReadMetadata(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));

        var fullPath = Path.GetFullPath(packagePath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Package file was not found: {fullPath}", fullPath);

        var info = new FileInfo(fullPath);
        if (info.Length > MaxPackageBytes)
            throw new InvalidOperationException($"Package '{fullPath}' exceeds the managed module metadata size limit.");

        using var archive = ZipFile.OpenRead(fullPath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidOperationException($"Package '{fullPath}' exceeds the managed module metadata entry-count limit.");

        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizePackagePath(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (!IsSafePackagePath(normalized))
                throw new InvalidOperationException($"Package '{fullPath}' contains unsafe path '{entry.FullName}'.");
        }

        var nuspec = archive.Entries
            .Where(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.FullName.Count(ch => ch == '/' || ch == '\\'))
            .ThenBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (nuspec is null)
            throw new InvalidOperationException($"Package '{fullPath}' does not contain a nuspec file.");

        using var stream = nuspec.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var metadata = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "metadata")
                       ?? throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain metadata.");

        var packageId = ReadElement(metadata, "id") ?? string.Empty;
        var nuspecDependencies = ReadDependencies(metadata);
        var manifest = ReadManifestMetadata(archive, packageId, fullPath);
        var uncompressedBytes = CountUncompressedBytes(archive);
        if (uncompressedBytes > MaxUncompressedPackageBytes)
            throw new InvalidOperationException($"Package '{fullPath}' exceeds the managed module uncompressed size limit.");

        var result = new ManagedModulePackageMetadata
        {
            Id = packageId,
            Version = ReadElement(metadata, "version") ?? string.Empty,
            Authors = ReadElement(metadata, "authors"),
            Description = ReadElement(metadata, "description"),
            ProjectUrl = ReadElement(metadata, "projectUrl"),
            License = ReadLicense(metadata),
            RequireLicenseAcceptance = ReadBoolean(metadata, "requireLicenseAcceptance"),
            Tags = ReadTags(ReadElement(metadata, "tags")),
            FileCount = CountFiles(archive),
            PackageBytes = info.Length,
            UncompressedBytes = uncompressedBytes,
            Dependencies = SelectInstallableDependencies(nuspecDependencies, manifest.Dependencies, manifest.ExternalModuleDependencies),
            ModuleManifestPath = manifest.Path,
            ModuleManifestVersion = manifest.Version,
            ModuleManifestPrerelease = manifest.Prerelease,
            ManifestDependencies = manifest.Dependencies,
            ManifestExternalModuleDependencies = manifest.ExternalModuleDependencies,
            PackagePath = fullPath
        };

        if (string.IsNullOrWhiteSpace(result.Id))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain an id.");
        if (string.IsNullOrWhiteSpace(result.Version))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain a version.");
        ValidateManifestAgreement(result, fullPath);

        return result;
    }

    private static void ValidateManifestAgreement(ManagedModulePackageMetadata metadata, string packagePath)
    {
        if (string.IsNullOrWhiteSpace(metadata.ModuleManifestPath))
            return;

        var manifestName = Path.GetFileNameWithoutExtension(metadata.ModuleManifestPath!.Replace('/', Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(manifestName) &&
            !manifestName.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Package '{packagePath}' nuspec id '{metadata.Id}' does not match module manifest '{metadata.ModuleManifestPath}'.");
        }

        if (string.IsNullOrWhiteSpace(metadata.ModuleManifestVersion))
            return;

        var manifestVersion = CombineManifestVersion(metadata.ModuleManifestVersion!, metadata.ModuleManifestPrerelease);
        if (ManagedModuleVersionComparer.Instance.Compare(manifestVersion, metadata.Version) != 0)
        {
            throw new InvalidOperationException(
                $"Package '{packagePath}' nuspec version '{metadata.Version}' does not match module manifest version '{manifestVersion}'.");
        }
    }

    private static string CombineManifestVersion(string version, string? prerelease)
    {
        var trimmedVersion = version.Trim();
        if (string.IsNullOrWhiteSpace(prerelease) || trimmedVersion.IndexOf('-') >= 0)
            return trimmedVersion;

        return trimmedVersion + "-" + prerelease!.Trim();
    }

    private static ManifestMetadata ReadManifestMetadata(ZipArchive archive, string packageId, string packagePath)
    {
        var manifest = SelectManifestEntry(archive, packageId);
        if (manifest is null)
            return ManifestMetadata.Empty;
        if (manifest.Length > MaxManifestBytes)
            throw new InvalidOperationException($"Package '{packagePath}' module manifest exceeds the managed module metadata size limit.");

        using var stream = manifest.Open();
        using var reader = new StreamReader(stream);
        var manifestText = reader.ReadToEnd();
        var version = ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, "ModuleVersion", out var manifestVersion)
            ? manifestVersion
            : null;
        var prerelease = ModuleManifestValueReader.ReadPsDataStringOrArrayFromText(manifestText, "Prerelease").FirstOrDefault();
        var dependencies = ReadManifestDependencies(manifestText);
        var externalDependencies = ModuleManifestValueReader.ReadPsDataStringOrArrayFromText(manifestText, "ExternalModuleDependencies")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ManifestMetadata(
            NormalizePackagePath(manifest.FullName),
            version,
            prerelease,
            dependencies,
            externalDependencies);
    }

    private static ZipArchiveEntry? SelectManifestEntry(ZipArchive archive, string packageId)
    {
        var manifests = archive.Entries
            .Where(static entry => entry.FullName.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            .Where(static entry => !string.IsNullOrWhiteSpace(NormalizePackagePath(entry.FullName)))
            .ToArray();
        if (manifests.Length == 0)
            return null;

        var expectedName = packageId + ".psd1";
        return manifests
            .OrderByDescending(entry => Path.GetFileName(entry.FullName).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(static entry => NormalizePackagePath(entry.FullName).Count(ch => ch == '/'))
            .ThenBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int CountFiles(ZipArchive archive)
        => archive.Entries.Count(static entry => !string.IsNullOrWhiteSpace(entry.Name));

    private static long CountUncompressedBytes(ZipArchive archive)
        => archive.Entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Sum(static entry => entry.Length);

    private static string? ReadElement(XElement parent, string localName)
        => parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

    private static bool ReadBoolean(XElement parent, string localName)
        => bool.TryParse(ReadElement(parent, localName), out var value) && value;

    private static string? ReadLicense(XElement metadata)
    {
        var license = metadata.Elements()
            .FirstOrDefault(static element => element.Name.LocalName.Equals("license", StringComparison.OrdinalIgnoreCase));
        if (license is null)
            return null;

        var type = license.Attribute("type")?.Value?.Trim();
        var value = license.Value?.Trim();
        return string.IsNullOrWhiteSpace(type) ? value : $"{type}:{value}";
    }

    private static IReadOnlyList<string> ReadTags(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? Array.Empty<string>()
            : tags!.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static tag => tag.Trim())
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IReadOnlyList<ManagedModuleDependencyInfo> ReadDependencies(XElement metadata)
    {
        var dependencies = metadata.Elements()
            .FirstOrDefault(static element => element.Name.LocalName.Equals("dependencies", StringComparison.OrdinalIgnoreCase));
        if (dependencies is null)
            return Array.Empty<ManagedModuleDependencyInfo>();

        var results = new List<ManagedModuleDependencyInfo>();
        foreach (var directDependency in dependencies.Elements().Where(static element => element.Name.LocalName.Equals("dependency", StringComparison.OrdinalIgnoreCase)))
            AddDependency(results, directDependency, targetFramework: null);

        foreach (var group in dependencies.Elements().Where(static element => element.Name.LocalName.Equals("group", StringComparison.OrdinalIgnoreCase)))
        {
            var targetFramework = group.Attribute("targetFramework")?.Value?.Trim();
            foreach (var dependency in group.Elements().Where(static element => element.Name.LocalName.Equals("dependency", StringComparison.OrdinalIgnoreCase)))
                AddDependency(results, dependency, targetFramework);
        }

        return results
            .Where(static dependency => !string.IsNullOrWhiteSpace(dependency.Id))
            .ToArray();
    }

    private static IReadOnlyList<ManagedModuleDependencyInfo> ReadManifestDependencies(string manifestText)
    {
        if (!ModuleManifestTextParser.TryGetRequiredModules(manifestText, out var modules) || modules is null)
            return Array.Empty<ManagedModuleDependencyInfo>();

        return modules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => new ManagedModuleDependencyInfo
            {
                Id = module.ModuleName,
                VersionRange = ToVersionRange(module),
                TargetFramework = null
            })
            .ToArray();
    }

    private static string? ToVersionRange(RequiredModuleReference module)
    {
        if (!string.IsNullOrWhiteSpace(module.RequiredVersion))
            return "[" + module.RequiredVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion) && !string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "[" + module.ModuleVersion!.Trim() + "," + module.MaximumVersion!.Trim() + "]";
        if (!string.IsNullOrWhiteSpace(module.ModuleVersion))
            return module.ModuleVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(module.MaximumVersion))
            return "(," + module.MaximumVersion!.Trim() + "]";

        return null;
    }

    private static IReadOnlyList<ManagedModuleDependencyInfo> SelectInstallableDependencies(
        IReadOnlyList<ManagedModuleDependencyInfo> nuspecDependencies,
        IReadOnlyList<ManagedModuleDependencyInfo> manifestDependencies,
        IReadOnlyList<string> manifestExternalModuleDependencies)
    {
        var externalDependencies = manifestExternalModuleDependencies.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : manifestExternalModuleDependencies
                .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(static dependency => dependency.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredNuspecDependencies = FilterExternalDependencies(nuspecDependencies, externalDependencies).ToArray();
        var filteredManifestDependencies = FilterExternalDependencies(manifestDependencies, externalDependencies).ToArray();

        return MergeDependencies(filteredNuspecDependencies, filteredManifestDependencies);
    }

    private static IReadOnlyList<ManagedModuleDependencyInfo> MergeDependencies(
        IReadOnlyList<ManagedModuleDependencyInfo> nuspecDependencies,
        IReadOnlyList<ManagedModuleDependencyInfo> manifestDependencies)
    {
        if (nuspecDependencies.Count == 0)
            return manifestDependencies;
        if (manifestDependencies.Count == 0)
            return nuspecDependencies;

        var results = new List<ManagedModuleDependencyInfo>(manifestDependencies);
        var manifestIds = new HashSet<string>(manifestDependencies.Select(static dependency => dependency.Id), StringComparer.OrdinalIgnoreCase);
        results.AddRange(nuspecDependencies.Where(dependency => !manifestIds.Contains(dependency.Id)));
        return results;
    }

    private static IEnumerable<ManagedModuleDependencyInfo> FilterExternalDependencies(
        IReadOnlyList<ManagedModuleDependencyInfo> dependencies,
        ISet<string> externalDependencies)
    {
        if (dependencies.Count == 0)
            return Array.Empty<ManagedModuleDependencyInfo>();
        if (externalDependencies.Count == 0)
            return dependencies;

        return dependencies.Where(dependency => !externalDependencies.Contains(dependency.Id));
    }

    private static void AddDependency(List<ManagedModuleDependencyInfo> dependencies, XElement dependency, string? targetFramework)
    {
        var id = dependency.Attribute("id")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return;

        dependencies.Add(new ManagedModuleDependencyInfo
        {
            Id = id!,
            VersionRange = dependency.Attribute("version")?.Value?.Trim(),
            TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework
        });
    }

    private static string NormalizePackagePath(string path)
        => path.Replace('\\', '/').Trim('/');

    private static bool IsSafePackagePath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
            normalizedPath.Contains(":", StringComparison.Ordinal))
            return false;

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.All(static part => part != "." && part != "..");
    }

    private sealed class ManifestMetadata
    {
        public ManifestMetadata(
            string? path,
            string? version,
            string? prerelease,
            IReadOnlyList<ManagedModuleDependencyInfo> dependencies)
            : this(path, version, prerelease, dependencies, Array.Empty<string>())
        {
        }

        public ManifestMetadata(
            string? path,
            string? version,
            string? prerelease,
            IReadOnlyList<ManagedModuleDependencyInfo> dependencies,
            IReadOnlyList<string> externalModuleDependencies)
        {
            Path = path;
            Version = version;
            Prerelease = prerelease;
            Dependencies = dependencies;
            ExternalModuleDependencies = externalModuleDependencies;
        }

        public string? Path { get; }

        public string? Version { get; }

        public string? Prerelease { get; }

        public IReadOnlyList<ManagedModuleDependencyInfo> Dependencies { get; }

        public IReadOnlyList<string> ExternalModuleDependencies { get; }

        public static ManifestMetadata Empty { get; } = new(
            null,
            null,
            null,
            Array.Empty<ManagedModuleDependencyInfo>(),
            Array.Empty<string>());
    }
}
