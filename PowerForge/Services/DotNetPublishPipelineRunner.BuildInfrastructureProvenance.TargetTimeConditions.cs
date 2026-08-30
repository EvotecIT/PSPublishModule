using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsDefinitelyInactiveControlledBuildOperation(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? definingProjectPath)
    {
        if (!IsDefinitelyInactiveMsBuildElement(element, evaluatedProperties, definingProjectPath))
            return false;

        XElement? target = element.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase));
        if (target is null ||
            IsDefinitelyInactiveMsBuildElement(target, evaluatedProperties, definingProjectPath))
        {
            return true;
        }

        HashSet<string> conditionProperties = ReadControlledBuildConditionPropertyNames(
            element,
            target);
        if (conditionProperties.Count == 0)
            return true;

        return !CanAssignTargetTimeConditionProperty(
            target.Descendants().TakeWhile(candidate => !ReferenceEquals(candidate, element)),
            conditionProperties);
    }

    private static HashSet<string> ReadControlledBuildConditionPropertyNames(
        XElement element,
        XElement target)
    {
        var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> conditions = element.AncestorsAndSelf()
            .TakeWhile(candidate => !ReferenceEquals(candidate, target))
            .Select(candidate => candidate.Attribute("Condition")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
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
        IEnumerable<XElement> precedingElements,
        ISet<string> conditionProperties)
    {
        foreach (XElement element in precedingElements)
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
