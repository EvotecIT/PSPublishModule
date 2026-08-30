using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledGenerateResourceSourcePaths(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        Func<string, string[]?> readLines)
    {
        foreach (XElement task in document.Descendants().Where(element =>
                     IsControlledBuildTaskElement(element) &&
                     element.Name.LocalName.Equals("GenerateResource", StringComparison.OrdinalIgnoreCase)))
        {
            if (evaluatedGlobalProperties is not null &&
                IsDefinitelyInactiveControlledBuildOperation(
                    task,
                    evaluatedGlobalProperties,
                    declaringPath))
            {
                continue;
            }

            XAttribute? sources = task.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("Sources", StringComparison.OrdinalIgnoreCase));
            if (sources is null || string.IsNullOrWhiteSpace(sources.Value))
                continue;

            if (!TryExpandControlledTaskInputValues(
                    sources.Value,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedSources,
                    consumingElement: task))
            {
                return false;
            }

            bool hasFileReference = false;
            foreach (string candidate in expandedSources
                         .SelectMany(value => DecodeMsBuildEscapes(value).Split(';'))
                         .Select(value => value.Trim().Trim('\'', '"'))
                         .Where(value => value.Length > 0))
            {
                if (!candidate.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!TryResolveControlledTaskInputPath(
                        candidate,
                        declaringPath,
                        taskInputBaseDirectory,
                        declaringAllowedRoot,
                        taskInputAllowedRoot,
                        out string sourcePath))
                {
                    return false;
                }
                string[]? lines = readLines(sourcePath);
                if (lines is null)
                    return false;
                XDocument resource;
                try
                {
                    resource = XDocument.Parse(string.Join(Environment.NewLine, lines));
                }
                catch
                {
                    return false;
                }
                if (resource.Descendants().Any(element =>
                        (element.Name.LocalName.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                         element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase)) &&
                        element.Attributes().Any(attribute =>
                            attribute.Name.LocalName.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                            attribute.Value.Split(',')[0].Trim().Equals(
                                "System.Resources.ResXFileRef",
                                StringComparison.OrdinalIgnoreCase))))
                {
                    hasFileReference = true;
                    break;
                }
            }

            if (!hasFileReference)
                continue;

            XAttribute? useSourcePath = task.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("UseSourcePath", StringComparison.OrdinalIgnoreCase));
            if (useSourcePath is null ||
                !TryExpandControlledTaskInputValues(
                    useSourcePath.Value,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedValues,
                    consumingElement: task) ||
                expandedValues.Length != 1 ||
                !bool.TryParse(DecodeMsBuildEscapes(expandedValues[0]).Trim(), out bool enabled) ||
                !enabled)
            {
                return false;
            }
        }

        return true;
    }
}
