using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsDefinitelyInactiveControlledBuildOperation(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? definingProjectPath,
        IEnumerable<XDocument>? relatedDocuments = null)
    {
        if (!IsDefinitelyInactiveMsBuildElement(element, evaluatedProperties, definingProjectPath))
            return false;

        XElement? target = element.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return true;

        bool targetIsDefinitelyInactive = IsDefinitelyInactiveMsBuildElement(
            target,
            evaluatedProperties,
            definingProjectPath);

        HashSet<string> conditionProperties = ReadControlledBuildConditionPropertyNames(
            element,
            target,
            includeTargetCondition: targetIsDefinitelyInactive);
        if (conditionProperties.Count == 0)
            return true;

        IEnumerable<XElement> precedingTargetElements = target.Descendants()
            .TakeWhile(candidate => !ReferenceEquals(candidate, element));
        IEnumerable<XDocument> assignmentDocuments = relatedDocuments ?? Enumerable.Empty<XDocument>();
        if (target.Document is not null)
            assignmentDocuments = assignmentDocuments.Prepend(target.Document);
        IEnumerable<XElement> otherTargetElements = assignmentDocuments
            .SelectMany(document => document.Descendants())
            .Where(candidate =>
            {
                XElement? candidateTarget = candidate.AncestorsAndSelf().FirstOrDefault(ancestor =>
                    ancestor.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
                return candidateTarget is not null && !ReferenceEquals(candidateTarget, target);
            });

        return !CanAssignTargetTimeConditionProperty(
            precedingTargetElements.Concat(otherTargetElements),
            conditionProperties);
    }

    private static HashSet<string> ReadControlledBuildConditionPropertyNames(
        XElement element,
        XElement target,
        bool includeTargetCondition)
    {
        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> conditions = element.AncestorsAndSelf()
            .TakeWhile(candidate => !ReferenceEquals(candidate, target))
            .Select(candidate => candidate.Attribute("Condition")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
        if (includeTargetCondition && !string.IsNullOrWhiteSpace(target.Attribute("Condition")?.Value))
            conditions = conditions.Prepend(target.Attribute("Condition")!.Value);
        IEnumerable<string> precedingWhenConditions = element.AncestorsAndSelf()
            .TakeWhile(candidate => !ReferenceEquals(candidate, target))
            .Where(candidate =>
                candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                candidate.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase))
            .SelectMany(branch => branch.ElementsBeforeSelf())
            .Where(candidate => candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Attribute("Condition")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;

        foreach (string condition in conditions.Concat(precedingWhenConditions))
        {
            foreach (Match match in Regex.Matches(
                         condition,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         RegexOptions.CultureInvariant))
            {
                propertyNames.Add(match.Groups[1].Value);
            }
        }

        return propertyNames;
    }

    private static bool CanAssignTargetTimeConditionProperty(
        IEnumerable<XElement> candidateElements,
        ISet<string> conditionProperties)
    {
        foreach (XElement element in candidateElements)
        {
            if (element.Parent?.Name.LocalName.Equals(
                    "PropertyGroup",
                    StringComparison.OrdinalIgnoreCase) == true &&
                conditionProperties.Contains(element.Name.LocalName))
            {
                return true;
            }

            if (!element.Name.LocalName.Equals("Output", StringComparison.OrdinalIgnoreCase))
                continue;

            string? propertyName = element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "PropertyName",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;
            if (ContainsUnresolvedBuildExpression(propertyName!) ||
                conditionProperties.Contains(DecodeMsBuildEscapes(propertyName!).Trim()))
            {
                return true;
            }
        }

        return false;
    }
}
