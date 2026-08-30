using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledTaskLoadedFileInputs(
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
                     element.Name.LocalName.Equals("ReadLinesFromFile", StringComparison.OrdinalIgnoreCase) &&
                     element.Ancestors().Any(ancestor =>
                         ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
        {
            if (evaluatedGlobalProperties is not null &&
                IsDefinitelyInactiveControlledBuildOperation(
                    task,
                    evaluatedGlobalProperties,
                    declaringPath))
            {
                continue;
            }

            string? fileValue = task.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "File",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (string.IsNullOrWhiteSpace(fileValue) ||
                !TryExpandControlledTaskInputValues(
                    fileValue!,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedFileValues,
                    consumingElement: task) ||
                expandedFileValues.Length == 0)
            {
                return false;
            }

            foreach (string value in expandedFileValues.SelectMany(expanded =>
                         DecodeMsBuildEscapes(expanded).Split(';')))
            {
                string candidate = value.Trim().Trim('\'', '"');
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
                        out string inputPath) ||
                    !HasOnlyControlledTaskLoadedFile(
                        inputPath,
                        declaringPath,
                        taskInputBaseDirectory,
                        declaringAllowedRoot,
                        taskInputAllowedRoot,
                        readLines))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasOnlyControlledTaskLoadedFile(
        string inputPath,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        Func<string, string[]?> readLines)
    {
        if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
            HasReparsePointBelowRoot(inputPath, taskInputAllowedRoot))
        {
            return false;
        }

        string[]? lines = readLines(inputPath);
        if (lines is null || lines.Any(value =>
                ContainsRootedBuildValue(value, taskInputAllowedRoot) ||
                ContainsEscapingRelativeBuildValue(
                    value,
                    taskInputBaseDirectory,
                    taskInputAllowedRoot) ||
                ContainsUncontrolledEnvironmentReference(value) ||
                ContainsUncontrolledAmbientPropertyFunction(value) ||
                ContainsUncontrolledFileSystemPropertyFunction(value) ||
                ContainsUnresolvedBuildExpression(value)))
        {
            return false;
        }

        foreach (string value in lines)
        {
            string candidate = DecodeMsBuildEscapes(value).Trim().Trim('\'', '"');
            if (candidate.Length == 0 || ContainsUnresolvedBuildExpression(candidate))
                continue;
            if (TryResolveControlledTaskInputPath(
                    candidate,
                    declaringPath,
                    taskInputBaseDirectory,
                    declaringAllowedRoot,
                    taskInputAllowedRoot,
                    out string loadedInputPath) &&
                (File.Exists(loadedInputPath) || Directory.Exists(loadedInputPath)) &&
                HasReparsePointBelowRoot(loadedInputPath, taskInputAllowedRoot))
            {
                return false;
            }
        }

        return true;
    }
}
