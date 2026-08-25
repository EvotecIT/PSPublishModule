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
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        string? controlledProjectPath)
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
                    if (!TryNormalizeControlledTaskOutputProperties(
                            value,
                            taskInputBaseDirectory,
                            controlledProjectPath,
                            out string candidate) ||
                        candidate.Length == 0 ||
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

                    if (!IsControlledTaskOutputPath(
                            outputPath,
                            declaringAllowedRoot,
                            taskInputAllowedRoot))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool TryNormalizeControlledTaskOutputProperties(
        string value,
        string taskInputBaseDirectory,
        string? controlledProjectPath,
        out string normalizedValue)
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
        if (value.IndexOf("$(MSBuildProjectFullPath)", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (string.IsNullOrWhiteSpace(controlledProjectPath))
            {
                normalizedValue = string.Empty;
                return false;
            }
            value = ReplaceOrdinalIgnoreCase(
                value,
                "$(MSBuildProjectFullPath)",
                Path.GetFullPath(controlledProjectPath!));
        }
        value = ReplaceOrdinalIgnoreCase(
            value,
            "$(TargetPath)",
            Path.Combine(taskInputBaseDirectory, "controlled-output.bin"));
        normalizedValue = value.Trim().Trim('\'', '"');
        return true;
    }

    private static bool IsControlledTaskOutputPath(
        string outputPath,
        string declaringAllowedRoot,
        string taskInputAllowedRoot)
    {
        string allowedRoot = IsSameOrBelowBuildInputPath(outputPath, declaringAllowedRoot)
            ? declaringAllowedRoot
            : taskInputAllowedRoot;
        return !HasReparsePointInExistingAncestors(outputPath, allowedRoot);
    }
}
