using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledLiteralTaskFileOutputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties)
    {
        foreach (XElement task in document.Descendants().Where(IsControlledBuildTaskElement))
        {
            if (!ControlledTaskFileOutputAttributes.TryGetValue(
                    task.Name.LocalName,
                    out string[]? outputAttributes))
            {
                continue;
            }

            foreach (XAttribute attribute in task.Attributes().Where(attribute =>
                         outputAttributes.Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)))
            {
                if (!TryExpandControlledTaskInputValues(
                        attribute.Value,
                        declaringPath,
                        taskInputBaseDirectory,
                        relatedDocuments,
                        evaluatedGlobalProperties,
                        out string[] expandedValues))
                {
                    return false;
                }

                foreach (string value in expandedValues.SelectMany(expanded =>
                             DecodeMsBuildEscapes(expanded).Split(';')))
                {
                    string candidate = NormalizeControlledTaskOutputProperties(
                            value,
                            taskInputBaseDirectory)
                        .Trim()
                        .Trim('\'', '"');
                    if (candidate.Length == 0 ||
                        candidate.IndexOf('*') >= 0 ||
                        candidate.IndexOf('?') >= 0 ||
                        ContainsUnresolvedBuildExpression(candidate) ||
                        !TryResolveControlledTaskInputPath(
                            candidate,
                            declaringPath,
                            taskInputBaseDirectory,
                            declaringAllowedRoot,
                            taskInputAllowedRoot,
                            out string outputPath))
                    {
                        return false;
                    }

                    string allowedRoot = IsSameOrBelowBuildInputPath(outputPath, declaringAllowedRoot)
                        ? declaringAllowedRoot
                        : taskInputAllowedRoot;
                    if (HasReparsePointInExistingAncestors(outputPath, allowedRoot))
                        return false;
                }
            }
        }

        return true;
    }

    private static string NormalizeControlledTaskOutputProperties(
        string value,
        string taskInputBaseDirectory)
    {
        string directory = Path.GetFullPath(taskInputBaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        foreach (string propertyName in new[]
                 {
                     "BaseIntermediateOutputPath",
                     "BaseOutputPath",
                     "IntermediateOutputPath",
                     "MSBuildProjectDirectory",
                     "OutputPath",
                     "TargetDir"
                 })
        {
            value = ReplaceOrdinalIgnoreCase(value, "$(" + propertyName + ")", directory);
        }
        value = ReplaceOrdinalIgnoreCase(
            value,
            "$(MSBuildProjectFullPath)",
            Path.Combine(taskInputBaseDirectory, "controlled.proj"));
        value = ReplaceOrdinalIgnoreCase(
            value,
            "$(TargetPath)",
            Path.Combine(taskInputBaseDirectory, "controlled-output.bin"));
        return value;
    }
}
