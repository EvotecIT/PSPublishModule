using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadControlledToolchainTargetNames(
        IReadOnlyCollection<string> rootEvaluatedImports,
        IReadOnlyDictionary<string, string> rootEvaluatedProperties,
        IReadOnlyCollection<ControlledPublishGraphNode> graphNodes,
        out HashSet<string> names)
    {
        var collectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        names = collectedNames;
        IEnumerable<string> ToolchainImports()
        {
            foreach (string path in rootEvaluatedImports)
                if (IsControlledToolchainImport(path, rootEvaluatedProperties))
                    yield return path;
            foreach (ControlledPublishGraphNode node in graphNodes)
                foreach (string path in node.EvaluatedImports)
                    if (IsControlledToolchainImport(path, node.EvaluatedProperties))
                        yield return path;
        }

        try
        {
            foreach (string path in ToolchainImports().Distinct(FileSystemPathSafety.ExistingPathComparer))
            {
                XDocument document = XDocument.Load(path, LoadOptions.None);
                foreach (XElement element in document.Descendants())
                {
                    if (element.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))
                    {
                        string? targetName = element.Attribute("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(targetName))
                            collectedNames.Add(targetName!.Trim());
                    }

                    // SDK target references can reach a custom target even without a
                    // BeforeTargets or AfterTargets hook in the project source.
                    if (element.Parent?.Name.LocalName.Equals("PropertyGroup",
                            StringComparison.OrdinalIgnoreCase) == true)
                        AddLiteralTargetNames(element.Value);
                    foreach (XAttribute attribute in element.Attributes().Where(attribute =>
                                 new[] { "DependsOnTargets", "Targets", "ExecuteTargets",
                                     "InitialTargets", "DefaultTargets" }.Contains(
                                     attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
                        AddLiteralTargetNames(attribute.Value);
                }
            }
            return true;
        }
        catch
        {
            collectedNames.Clear();
            return false;
        }

        void AddLiteralTargetNames(string value)
        {
            foreach (string segment in value.Split(';'))
            {
                string candidate = segment.Trim();
                if (Regex.IsMatch(candidate, @"^[A-Za-z_][A-Za-z0-9_.-]*$",
                        RegexOptions.CultureInvariant))
                    collectedNames.Add(candidate);
            }
        }
    }
}
