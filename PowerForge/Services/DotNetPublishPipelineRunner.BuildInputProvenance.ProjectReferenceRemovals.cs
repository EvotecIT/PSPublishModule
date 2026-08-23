using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadEffectiveProjectReferencePropertyRemovals(
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> declarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        bool preferEffectiveLiteralAssignments,
        out string[][] removalContexts)
    {
        removalContexts = Array.Empty<string[]>();
        if (!preferEffectiveLiteralAssignments)
        {
            removalContexts =
            [
                ReadProjectReferencePropertyNames(
                    ReadItemText(item, "UndefineProperties"),
                    ReadItemText(item, "GlobalPropertiesToRemove"),
                    string.Join(";", taskWidePropertyRemovals))
            ];
            return true;
        }

        string? referencedProject = ReadItemText(item, projectPathMetadataName);
        if (string.IsNullOrWhiteSpace(referencedProject))
            return false;

        try
        {
            string referencedPath = Path.GetFullPath(referencedProject!);
            var results = new HashSet<string>(taskWidePropertyRemovals, StringComparer.OrdinalIgnoreCase);
            foreach (string metadataName in new[] { "UndefineProperties", "GlobalPropertiesToRemove" })
            {
                LiteralProjectReferenceMetadataAssignment[] assignments =
                    ReadEffectiveLiteralProjectReferenceMetadataAssignments(
                        declaringProjectPath,
                        referencedPath,
                        declarations,
                        evaluatedConditionProperties,
                        metadataName,
                        includeTargetTime: declarations.Any(declaration =>
                            declaration.IsTargetTime && declaration.RunsBeforeResolveReferences),
                        ReadItemText(item, metadataName));
                foreach (LiteralProjectReferenceMetadataAssignment assignment in assignments)
                {
                    foreach (string candidate in ReadLiteralProjectReferencePropertyAssignmentCandidates(
                                 assignment.PropertyDefinitions,
                                 assignment.InitialProperties,
                                 assignment.ConditionProperties,
                                 assignment.DefiningProjectPath,
                                 assignment.Value))
                    {
                        if (candidate.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                            candidate.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                            candidate.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
                            !TryUnescapeMsBuildLiteral(candidate, out string? decoded))
                        {
                            return false;
                        }

                        foreach (string name in ReadProjectReferencePropertyNames(decoded))
                            results.Add(name);
                    }
                }
            }

            var contexts = new List<string[]> { results.ToArray() };
            if (HasUncertainProjectReferencePropertyRemoval(
                    declaringProjectPath,
                    referencedPath,
                    declarations,
                    evaluatedConditionProperties))
            {
                contexts.Add(taskWidePropertyRemovals.ToArray());
            }
            removalContexts = contexts
                .GroupBy(
                    context => string.Join("\0", context.OrderBy(
                        value => value,
                        StringComparer.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasUncertainProjectReferencePropertyRemoval(
        string declaringProjectPath,
        string referencedPath,
        IEnumerable<PreprocessedProjectReferenceDeclaration> declarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties)
    {
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations)
        {
            if (declaration.IsTargetTime && !declaration.RunsBeforeResolveReferences)
                continue;

            IReadOnlyDictionary<string, string> conditionProperties =
                BuildTargetTimeConditionProperties(
                    evaluatedConditionProperties,
                    declaration.RuntimePropertyDefinitions);
            bool definitelyActive = IsDefinitelyActiveMsBuildElement(
                declaration.Element,
                conditionProperties,
                declaration.DefiningProjectPath);
            if (IsDefinitelyInactiveMsBuildElement(
                    declaration.Element,
                    conditionProperties,
                    declaration.DefiningProjectPath) ||
                !DoesProjectReferenceDeclarationMatch(
                    declaringProjectPath,
                    referencedPath,
                    declaration,
                    conditionProperties))
            {
                continue;
            }

            bool hasRemovalAttribute = declaration.Element.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("UndefineProperties", StringComparison.OrdinalIgnoreCase) ||
                    attribute.Name.LocalName.Equals("GlobalPropertiesToRemove", StringComparison.OrdinalIgnoreCase));
            XElement[] removalElements = declaration.Element.Elements().Where(element =>
                    element.Name.LocalName.Equals("UndefineProperties", StringComparison.OrdinalIgnoreCase) ||
                    element.Name.LocalName.Equals("GlobalPropertiesToRemove", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if ((!definitelyActive && (hasRemovalAttribute || removalElements.Length > 0)) ||
                removalElements.Any(element =>
                    !IsDefinitelyActiveMsBuildElement(
                        element,
                        conditionProperties,
                        declaration.DefiningProjectPath) &&
                    !IsDefinitelyInactiveMsBuildElement(
                        element,
                        conditionProperties,
                        declaration.DefiningProjectPath)))
            {
                return true;
            }
        }

        return false;
    }
}
