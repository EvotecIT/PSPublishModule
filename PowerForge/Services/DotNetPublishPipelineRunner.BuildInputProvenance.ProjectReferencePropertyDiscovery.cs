using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string[] ReadControlledBuildPropertyNames(IEnumerable<string> projectPaths)
    {
        string[] paths = projectPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        var names = new HashSet<string>(
            ReadProjectReferenceConditionPropertyNames(paths),
            StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            try
            {
                XDocument document = XDocument.Load(path, LoadOptions.None);
                foreach (string value in document.DescendantNodes()
                             .OfType<XText>()
                             .Select(text => text.Value)
                             .Concat(document.Descendants().Attributes().Select(attribute => attribute.Value)))
                {
                    AddConditionPropertyNames(value, names);
                }
            }
            catch
            {
                // Unknown controlled inputs stay fail closed during the later document scan.
            }
        }
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] ReadProjectReferenceConditionPropertyNames(IEnumerable<string> projectPaths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        names.Add("_GlobalPropertiesToRemoveFromProjectReferences");
        var propertyDefinitions = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (string projectPath in projectPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                     .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            try
            {
                XDocument document = XDocument.Load(projectPath, LoadOptions.None);
                foreach (XElement property in document.Descendants().Where(element =>
                             element.Parent?.Name.LocalName.Equals(
                                 "PropertyGroup",
                                 StringComparison.OrdinalIgnoreCase) == true))
                {
                    if (!propertyDefinitions.TryGetValue(property.Name.LocalName, out List<XElement>? definitions))
                    {
                        definitions = new List<XElement>();
                        propertyDefinitions[property.Name.LocalName] = definitions;
                    }
                    definitions.Add(property);
                }

                foreach (string condition in document.Descendants()
                             .Attributes()
                             .Where(attribute => attribute.Name.LocalName.Equals(
                                 "Condition",
                                 StringComparison.OrdinalIgnoreCase))
                             .Select(attribute => attribute.Value))
                {
                    AddConditionPropertyNames(condition, names);
                }

                foreach (XElement projectReference in document.Descendants().Where(element =>
                             element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
                {
                    IEnumerable<XElement> conditionOwners = projectReference
                        .AncestorsAndSelf()
                        .Concat(projectReference.Descendants());
                    foreach (string condition in conditionOwners
                                 .Select(element => element.Attribute("Condition")?.Value)
                                 .Where(value => !string.IsNullOrWhiteSpace(value))!)
                    {
                        AddConditionPropertyNames(condition, names);
                    }

                    foreach (string expression in projectReference.Attributes()
                                 .Select(attribute => attribute.Value)
                                 .Concat(projectReference.Descendants().Select(element => element.Value))
                                 .Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        AddConditionPropertyNames(expression, names);
                    }

                    foreach (string condition in ReadMsBuildElementConditions(projectReference))
                        AddConditionPropertyNames(condition, names);
                }
            }
            catch
            {
                // Unknown conditions stay eligible so provenance remains fail closed.
            }
        }

        var pending = new Queue<string>(names);
        var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            string propertyName = pending.Dequeue();
            if (!inspected.Add(propertyName) ||
                !propertyDefinitions.TryGetValue(propertyName, out List<XElement>? definitions))
            {
                continue;
            }

            foreach (XElement definition in definitions)
            {
                int previousCount = names.Count;
                AddConditionPropertyNames(definition.Value, names);
                foreach (string condition in ReadMsBuildElementConditions(definition))
                    AddConditionPropertyNames(condition, names);
                if (names.Count == previousCount)
                    continue;

                foreach (string discoveredName in names.Where(name => !inspected.Contains(name)))
                    pending.Enqueue(discoveredName);
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ReadMsBuildElementConditions(XElement element)
    {
        foreach (string condition in element.AncestorsAndSelf()
                     .Select(candidate => candidate.Attribute("Condition")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value))!)
        {
            yield return condition;
        }

        foreach (XElement branch in element.Ancestors()
                     .Where(candidate =>
                         candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string condition in branch.ElementsBeforeSelf()
                         .Where(candidate => candidate.Name.LocalName.Equals(
                             "When",
                             StringComparison.OrdinalIgnoreCase))
                         .Select(candidate => candidate.Attribute("Condition")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value))!)
            {
                yield return condition;
            }
        }
    }
}
