using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ISet<string> ControlledPublishFileItemNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ResolvedFileToPublish",
            "_ResolvedFileToPublishAlways",
            "_ResolvedFileToPublishIfDifferent",
            "_ResolvedFileToPublishPreserveNewest",
            "_SourceItemsToCopyToPublishDirectory",
            "_SourceItemsToCopyToPublishDirectoryAlways",
            "_SourceItemsToCopyToPublishDirectoryIfDifferent"
        };

    private static bool HasOnlyControlledPublishItemInputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        Func<string, bool>? isControlledInput)
    {
        foreach (XElement item in document.Descendants().Where(element =>
                     ControlledPublishFileItemNames.Contains(element.Name.LocalName) &&
                     element.Ancestors().Any(ancestor =>
                         ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
        {
            foreach (XAttribute attribute in item.Attributes().Where(attribute =>
                         attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                         attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase)))
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
                            out string inputPath))
                    {
                        return false;
                    }

                    if (isControlledInput is null)
                    {
                        if (File.Exists(inputPath) &&
                            HasReparsePointBelowRoot(inputPath, taskInputAllowedRoot))
                        {
                            return false;
                        }
                    }
                    else if (File.Exists(inputPath) && !isControlledInput(inputPath))
                    {
                        return false;
                    }
                }
            }

            IEnumerable<string> relativePaths = item.Attributes()
                .Where(attribute => IsPublishRelativePathMetadata(attribute.Name.LocalName))
                .Select(attribute => attribute.Value)
                .Concat(item.Elements()
                    .Where(element => IsPublishRelativePathMetadata(element.Name.LocalName))
                    .Select(element => element.Value));
            foreach (string relativePath in relativePaths)
            {
                if (!TryExpandControlledTaskInputValues(
                        relativePath,
                        declaringPath,
                        taskInputBaseDirectory,
                        relatedDocuments,
                        evaluatedGlobalProperties,
                        out string[] expandedValues) ||
                    expandedValues.Any(value => !IsControlledPublishRelativePath(value)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsPublishRelativePathMetadata(string name)
        => name.Equals("RelativePath", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TargetPath", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Link", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("DestinationSubPath", StringComparison.OrdinalIgnoreCase);
}
