using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryReadPreResolveTaskWideProjectReferencePropertyRemovals(
        IReadOnlyList<PreprocessedProjectPropertyDefinition> propertyDefinitions,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        out string[] removals)
    {
        removals = Array.Empty<string>();
        IReadOnlyDictionary<string, string> preResolveProperties =
            BuildTargetTimeConditionProperties(
                evaluatedConditionProperties,
                propertyDefinitions);
        if (!preResolveProperties.TryGetValue(
                "_GlobalPropertiesToRemoveFromProjectReferences",
                out string? value))
        {
            if (propertyDefinitions.Any(definition =>
                    definition.Element.Name.LocalName.Equals(
                        "_GlobalPropertiesToRemoveFromProjectReferences",
                        StringComparison.OrdinalIgnoreCase) &&
                    !IsDefinitelyInactiveMsBuildElement(
                        definition.Element,
                        evaluatedConditionProperties,
                        definition.DefiningProjectPath)))
            {
                foreach (PreprocessedProjectPropertyDefinition definition in propertyDefinitions.Where(definition =>
                             definition.Element.Name.LocalName.Equals(
                                 "_GlobalPropertiesToRemoveFromProjectReferences",
                                 StringComparison.OrdinalIgnoreCase)))
                return false;
            }
            return true;
        }
        if (value.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            value.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            value.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(value, out string? decoded))
            return false;

        removals = ReadProjectReferencePropertyNames(decoded);
        return true;
    }

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
            var contexts = new List<HashSet<string>>
            {
                new(taskWidePropertyRemovals, StringComparer.OrdinalIgnoreCase)
            };
            foreach (string metadataName in new[] { "UndefineProperties", "GlobalPropertiesToRemove" })
            {
                var evaluatedNames = new HashSet<string>(
                    ReadProjectReferencePropertyNames(ReadItemText(item, metadataName)),
                    StringComparer.OrdinalIgnoreCase);
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
                var matchingMetadataContexts = new List<HashSet<string>>();
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

                        var candidateNames = new HashSet<string>(
                            ReadProjectReferencePropertyNames(decoded),
                            StringComparer.OrdinalIgnoreCase);
                        bool matchesResolvedMetadata = candidateNames.SetEquals(evaluatedNames);
                        // The metadata query does not execute pre-resolution targets.
                        // Exact scheduled replay is authoritative for that missing value.
                        bool recoversPreResolveTargetMetadata = evaluatedNames.Count == 0 &&
                                                               candidateNames.Count > 0 &&
                                                               assignment.IsPreResolveTargetTime;
                        if ((matchesResolvedMetadata || recoversPreResolveTargetMetadata) &&
                            !matchingMetadataContexts.Any(existing => existing.SetEquals(candidateNames)))
                        {
                            matchingMetadataContexts.Add(candidateNames);
                        }
                    }
                }

                if (matchingMetadataContexts.Count == 0)
                {
                    // Without exact literal evidence, retaining the property is the
                    // fail-closed provenance choice.
                    matchingMetadataContexts.Add(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }

                contexts = contexts.SelectMany(context => matchingMetadataContexts.Select(metadataContext =>
                    {
                        var combined = new HashSet<string>(context, StringComparer.OrdinalIgnoreCase);
                        combined.UnionWith(metadataContext);
                        return combined;
                    }))
                    .Take(MaximumProjectReferencePropertyContexts + 1)
                    .ToList();
                if (contexts.Count > MaximumProjectReferencePropertyContexts)
                    return false;
            }

            var removalResults = contexts.Select(context => context.ToArray()).ToList();
            if (HasUncertainProjectReferencePropertyRemoval(
                    declaringProjectPath,
                    referencedPath,
                    declarations,
                    evaluatedConditionProperties))
            {
                removalResults.Add(taskWidePropertyRemovals.ToArray());
            }
            removalContexts = removalResults
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
                DoesProjectReferenceDeclarationMatch(
                    declaringProjectPath,
                    referencedPath,
                    declaration,
                    conditionProperties) is ProjectReferenceDeclarationMatch.NoMatch)
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
