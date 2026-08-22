using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static HashSet<string> ReadImmutableGlobalPropertyNames(
        ProjectEvaluationRequest request,
        XElement project)
    {
        var immutable = new HashSet<string>(
            request.GlobalProperties.Keys,
            StringComparer.OrdinalIgnoreCase)
        {
            "BuildProjectReferences"
        };
        if (request.Configuration is not null)
            immutable.Add("Configuration");
        if (request.TargetFramework is not null)
            immutable.Add("TargetFramework");

        foreach (string propertyName in (project.Attribute("TreatAsLocalProperty")?.Value ?? string.Empty)
                     .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(value => value.Trim())
                     .Where(value => value.Length > 0))
        {
            immutable.Remove(propertyName);
        }
        return immutable;
    }

    private static IReadOnlyDictionary<string, string> BuildInitialProjectReferenceProperties(
        ProjectEvaluationRequest request)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> property in request.GlobalProperties)
        {
            properties[property.Key] = property.Value;
        }
        properties["BuildProjectReferences"] = "false";
        if (request.Configuration is not null)
            properties["Configuration"] = request.Configuration;
        if (request.TargetFramework is not null)
            properties["TargetFramework"] = request.TargetFramework;
        return properties;
    }

    private static string[] ReadLiteralMsBuildPropertyDefinitions(
        IReadOnlyList<PreprocessedProjectPropertyDefinition> propertyDefinitions,
        IReadOnlyDictionary<string, string> initialProperties,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string propertyName)
    {
        var definedPropertyNames = new HashSet<string>(
            propertyDefinitions.Select(definition => definition.Element.Name.LocalName),
            StringComparer.OrdinalIgnoreCase);
        var assignedValues = initialProperties.ToDictionary(
            entry => entry.Key,
            entry => new[] { entry.Value },
            StringComparer.OrdinalIgnoreCase);
        foreach (PreprocessedProjectPropertyDefinition definition in propertyDefinitions)
        {
            if (IsDefinitelyInactiveMsBuildElement(
                    definition.Element,
                    evaluatedConditionProperties,
                    definition.DefiningProjectPath))
                continue;

            string currentPropertyName = definition.Element.Name.LocalName;
            string rawValue = ExpandMsBuildThisFileProperties(
                definition.Element.Value,
                definition.DefiningProjectPath);
            string[] values = ExpandLiteralMsBuildPropertyValueAtAssignment(
                rawValue,
                assignedValues,
                definedPropertyNames,
                evaluatedConditionProperties);
            if (IsDefinitelyActiveMsBuildElement(
                    definition.Element,
                    evaluatedConditionProperties,
                    definition.DefiningProjectPath))
            {
                assignedValues[currentPropertyName] = values;
            }
            else
            {
                string[] previous = assignedValues.TryGetValue(
                        currentPropertyName,
                        out string[]? existing)
                    ? existing
                    : Array.Empty<string>();
                assignedValues[currentPropertyName] = previous
                    .Concat(values)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaximumProjectReferencePropertyContexts + 1)
                    .ToArray();
                if (assignedValues[currentPropertyName].Length > MaximumProjectReferencePropertyContexts)
                    assignedValues[currentPropertyName] = Array.Empty<string>();
            }
        }

        return assignedValues.TryGetValue(propertyName, out string[]? result)
            ? result
            : Array.Empty<string>();
    }

    private static string[] ExpandLiteralMsBuildPropertyValueAtAssignment(
        string rawValue,
        IReadOnlyDictionary<string, string[]> assignedValues,
        ISet<string> definedPropertyNames,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal) { rawValue };
        var pending = new Queue<string>();
        pending.Enqueue(rawValue);
        while (pending.Count > 0)
        {
            string candidate = pending.Dequeue();
            if (!TryFindSimpleMsBuildPropertyExpression(
                    candidate,
                    out int expressionStart,
                    out int expressionLength,
                    out string? referencedProperty))
            {
                continue;
            }

            string[] replacements;
            if (assignedValues.TryGetValue(referencedProperty!, out string[]? assigned))
            {
                replacements = assigned;
            }
            else if (!definedPropertyNames.Contains(referencedProperty!) &&
                     evaluatedProperties.TryGetValue(referencedProperty!, out string? evaluatedValue) &&
                     IsSafeEvaluatedProjectReferencePropertyExpansion(evaluatedValue))
            {
                replacements = [evaluatedValue];
            }
            else
            {
                replacements = [string.Empty];
            }

            candidates.Remove(candidate);
            foreach (string replacement in replacements)
            {
                string expanded = candidate.Substring(0, expressionStart) +
                                  replacement +
                                  candidate.Substring(expressionStart + expressionLength);
                if (candidates.Add(expanded))
                    pending.Enqueue(expanded);
                if (candidates.Count > MaximumProjectReferencePropertyContexts)
                    return Array.Empty<string>();
            }
        }
        return candidates.ToArray();
    }

    private static string ExpandMsBuildThisFileProperties(string value, string definingProjectPath)
    {
        string fullPath = Path.GetFullPath(definingProjectPath);
        string directory = Path.GetDirectoryName(fullPath)! + Path.DirectorySeparatorChar;
        string root = Path.GetPathRoot(directory) ?? string.Empty;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildThisFileFullPath"] = fullPath,
            ["MSBuildThisFileDirectory"] = directory,
            ["MSBuildThisFileDirectoryNoRoot"] = directory.Substring(root.Length),
            ["MSBuildThisFile"] = Path.GetFileName(fullPath),
            ["MSBuildThisFileName"] = Path.GetFileNameWithoutExtension(fullPath),
            ["MSBuildThisFileExtension"] = Path.GetExtension(fullPath)
        };
        foreach (KeyValuePair<string, string> replacement in replacements)
        {
            value = ReplaceOrdinalIgnoreCase(
                value,
                "$(" + replacement.Key + ")",
                replacement.Value);
        }
        return value;
    }

    private static bool TryFindSimpleMsBuildPropertyExpression(
        string value,
        out int expressionStart,
        out int expressionLength,
        out string? propertyName)
    {
        expressionStart = value.IndexOf("$(", StringComparison.Ordinal);
        expressionLength = 0;
        propertyName = null;
        if (expressionStart < 0)
            return false;

        int expressionEnd = value.IndexOf(')', expressionStart + 2);
        if (expressionEnd < 0)
            return false;

        string candidate = value.Substring(expressionStart + 2, expressionEnd - expressionStart - 2).Trim();
        if (candidate.Length == 0 ||
            candidate.IndexOfAny(new[] { '$', '(', ')', ';', '=' }) >= 0)
        {
            return false;
        }

        propertyName = candidate;
        expressionLength = expressionEnd - expressionStart + 1;
        return true;
    }
}
