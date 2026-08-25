using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const int MaximumControlledTaskInputExpressions = 256;

    private static bool ContainsUncontrolledTaskInputPropertyFunction(
        XDocument document,
        IReadOnlyCollection<XDocument> relatedDocuments)
    {
        var pending = new Queue<string>(document.Descendants()
            .Where(IsControlledBuildTaskElement)
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                !attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Name.LocalName.Equals("ContinueOnError", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Value));
        var inspected = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            if (inspected.Count >= MaximumControlledTaskInputExpressions)
                return true;

            string expression = pending.Dequeue();
            if (!inspected.Add(expression))
                continue;
            string decodedExpression = DecodeMsBuildEscapes(expression);
            if (ContainsValueProducingPropertyFunction(decodedExpression) ||
                decodedExpression.IndexOf("$($(", StringComparison.Ordinal) >= 0 ||
                decodedExpression.IndexOf("@($(", StringComparison.Ordinal) >= 0 ||
                decodedExpression.IndexOf("%($(", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            foreach (Match match in Regex.Matches(
                         expression,
                         @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                         RegexOptions.CultureInvariant))
            {
                string propertyName = match.Groups[1].Value;
                foreach (XElement property in relatedDocuments
                             .SelectMany(related => related.Descendants())
                             .Where(element =>
                                 element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                                 element.Parent is not null &&
                                 element.Parent.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase)))
                {
                    pending.Enqueue(property.Value);
                    foreach (XAttribute attribute in property.Attributes())
                        pending.Enqueue(attribute.Value);
                }
            }

            foreach (Match match in Regex.Matches(
                         expression,
                         @"@\(\s*([A-Za-z_][A-Za-z0-9_.-]*?)(?=\s*(?:->|,|\)))",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                foreach (XElement item in relatedDocuments
                             .SelectMany(related => related.Descendants())
                             .Where(element =>
                                 element.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                                 element.Parent is not null &&
                                 (element.Parent.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) ||
                                  element.Parent.Name.LocalName.Equals("ItemDefinitionGroup", StringComparison.OrdinalIgnoreCase))))
                {
                    pending.Enqueue(item.Value);
                    foreach (XAttribute attribute in item.DescendantsAndSelf().Attributes())
                        pending.Enqueue(attribute.Value);
                }
            }

            foreach (Match match in Regex.Matches(
                         expression,
                         @"%\(\s*(?:([A-Za-z_][A-Za-z0-9_.-]*)\.)?([A-Za-z_][A-Za-z0-9_.-]*)\s*\)",
                         RegexOptions.CultureInvariant))
            {
                string itemName = match.Groups[1].Value;
                string metadataName = match.Groups[2].Value;
                foreach (XElement metadata in relatedDocuments
                             .SelectMany(related => related.Descendants())
                             .Where(element =>
                                 element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                                 (itemName.Length == 0 ||
                                  element.Ancestors().Any(ancestor =>
                                      ancestor.Name.LocalName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))))
                {
                    pending.Enqueue(metadata.Value);
                    foreach (XAttribute attribute in metadata.Attributes())
                        pending.Enqueue(attribute.Value);
                }
            }
        }

        return false;
    }

    private static bool IsControlledBuildTaskElement(XElement element)
    {
        if (element.Parent is null ||
            !element.Parent.Name.LocalName.Equals("Target", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
               !element.Name.LocalName.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase) &&
               !element.Name.LocalName.Equals("OnError", StringComparison.OrdinalIgnoreCase);
    }
}
