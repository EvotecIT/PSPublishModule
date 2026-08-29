using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string[] ReadProjectInitialTargetExpressions(
        string projectPath,
        IReadOnlyCollection<string> importPaths)
    {
        StringComparer comparer = IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var results = new List<string>();
        foreach (string path in new[] { projectPath }.Concat(importPaths).Distinct(comparer))
        {
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            string? expression = document.Root?.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("InitialTargets", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(expression))
                results.Add(ExpandMsBuildThisFileProperties(expression!, path));
        }

        return results.Distinct(StringComparer.Ordinal).ToArray();
    }
}
