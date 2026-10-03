using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;

namespace PowerForge.Web;

/// <summary>Discovers local XML records available for assembly-backed inheritance and cache integrity.</summary>
internal static class WebApiDocumentationInputs
{
    internal static IReadOnlyList<string> Discover(string assemblyPath)
    {
        var assembly = Path.GetFullPath(assemblyPath);
        var directory = Path.GetDirectoryName(assembly)!;
        var inputs = Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly).ToList();
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Append("System.Runtime").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var framework = ReadFramework(metadata);
        if (framework?.Identifier == ".NETCoreApp")
        {
            var tfm = $"net{framework.Version.Major}.{framework.Version.Minor}";
            var roots = new List<string>();
            var configuredRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot)) roots.Add(configuredRoot);
            var runtimeRoot = Directory.GetParent(typeof(object).Assembly.Location)?.Parent?.Parent?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(runtimeRoot)) roots.Add(runtimeRoot);
            var packRoots = roots.Select(root => Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref"))
                .Concat(WebApiDocsGenerator.GetApiDocsNuGetPackageRootCandidates(assembly)
                    .Select(root => Path.Combine(root, "microsoft.netcore.app.ref")));
            var candidates = packRoots.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories)
                .Select(path => (Path: Path.Combine(path, "ref", tfm), Version: ParseVersion(Path.GetFileName(path))))
                .Where(candidate => candidate.Version is not null &&
                    candidate.Version.Major == framework.Version.Major && candidate.Version.Minor == framework.Version.Minor &&
                    Directory.Exists(candidate.Path))
                .OrderByDescending(candidate => candidate.Version).ToArray();
            if (candidates.Length > 0)
                foreach (var name in names)
                {
                    var xml = Path.Combine(candidates[0].Path, name + ".xml");
                    if (File.Exists(xml)) inputs.Add(xml);
                }
        }
        return inputs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Version? ParseVersion(string value)
        => Version.TryParse(value.Split('-')[0], out var version) ? version : null;

    private static FrameworkName? ReadFramework(MetadataReader reader)
    {
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)parent);
            if (reader.GetString(type.Name) != "TargetFrameworkAttribute" ||
                reader.GetString(type.Namespace) != "System.Runtime.Versioning") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) continue;
            var name = blob.ReadSerializedString();
            return string.IsNullOrWhiteSpace(name) ? null : new FrameworkName(name);
        }
        return null;
    }
}
