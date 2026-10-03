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

    private static ApiDocModel ParseXmlDocuments(
        IReadOnlyList<string> xmlPaths,
        Assembly? assembly,
        WebApiDocsOptions options,
        List<string> warnings)
    {
        var combined = new ApiDocModel();
        var documents = xmlPaths.Where(File.Exists)
            .Select(path => (Path: path, Document: LoadXmlDocumentation(path))).ToArray();
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var source in documents)
            foreach (var member in source.Document.Root!.Element("members")!.Elements("member"))
                if (member.Attribute("name")?.Value is { Length: > 0 } name) members.TryAdd(name, member);
        if (assembly is not null) ResolveImplicitInheritDoc(assembly, members, warnings);
        foreach (var source in documents)
        {
            var xmlPath = source.Path;
            var parsed = ParseXml(source.Document, assembly, options, members);
            combined.AssemblyName ??= parsed.AssemblyName;
            combined.AssemblyVersion ??= parsed.AssemblyVersion;

            foreach (var pair in parsed.Types)
            {
                if (!pair.Value.OriginFiles.Contains(xmlPath, StringComparer.OrdinalIgnoreCase))
                    pair.Value.OriginFiles.Add(xmlPath);
                combined.Types.TryAdd(pair.Key, pair.Value);
            }
        }

        return combined;
    }
}
