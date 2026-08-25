using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledCallTargetDestinations(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties)
    {
        var controlledTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement target in relatedDocuments
                     .SelectMany(related => related.Document.Descendants())
                     .Where(element => element.Name.LocalName.Equals(
                         "Target",
                         StringComparison.OrdinalIgnoreCase)))
        {
            string? name = target.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "Name",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (!string.IsNullOrWhiteSpace(name) &&
                !ContainsUnresolvedBuildExpression(name!))
            {
                controlledTargets.Add(DecodeMsBuildEscapes(name!).Trim());
            }
        }

        foreach (XElement callTarget in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("CallTarget", StringComparison.OrdinalIgnoreCase)))
        {
            string? targets = callTarget.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                    "Targets",
                    StringComparison.OrdinalIgnoreCase))?
                .Value;
            if (string.IsNullOrWhiteSpace(targets) ||
                !TryExpandControlledTaskInputValues(
                    targets!,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedTargets))
            {
                return false;
            }

            string[] destinations = expandedTargets
                .SelectMany(value => DecodeMsBuildEscapes(value).Split(';'))
                .Select(value => value.Trim().Trim('\'', '"'))
                .Where(value => value.Length > 0)
                .ToArray();
            if (destinations.Length == 0 ||
                destinations.Any(destination =>
                    ContainsUnresolvedBuildExpression(destination) ||
                    !controlledTargets.Contains(destination)))
            {
                return false;
            }
        }

        return true;
    }
}
