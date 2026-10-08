using System.Reflection;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static string[] ResolveCSharpXmlPaths(WebApiDocsOptions options)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.XmlPath))
            paths.Add(options.XmlPath);
        if (options.XmlPaths is not null)
            paths.AddRange(options.XmlPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolveCSharpAssemblyPaths(WebApiDocsOptions options)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.AssemblyPath))
            paths.Add(options.AssemblyPath);
        if (options.AssemblyPaths is not null)
            paths.AddRange(options.AssemblyPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ApiDocModel ParseXmlDocuments(
        IReadOnlyList<string> xmlPaths,
        IReadOnlyDictionary<string, Assembly> assemblies,
        WebApiDocsOptions options,
        List<string> warnings)
    {
        var combined = new ApiDocModel();
        var documents = xmlPaths.Where(File.Exists)
            .Select(path => (Path: path, Document: LoadXmlDocumentation(path, warnings))).ToArray();
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var typeOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in documents)
            foreach (var member in source.Document.Root!.Element("members")!.Elements("member"))
                if (member.Attribute("name")?.Value is { Length: > 0 } name) members.TryAdd(name, member);
        foreach (var assembly in assemblies.Values)
            ResolveImplicitInheritDoc(assembly, members, options, warnings);
        foreach (var source in documents)
        {
            var xmlPath = source.Path;
            var declaredAssembly = source.Document.Root!.Element("assembly")?.Element("name")?.Value;
            Assembly? assembly = null;
            if (!string.IsNullOrWhiteSpace(declaredAssembly))
                assemblies.TryGetValue(declaredAssembly, out assembly);
            if (assembly is null && assemblies.Count == 1 && options.AssemblyPaths is not { Count: > 0 })
                assembly = assemblies.Values.First();
            if (assembly is null && assemblies.Count > 0)
                throw new InvalidDataException($"XML documentation assembly '{declaredAssembly}' has no matching configured assembly: {xmlPath}");
            var parsed = ParseXml(source.Document, assembly, options, members);
            combined.AssemblyName ??= parsed.AssemblyName;
            combined.AssemblyVersion ??= parsed.AssemblyVersion;

            foreach (var pair in parsed.Types)
            {
                if (typeOwners.TryGetValue(pair.Key, out var previousOwner) &&
                    !string.Equals(previousOwner, declaredAssembly, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"API type '{pair.Key}' is declared by multiple assemblies: {previousOwner}, {declaredAssembly}.");
                typeOwners[pair.Key] = declaredAssembly ?? string.Empty;
                if (!pair.Value.OriginFiles.Contains(xmlPath, StringComparer.OrdinalIgnoreCase))
                    pair.Value.OriginFiles.Add(xmlPath);
                combined.Types.TryAdd(pair.Key, pair.Value);
            }
        }

        return combined;
    }
}
