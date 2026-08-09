using System.Reflection;

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
        WebApiDocsOptions options)
    {
        var combined = new ApiDocModel();
        foreach (var xmlPath in xmlPaths)
        {
            var parsed = ParseXml(xmlPath, assembly, options);
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
