using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class ControlledStaticItem
    {
        internal ControlledStaticItem(string identity, XElement element, string declaringPath)
        {
            Identity = identity;
            Element = element;
            DeclaringPath = declaringPath;
        }

        internal string Identity { get; }

        internal XElement Element { get; }

        internal string DeclaringPath { get; }
    }

    private static bool TryExpandControlledTaskInputValues(
        string expression,
        string declaringPath,
        string taskInputBaseDirectory,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        out string[] expandedValues)
    {
        var pending = new Queue<string>();
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        pending.Enqueue(expression);
        while (pending.Count > 0)
        {
            if (inspected.Count >= MaximumControlledTaskFileInputExpressions)
            {
                expandedValues = Array.Empty<string>();
                return false;
            }

            string value = pending.Dequeue();
            if (!inspected.Add(value))
                continue;
            bool expanded = false;

            foreach (Match match in Regex.Matches(
                         value,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         RegexOptions.CultureInvariant))
            {
                string propertyName = match.Groups[1].Value;
                if (propertyName.Equals("MSBuildThisFileDirectory", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (propertyName.Equals("MSBuildProjectDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    pending.Enqueue(value.Replace(match.Value, taskInputBaseDirectory));
                    expanded = true;
                    continue;
                }
                if (evaluatedGlobalProperties is not null &&
                    evaluatedGlobalProperties.TryGetValue(propertyName, out string? evaluatedValue))
                {
                    pending.Enqueue(value.Replace(match.Value, evaluatedValue));
                    expanded = true;
                    continue;
                }
                if (!TryReplayControlledTaskInputProperty(
                        propertyName,
                        relatedDocuments,
                        evaluatedGlobalProperties,
                        out string effectiveValue,
                        out bool found))
                {
                    expandedValues = Array.Empty<string>();
                    return false;
                }
                if (!found)
                    continue;
                pending.Enqueue(value.Replace(match.Value, effectiveValue));
                expanded = true;
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"@\(\s*(?<item>[A-Za-z_][A-Za-z0-9_.-]*)\s*->\s*(?:'(?<single>[^']*)'|""(?<double>[^""]*)"")(?:\s*,\s*(?:'(?<singleSeparator>[^']*)'|""(?<doubleSeparator>[^""]*)""))?\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups["item"].Value;
                string transform = match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["double"].Value;
                ControlledStaticItem[] items = EnumerateControlledStaticItems(itemName, relatedDocuments).ToArray();
                if (items.Length == 0)
                {
                    expandedValues = Array.Empty<string>();
                    return false;
                }
                var transformedValues = new List<string>(items.Length);
                foreach (ControlledStaticItem item in items)
                {
                    if (!TryExpandControlledItemTransform(
                            transform,
                            itemName,
                            item,
                            taskInputBaseDirectory,
                            out string transformedValue))
                    {
                        expandedValues = Array.Empty<string>();
                        return false;
                    }
                    transformedValues.Add(transformedValue);
                }
                string separator = match.Groups["singleSeparator"].Success
                    ? match.Groups["singleSeparator"].Value
                    : match.Groups["doubleSeparator"].Success
                        ? match.Groups["doubleSeparator"].Value
                        : ";";
                pending.Enqueue(value.Replace(match.Value, string.Join(separator, transformedValues)));
                expanded = true;
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"@\(\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                string[] itemValues = EnumerateControlledStaticItems(itemName, relatedDocuments)
                    .Select(item => item.Identity)
                    .ToArray();
                if (itemValues.Length == 0)
                {
                    expandedValues = Array.Empty<string>();
                    return false;
                }
                pending.Enqueue(value.Replace(match.Value, string.Join(";", itemValues)));
                expanded = true;
            }

            foreach (Match match in Regex.Matches(
                         value,
                         @"%\(\s*(?:([A-Za-z_][A-Za-z0-9_.-]*)\.)?([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                string metadataName = match.Groups[2].Value;
                foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
                {
                    foreach (XElement metadata in relatedDocument.Descendants().Where(element =>
                                 element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                                 (itemName.Length == 0 ||
                                  element.Ancestors().Any(ancestor =>
                                      ancestor.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))))
                    {
                        string metadataValue = ResolveControlledThisFileDirectory(metadata.Value, relatedPath);
                        pending.Enqueue(metadataValue);
                        pending.Enqueue(value.Replace(match.Value, metadataValue));
                        expanded = true;
                    }
                }
            }

            if (!expanded)
                values.Add(ResolveControlledThisFileDirectory(value, declaringPath));
        }

        expandedValues = values.ToArray();
        return values.Count > 0;
    }

    private static bool TryReplayControlledTaskInputProperty(
        string propertyName,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        out string effectiveValue,
        out bool found)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (evaluatedGlobalProperties is not null)
        {
            foreach (KeyValuePair<string, string> property in evaluatedGlobalProperties)
                properties[property.Key] = property.Value;
        }
        var unknownProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        found = false;
        foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
        {
            foreach (XElement propertyGroup in relatedDocument.Descendants().Where(element =>
                         element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase)))
            {
                bool groupConditionKnown = TryIsControlledPropertyBranchActive(
                    propertyGroup,
                    properties,
                    out bool branchActive);
                bool groupActive = false;
                if (groupConditionKnown && branchActive)
                {
                    groupConditionKnown = TryIsControlledPropertyAssignmentActive(
                        propertyGroup,
                        properties,
                        out groupActive);
                }

                foreach (XElement property in propertyGroup.Elements())
                {
                    string assignedPropertyName = property.Name.LocalName;
                    if (evaluatedGlobalProperties is not null &&
                        evaluatedGlobalProperties.ContainsKey(assignedPropertyName))
                    {
                        if (assignedPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                        continue;
                    }

                    if (!groupConditionKnown)
                    {
                        properties.Remove(assignedPropertyName);
                        unknownProperties.Add(assignedPropertyName);
                        if (assignedPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                        continue;
                    }
                    if (!groupActive)
                        continue;
                    if (!TryIsControlledPropertyAssignmentActive(property, properties, out bool propertyActive))
                    {
                        properties.Remove(assignedPropertyName);
                        unknownProperties.Add(assignedPropertyName);
                        if (assignedPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                        continue;
                    }
                    if (!propertyActive)
                        continue;

                    string assignedValue = ResolveControlledThisFileDirectory(property.Value, relatedPath);
                    bool hasUnknownReference = false;
                    assignedValue = Regex.Replace(
                        assignedValue,
                        @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                        propertyReference =>
                        {
                            string referencedPropertyName = propertyReference.Groups[1].Value;
                            if (unknownProperties.Contains(referencedPropertyName) ||
                                !properties.TryGetValue(referencedPropertyName, out string? priorValue))
                            {
                                hasUnknownReference = true;
                                return propertyReference.Value;
                            }
                            return priorValue;
                        },
                        RegexOptions.CultureInvariant);
                    if (hasUnknownReference)
                    {
                        properties.Remove(assignedPropertyName);
                        unknownProperties.Add(assignedPropertyName);
                    }
                    else
                    {
                        properties[assignedPropertyName] = assignedValue;
                        unknownProperties.Remove(assignedPropertyName);
                    }
                    if (assignedPropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                        found = true;
                }
            }
        }

        if (unknownProperties.Contains(propertyName))
        {
            effectiveValue = string.Empty;
            return false;
        }
        effectiveValue = found && properties.TryGetValue(propertyName, out string? value)
            ? value
            : string.Empty;
        return true;
    }

    private static bool TryIsControlledPropertyAssignmentActive(
        XElement element,
        IReadOnlyDictionary<string, string> properties,
        out bool active)
    {
        XAttribute? condition = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase));
        if (condition is null || string.IsNullOrWhiteSpace(condition.Value))
        {
            active = true;
            return true;
        }

        return TryEvaluateSimpleMsBuildCondition(condition.Value, properties, out active);
    }

    private static bool TryIsControlledPropertyBranchActive(
        XElement element,
        IReadOnlyDictionary<string, string> properties,
        out bool active)
    {
        active = true;
        foreach (XElement branch in element.Ancestors().Where(ancestor =>
                     ancestor.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                     ancestor.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase)))
        {
            XElement? choose = branch.Parent;
            if (choose is null ||
                !choose.Name.LocalName.Equals("Choose", StringComparison.OrdinalIgnoreCase))
            {
                active = false;
                return false;
            }

            XElement? selectedBranch = null;
            foreach (XElement candidate in choose.Elements())
            {
                if (candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryIsControlledPropertyAssignmentActive(candidate, properties, out bool selected))
                    {
                        active = false;
                        return false;
                    }
                    if (!selected)
                        continue;
                    selectedBranch = candidate;
                    break;
                }
                if (candidate.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase))
                {
                    selectedBranch = candidate;
                    break;
                }
            }

            if (!ReferenceEquals(selectedBranch, branch))
            {
                active = false;
                return true;
            }
        }

        return true;
    }

    private static IEnumerable<ControlledStaticItem> EnumerateControlledStaticItems(
        string itemName,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments)
    {
        foreach ((XDocument relatedDocument, string relatedPath) in relatedDocuments)
        {
            foreach (XElement item in relatedDocument.Descendants().Where(element =>
                         element.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                         element.Parent is not null &&
                         element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)))
            {
                XAttribute? include = item.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase));
                string itemValue = include?.Value ?? item.Value;
                foreach (string identity in DecodeMsBuildEscapes(itemValue).Split(';'))
                {
                    if (!string.IsNullOrWhiteSpace(identity))
                    {
                        yield return new ControlledStaticItem(
                            ResolveControlledThisFileDirectory(identity.Trim(), relatedPath),
                            item,
                            relatedPath);
                    }
                }
            }
        }
    }

    private static bool TryExpandControlledItemTransform(
        string transform,
        string itemName,
        ControlledStaticItem item,
        string taskInputBaseDirectory,
        out string transformedValue)
    {
        transformedValue = transform;
        foreach (Match metadata in Regex.Matches(
                     transform,
                     @"%\(\s*(?:([A-Za-z_][A-Za-z0-9_.-]*)\.)?([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                     RegexOptions.CultureInvariant))
        {
            string qualifiedItemName = metadata.Groups[1].Value;
            if (qualifiedItemName.Length > 0 &&
                !qualifiedItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase))
            {
                transformedValue = string.Empty;
                return false;
            }
            if (!TryGetControlledItemMetadata(
                    item,
                    metadata.Groups[2].Value,
                    taskInputBaseDirectory,
                    out string metadataValue))
            {
                transformedValue = string.Empty;
                return false;
            }
            transformedValue = transformedValue.Replace(metadata.Value, metadataValue);
        }

        return true;
    }

    private static bool TryGetControlledItemMetadata(
        ControlledStaticItem item,
        string metadataName,
        string taskInputBaseDirectory,
        out string value)
    {
        if (metadataName.Equals("Identity", StringComparison.OrdinalIgnoreCase))
        {
            value = item.Identity;
            return true;
        }

        XElement? explicitMetadata = item.Element.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase));
        if (explicitMetadata is not null)
        {
            value = ResolveControlledThisFileDirectory(explicitMetadata.Value, item.DeclaringPath);
            return true;
        }
        XAttribute? explicitMetadataAttribute = item.Element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
            !attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase));
        if (explicitMetadataAttribute is not null)
        {
            value = ResolveControlledThisFileDirectory(explicitMetadataAttribute.Value, item.DeclaringPath);
            return true;
        }

        if (ContainsUnresolvedBuildExpression(item.Identity))
        {
            value = string.Empty;
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(
                Path.IsPathRooted(item.Identity)
                    ? item.Identity
                    : Path.Combine(taskInputBaseDirectory, item.Identity));
        }
        catch
        {
            value = string.Empty;
            return false;
        }

        string identityDirectory = Path.GetDirectoryName(item.Identity) ?? string.Empty;
        string definingProjectDirectory = Path.GetDirectoryName(Path.GetFullPath(item.DeclaringPath))!;
        if (metadataName.Equals("FullPath", StringComparison.OrdinalIgnoreCase))
            value = fullPath;
        else if (metadataName.Equals("RootDir", StringComparison.OrdinalIgnoreCase))
            value = Path.GetPathRoot(fullPath) ?? string.Empty;
        else if (metadataName.Equals("Filename", StringComparison.OrdinalIgnoreCase))
            value = Path.GetFileNameWithoutExtension(item.Identity);
        else if (metadataName.Equals("Extension", StringComparison.OrdinalIgnoreCase))
            value = Path.GetExtension(item.Identity);
        else if (metadataName.Equals("RelativeDir", StringComparison.OrdinalIgnoreCase) ||
                 metadataName.Equals("Directory", StringComparison.OrdinalIgnoreCase))
            value = EnsureControlledDirectoryMetadataSeparator(identityDirectory);
        else if (metadataName.Equals("DefiningProjectFullPath", StringComparison.OrdinalIgnoreCase))
            value = Path.GetFullPath(item.DeclaringPath);
        else if (metadataName.Equals("DefiningProjectDirectory", StringComparison.OrdinalIgnoreCase))
            value = EnsureControlledDirectoryMetadataSeparator(definingProjectDirectory);
        else if (metadataName.Equals("DefiningProjectName", StringComparison.OrdinalIgnoreCase))
            value = Path.GetFileNameWithoutExtension(item.DeclaringPath);
        else if (metadataName.Equals("DefiningProjectExtension", StringComparison.OrdinalIgnoreCase))
            value = Path.GetExtension(item.DeclaringPath);
        else
        {
            value = string.Empty;
            return false;
        }

        return true;
    }

    private static string EnsureControlledDirectoryMetadataSeparator(string value)
        => value.Length == 0 ||
           value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
           value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? value
            : value + Path.DirectorySeparatorChar;
}
