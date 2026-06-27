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

        var result = new ManagedModulePackageMetadata
        {
            Id = ReadElement(metadata, "id") ?? string.Empty,
            Version = ReadElement(metadata, "version") ?? string.Empty,
            Authors = ReadElement(metadata, "authors"),
            Description = ReadElement(metadata, "description"),
            ProjectUrl = ReadElement(metadata, "projectUrl"),
            License = ReadLicense(metadata),
            Tags = ReadTags(ReadElement(metadata, "tags")),
            Dependencies = ReadDependencies(metadata),
            PackagePath = fullPath
        };

        if (string.IsNullOrWhiteSpace(result.Id))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain an id.");
        if (string.IsNullOrWhiteSpace(result.Version))
            throw new InvalidOperationException($"Package '{fullPath}' nuspec does not contain a version.");

        return result;
    }

    private static string? ReadElement(XElement parent, string localName)
        => parent.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

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
}
