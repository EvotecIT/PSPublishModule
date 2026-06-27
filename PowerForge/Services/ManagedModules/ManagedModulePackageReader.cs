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
            UncompressedBytes = CountUncompressedBytes(archive),
            Dependencies = MergeDependencies(nuspecDependencies, manifest.Dependencies),
            ModuleManifestPath = manifest.Path,
            ModuleManifestVersion = manifest.Version,
            ModuleManifestPrerelease = manifest.Prerelease,
            ManifestDependencies = manifest.Dependencies,
            PackagePath = fullPath
        };

        if (string.IsNullOrWhiteSpace(result.Id))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain an id.");
        if (string.IsNullOrWhiteSpace(result.Version))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain a version.");

        return result;
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
        var version = ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, "ModuleVersion", out var manifestVersion)
            ? manifestVersion
            : null;
        var prerelease = ReadPsDataStringOrArray(manifestText, "Prerelease").FirstOrDefault();
        var dependencies = ReadManifestDependencies(manifestText);

        return new ManifestMetadata(
            NormalizePackagePath(manifest.FullName),
            version,
            prerelease,
            dependencies);
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
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.TargetFramework, StringComparer.OrdinalIgnoreCase)
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
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
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

    private static IReadOnlyList<ManagedModuleDependencyInfo> MergeDependencies(
        IReadOnlyList<ManagedModuleDependencyInfo> nuspecDependencies,
        IReadOnlyList<ManagedModuleDependencyInfo> manifestDependencies)
    {
        if (nuspecDependencies.Count == 0)
            return manifestDependencies;
        if (manifestDependencies.Count == 0)
            return nuspecDependencies;

        var results = new List<ManagedModuleDependencyInfo>(nuspecDependencies);
        var nuspecIds = new HashSet<string>(nuspecDependencies.Select(static dependency => dependency.Id), StringComparer.OrdinalIgnoreCase);
        results.AddRange(manifestDependencies.Where(dependency => !nuspecIds.Contains(dependency.Id)));
        return results
            .OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadPsDataStringOrArray(string manifestText, string key)
    {
        if (ModuleManifestTextParser.TryReadPsDataAssignedExpression(manifestText, key, out var expression) &&
            !string.IsNullOrWhiteSpace(expression))
        {
            if (ModuleManifestTextParser.TryParseStringArrayExpression(expression!, out var values) && values is not null)
                return values;
            if (ModuleManifestTextParser.TryParseQuotedStringExpression(expression!, out var value) && !string.IsNullOrWhiteSpace(value))
                return new[] { value! };
        }

        if (ModuleManifestTextParser.TryGetPsDataStringArrayValue(manifestText, key, out var legacyValues) && legacyValues is not null)
            return legacyValues;
        if (ModuleManifestTextParser.TryGetPsDataStringValue(manifestText, key, out var legacyValue) && !string.IsNullOrWhiteSpace(legacyValue))
            return new[] { legacyValue! };

        return Array.Empty<string>();
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
        {
            Path = path;
            Version = version;
            Prerelease = prerelease;
            Dependencies = dependencies;
        }

        public string? Path { get; }

        public string? Version { get; }

        public string? Prerelease { get; }

        public IReadOnlyList<ManagedModuleDependencyInfo> Dependencies { get; }

        public static ManifestMetadata Empty { get; } = new(
            null,
            null,
            null,
            Array.Empty<ManagedModuleDependencyInfo>());
    }
}
