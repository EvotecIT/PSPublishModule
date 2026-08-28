using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ISet<string> ControlledTargetFileItemNames =
        new HashSet<string>(CreateEvaluatedBuildItemNames(), StringComparer.OrdinalIgnoreCase);

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
                     (ControlledTargetFileItemNames.Contains(element.Name.LocalName) ||
                      ControlledPublishFileItemNames.Contains(element.Name.LocalName)) &&
                     IsControlledBuildTargetItem(element, relatedDocuments)))
        {
            if (IsAmbientReferenceResolutionItem(item.Name.LocalName))
                return false;

            var resolvedItemInputs = new List<string>();
            bool hasReferenceInclude = false;
            bool hasControlledReferenceInclude = false;
            foreach (XAttribute attribute in item.Attributes().Where(attribute =>
                         attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                         attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase)))
            {
                bool isInclude = attribute.Name.LocalName.Equals(
                    "Include",
                    StringComparison.OrdinalIgnoreCase);
                if (isInclude && item.Name.LocalName.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                    hasReferenceInclude = true;
                if (!TryExpandControlledTaskInputValues(
                        attribute.Value,
                        declaringPath,
                        taskInputBaseDirectory,
                        relatedDocuments,
                        evaluatedGlobalProperties,
                        out string[] expandedValues,
                        consumingElement: item))
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

                    resolvedItemInputs.Add(inputPath);

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

                    if (isInclude &&
                        item.Name.LocalName.Equals("Reference", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(inputPath))
                    {
                        hasControlledReferenceInclude = true;
                    }
                }
            }

            if (!HasOnlyControlledTargetItemMetadataInputs(
                    item,
                    declaringPath,
                    taskInputBaseDirectory,
                    declaringAllowedRoot,
                    taskInputAllowedRoot,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    resolvedItemInputs,
                    isControlledInput,
                    out bool hasReferenceHintPath) ||
                (hasReferenceInclude &&
                 !hasControlledReferenceInclude &&
                 !hasReferenceHintPath))
            {
                return false;
            }

            if (!IsControlledPublishFileItemName(item.Name.LocalName))
                continue;

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
                        out string[] expandedValues,
                        consumingElement: item) ||
                    expandedValues.Any(value => !IsControlledPublishRelativePath(value)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasOnlyControlledTargetItemMetadataInputs(
        XElement item,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        IReadOnlyCollection<string> resolvedItemInputs,
        Func<string, bool>? isControlledInput,
        out bool hasReferenceHintPath)
    {
        hasReferenceHintPath = false;
        string itemName = item.Name.LocalName;
        string? metadataName = itemName.Equals("Reference", StringComparison.OrdinalIgnoreCase)
            ? "HintPath"
            : itemName.Equals("EmbeddedResource", StringComparison.OrdinalIgnoreCase)
                ? "DependentUpon"
                : null;
        if (metadataName is null)
            return true;

        (string Value, string DeclaringPath)[] metadataValues = item.Attributes()
            .Where(attribute => attribute.Name.LocalName.Equals(
                metadataName,
                StringComparison.OrdinalIgnoreCase))
            .Select(attribute => (attribute.Value, declaringPath))
            .Concat(item.Elements()
                .Where(element => element.Name.LocalName.Equals(
                    metadataName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(element => (element.Value, declaringPath)))
            .ToArray();
        if (metadataValues.Length == 0 &&
            !TryReadControlledItemDefinitionMetadata(
                itemName,
                metadataName,
                relatedDocuments,
                evaluatedGlobalProperties,
                out metadataValues))
        {
            return false;
        }
        if (metadataValues.Length == 0)
            return true;
        if (itemName.Equals("Reference", StringComparison.OrdinalIgnoreCase))
            hasReferenceHintPath = true;

        string[] inputBaseDirectories = itemName.Equals(
                "EmbeddedResource",
                StringComparison.OrdinalIgnoreCase)
            ? resolvedItemInputs
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray()
            : [taskInputBaseDirectory];
        if (inputBaseDirectories.Length == 0)
            return false;

        foreach ((string metadataValue, string metadataDeclaringPath) in metadataValues)
        {
            if (!TryExpandControlledTaskInputValues(
                    metadataValue,
                    metadataDeclaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedValues,
                    consumingElement: item))
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
                    ContainsUnresolvedBuildExpression(candidate))
                {
                    return false;
                }

                foreach (string inputBaseDirectory in inputBaseDirectories)
                {
                    if (!TryResolveControlledTaskInputPath(
                            candidate,
                            declaringPath,
                            inputBaseDirectory,
                            declaringAllowedRoot,
                            taskInputAllowedRoot,
                            out string inputPath) ||
                        Directory.Exists(inputPath))
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
        }

        return true;
    }

    private static bool TryReadControlledItemDefinitionMetadata(
        string itemName,
        string metadataName,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        out (string Value, string DeclaringPath)[] metadataValues)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (evaluatedGlobalProperties is not null)
        {
            foreach (KeyValuePair<string, string> property in evaluatedGlobalProperties)
                properties[property.Key] = property.Value;
        }

        (string Value, string DeclaringPath)? effectiveMetadata = null;
        foreach ((XDocument document, string itemDefinitionDeclaringPath) in relatedDocuments)
        {
            foreach (XElement group in document.Descendants().Where(element =>
                         element.Name.LocalName.Equals("ItemDefinitionGroup", StringComparison.OrdinalIgnoreCase) &&
                         !element.Ancestors().Any(ancestor =>
                             ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))))
            {
                if (!TryIsControlledPropertyBranchActive(group, properties, out bool branchActive))
                {
                    metadataValues = Array.Empty<(string Value, string DeclaringPath)>();
                    return false;
                }
                if (!branchActive)
                    continue;
                if (!TryIsControlledPropertyAssignmentActive(group, properties, out bool groupActive))
                {
                    metadataValues = Array.Empty<(string Value, string DeclaringPath)>();
                    return false;
                }
                if (!groupActive)
                    continue;

                foreach (XElement definition in group.Elements().Where(element =>
                             element.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!TryIsControlledPropertyAssignmentActive(
                            definition,
                            properties,
                            out bool definitionActive))
                    {
                        metadataValues = Array.Empty<(string Value, string DeclaringPath)>();
                        return false;
                    }
                    if (!definitionActive)
                        continue;

                    foreach (XAttribute attribute in definition.Attributes().Where(attribute =>
                                 attribute.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase)))
                    {
                        effectiveMetadata = (attribute.Value, itemDefinitionDeclaringPath);
                    }
                    foreach (XElement metadata in definition.Elements().Where(element =>
                                 element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!TryIsControlledPropertyAssignmentActive(
                                metadata,
                                properties,
                                out bool metadataActive))
                        {
                            metadataValues = Array.Empty<(string Value, string DeclaringPath)>();
                            return false;
                        }
                        if (metadataActive)
                            effectiveMetadata = (metadata.Value, itemDefinitionDeclaringPath);
                    }
                }
            }
        }

        metadataValues = effectiveMetadata.HasValue
            ? new[] { effectiveMetadata.Value }
            : Array.Empty<(string Value, string DeclaringPath)>();
        return true;
    }

    private static bool IsPublishRelativePathMetadata(string name)
        => name.Equals("RelativePath", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TargetPath", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Link", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("DestinationSubPath", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlledPublishFileItemName(string name)
        => ControlledPublishFileItemNames.Contains(name);

    private static bool IsControlledBuildTargetItem(
        XElement item,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments)
    {
        XElement? target = item.Ancestors().FirstOrDefault(ancestor =>
            ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return false;

        string[] scheduledTargets = target.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName.Equals("BeforeTargets", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("AfterTargets", StringComparison.OrdinalIgnoreCase))
            .SelectMany(attribute => DecodeMsBuildEscapes(attribute.Value).Split(';'))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (scheduledTargets.Length == 0 ||
            scheduledTargets.Any(value => !value.Equals("Clean", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? targetName = target.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(targetName))
            return true;

        return relatedDocuments.SelectMany(related => related.Document.Descendants())
            .SelectMany(element => element.Attributes())
            .Where(attribute => !ReferenceEquals(attribute.Parent, target) ||
                                !attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            .SelectMany(attribute => DecodeMsBuildEscapes(attribute.Value).Split(';'))
            .Select(value => value.Trim())
            .Any(value => value.Equals(targetName, StringComparison.OrdinalIgnoreCase));
    }
}
