using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class PreprocessedProjectPropertyDefinition
    {
        internal PreprocessedProjectPropertyDefinition(XElement element, string definingProjectPath)
        {
            Element = element;
            DefiningProjectPath = definingProjectPath;
        }

        internal XElement Element { get; }

        internal string DefiningProjectPath { get; }
    }

    private sealed class PreprocessedProjectReferenceDeclaration
    {
        internal PreprocessedProjectReferenceDeclaration(
            XElement element,
            string definingProjectPath,
            IReadOnlyList<PreprocessedProjectPropertyDefinition> propertyDefinitions,
            IReadOnlyList<PreprocessedProjectPropertyDefinition> runtimePropertyDefinitions,
            IReadOnlyDictionary<string, string> initialProperties,
            IReadOnlyDictionary<string, EvaluatedProjectItem[]> evaluatedItemLists,
            bool isTargetTime,
            bool runsBeforeResolveReferences,
            bool executionMayBeSkipped)
        {
            Element = element;
            DefiningProjectPath = definingProjectPath;
            PropertyDefinitions = propertyDefinitions;
            RuntimePropertyDefinitions = runtimePropertyDefinitions;
            InitialProperties = initialProperties;
            EvaluatedItemLists = evaluatedItemLists;
            IsTargetTime = isTargetTime;
            RunsBeforeResolveReferences = runsBeforeResolveReferences;
            ExecutionMayBeSkipped = executionMayBeSkipped;
        }

        internal XElement Element { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> RuntimePropertyDefinitions { get; }

        internal IReadOnlyDictionary<string, string> InitialProperties { get; }

        internal IReadOnlyDictionary<string, EvaluatedProjectItem[]> EvaluatedItemLists { get; }

        internal bool IsTargetTime { get; }

        internal bool RunsBeforeResolveReferences { get; }

        internal bool ExecutionMayBeSkipped { get; }
    }

    private sealed class LiteralProjectReferenceMetadataAssignment
    {
        internal LiteralProjectReferenceMetadataAssignment(
            string value,
            PreprocessedProjectReferenceDeclaration declaration,
            IReadOnlyDictionary<string, string> conditionProperties)
        {
            Value = value;
            DefiningProjectPath = declaration.DefiningProjectPath;
            PropertyDefinitions = declaration.PropertyDefinitions;
            InitialProperties = declaration.InitialProperties;
            ConditionProperties = conditionProperties;
        }

        internal LiteralProjectReferenceMetadataAssignment(
            string value,
            LiteralProjectReferenceMetadataAssignment source)
        {
            Value = value;
            DefiningProjectPath = source.DefiningProjectPath;
            PropertyDefinitions = source.PropertyDefinitions;
            InitialProperties = source.InitialProperties;
            ConditionProperties = source.ConditionProperties;
        }

        internal string Value { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal IReadOnlyDictionary<string, string> InitialProperties { get; }

        internal IReadOnlyDictionary<string, string> ConditionProperties { get; }
    }

    private static LiteralProjectReferenceMetadataAssignment[]
        ReadEffectiveLiteralProjectReferenceMetadataAssignments(
            string declaringProjectPath,
            string referencedPath,
            IReadOnlyList<PreprocessedProjectReferenceDeclaration> declarations,
            IReadOnlyDictionary<string, string> evaluatedConditionProperties,
            string metadataName,
            bool includeTargetTime,
            string? evaluatedMetadataValue = null)
    {
        var defaults = new List<LiteralProjectReferenceMetadataAssignment>();
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations.Where(declaration =>
                     (includeTargetTime || !declaration.IsTargetTime) &&
                     (!declaration.IsTargetTime || declaration.RunsBeforeResolveReferences) &&
                     IsProjectReferenceItemDefinition(declaration.Element)))
        {
            IReadOnlyDictionary<string, string> declarationConditionProperties =
                BuildTargetTimeConditionProperties(
                    evaluatedConditionProperties,
                    declaration.RuntimePropertyDefinitions);
            if (IsDefinitelyInactiveMsBuildElement(
                    declaration.Element,
                    declarationConditionProperties,
                    declaration.DefiningProjectPath))
            {
                continue;
            }

            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    declarationConditionProperties,
                    metadataName);
            if (declaredAssignments.Count == 0)
                continue;

            defaults = IsDefinitelyActiveMsBuildElement(
                declaration.Element,
                declarationConditionProperties,
                declaration.DefiningProjectPath)
                ? declaredAssignments
                : MergeLiteralProjectReferenceMetadataAssignments(defaults, declaredAssignments);
        }

        var states = new List<LiteralProjectReferenceItemState>();
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations)
        {
            XElement projectReference = declaration.Element;
            IReadOnlyDictionary<string, string> declarationConditionProperties =
                BuildTargetTimeConditionProperties(
                    evaluatedConditionProperties,
                    declaration.RuntimePropertyDefinitions);
            if ((!includeTargetTime && declaration.IsTargetTime) ||
                (declaration.IsTargetTime && !declaration.RunsBeforeResolveReferences) ||
                IsProjectReferenceItemDefinition(projectReference) ||
                IsDefinitelyInactiveMsBuildElement(
                    projectReference,
                    declarationConditionProperties,
                    declaration.DefiningProjectPath))
            {
                continue;
            }

            ProjectReferenceDeclarationMatch declarationMatch = DoesProjectReferenceDeclarationMatch(
                declaringProjectPath,
                referencedPath,
                declaration,
                declarationConditionProperties,
                metadataName,
                evaluatedMetadataValue);
            if (declarationMatch is ProjectReferenceDeclarationMatch.NoMatch)
                continue;
            bool declarationMayBeSkipped = declaration.ExecutionMayBeSkipped ||
                                           declarationMatch is ProjectReferenceDeclarationMatch.Ambiguous;

            bool isInclude = HasMsBuildAttribute(projectReference, "Include");
            bool isUpdate = HasMsBuildAttribute(projectReference, "Update");
            bool isRemove = HasMsBuildAttribute(projectReference, "Remove");
            bool definitelyActive = IsDefinitelyActiveMsBuildElement(
                projectReference,
                declarationConditionProperties,
                declaration.DefiningProjectPath);
            bool identityIsComputed = projectReference.Attributes().Any(attribute =>
                (attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase)) &&
                IsComputedProjectReferenceItemSpec(attribute.Value));
            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    declarationConditionProperties,
                    metadataName);
            if (isRemove)
            {
                if (definitelyActive && !declarationMayBeSkipped)
                    states.Clear();
                continue;
            }

            if (isInclude)
            {
                List<LiteralProjectReferenceMetadataAssignment> effectiveAssignments =
                    declaredAssignments.Count > 0
                        ? ExpandCurrentProjectReferenceItemMetadata(
                            declaredAssignments,
                            defaults,
                            metadataName)
                        : defaults;
                states.Add(new LiteralProjectReferenceItemState(
                    effectiveAssignments));
                continue;
            }

            bool removeMetadataIsCertain = TryReadProjectReferenceMetadataRemoval(
                declaration,
                declarationConditionProperties,
                metadataName,
                out bool removesCurrentMetadata);
            bool removalIsAmbiguous = (HasMsBuildAttribute(projectReference, "RemoveMetadata") ||
                                       HasMsBuildAttribute(projectReference, "KeepMetadata")) &&
                                      !removeMetadataIsCertain;
            if (isUpdate &&
                (declaredAssignments.Count > 0 || removesCurrentMetadata || removalIsAmbiguous))
            {
                LiteralProjectReferenceItemState[] currentStates = states.ToArray();
                if (removesCurrentMetadata || removalIsAmbiguous)
                {
                    foreach (LiteralProjectReferenceItemState state in currentStates)
                    {
                        bool deletionIsDefinite = removesCurrentMetadata &&
                                                  removeMetadataIsCertain &&
                                                  definitelyActive &&
                                                  !declarationMayBeSkipped;
                        if (deletionIsDefinite)
                        {
                            state.Assignments.Clear();
                        }
                        else
                        {
                            states.Add(new LiteralProjectReferenceItemState(
                                Array.Empty<LiteralProjectReferenceMetadataAssignment>()));
                        }
                    }
                }

                if (declaredAssignments.Count == 0)
                    continue;

                foreach (LiteralProjectReferenceItemState state in states)
                {
                    List<LiteralProjectReferenceMetadataAssignment> effectiveAssignments =
                        ExpandCurrentProjectReferenceItemMetadata(
                            declaredAssignments,
                            state.Assignments,
                            metadataName);
                    state.Assignments = definitelyActive &&
                                        !declarationMayBeSkipped &&
                                        !identityIsComputed
                        ? effectiveAssignments
                        : MergeLiteralProjectReferenceMetadataAssignments(
                            state.Assignments,
                            effectiveAssignments);
                }
            }
        }

        LiteralProjectReferenceMetadataAssignment[] replayed = states
            .SelectMany(state => state.Assignments)
            .GroupBy(BuildLiteralProjectReferenceMetadataAssignmentKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        return replayed;
    }

    private static bool HasMsBuildAttribute(XElement element, string attributeName)
        => element.Attributes().Any(attribute => attribute.Name.LocalName.Equals(
            attributeName,
            StringComparison.OrdinalIgnoreCase));

    private enum ProjectReferenceDeclarationMatch
    {
        NoMatch,
        Match,
        Ambiguous
    }

    private static ProjectReferenceDeclarationMatch DoesProjectReferenceDeclarationMatch(
        string declaringProjectPath,
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string? metadataName = null,
        string? evaluatedMetadataValue = null)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string[] identityBaseDirectories =
        [
            Path.GetDirectoryName(declaringProjectPath)!,
            Path.GetDirectoryName(declaration.DefiningProjectPath)!
        ];
        foreach (XAttribute identity in declaration.Element.Attributes().Where(attribute =>
                     attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Name.LocalName.Equals("Remove", StringComparison.OrdinalIgnoreCase)))
        {
            if (!DoesProjectReferenceItemSpecMatch(
                    referencedPath,
                    declaration,
                    evaluatedConditionProperties,
                    identityBaseDirectories,
                    comparison,
                    identity.Value))
            {
                continue;
            }

            XAttribute? exclude = declaration.Element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("Exclude", StringComparison.OrdinalIgnoreCase));
            if (!identity.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                exclude is null)
            {
                return ProjectReferenceDeclarationMatch.Match;
            }

            bool excludeMatches = DoesProjectReferenceItemSpecMatch(
                referencedPath,
                declaration,
                evaluatedConditionProperties,
                identityBaseDirectories,
                comparison,
                exclude.Value,
                out bool excludeMatchIsCertain);
            if (!excludeMatchIsCertain)
                return ProjectReferenceDeclarationMatch.Ambiguous;
            return excludeMatches
                ? ProjectReferenceDeclarationMatch.NoMatch
                : ProjectReferenceDeclarationMatch.Match;
        }

        return DoesEvaluatedMetadataCorrelateComputedDeclaration(
            declaration,
            evaluatedConditionProperties,
            metadataName,
            evaluatedMetadataValue)
            ? ProjectReferenceDeclarationMatch.Match
            : ProjectReferenceDeclarationMatch.NoMatch;
    }

    private static bool DoesEvaluatedMetadataCorrelateComputedDeclaration(
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string? metadataName,
        string? evaluatedMetadataValue)
    {
        if (string.IsNullOrEmpty(metadataName) || evaluatedMetadataValue is null ||
            !declaration.Element.Attributes().Any(attribute =>
                (attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                 attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase)) &&
                (IsMsBuildPropertyFunctionExpression(attribute.Value) ||
                 attribute.Value.IndexOf("@(", StringComparison.Ordinal) >= 0)))
        {
            return false;
        }

        foreach (LiteralProjectReferenceMetadataAssignment assignment in
                 ReadActiveLiteralProjectReferenceMetadataAssignments(
                     declaration,
                     evaluatedConditionProperties,
                     metadataName!))
        {
            foreach (string candidate in ReadLiteralProjectReferencePropertyAssignmentCandidates(
                         assignment.PropertyDefinitions,
                         assignment.InitialProperties,
                         assignment.ConditionProperties,
                         assignment.DefiningProjectPath,
                         assignment.Value))
            {
                if (TryUnescapeMsBuildLiteral(candidate, out string? decoded) &&
                    string.Equals(
                        decoded!.Trim(),
                        evaluatedMetadataValue.Trim(),
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadProjectReferenceMetadataRemoval(
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName,
        out bool removesMetadata)
    {
        removesMetadata = false;
        XAttribute? removeMetadata = declaration.Element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("RemoveMetadata", StringComparison.OrdinalIgnoreCase));
        XAttribute? keepMetadata = declaration.Element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("KeepMetadata", StringComparison.OrdinalIgnoreCase));
        if (removeMetadata is null && keepMetadata is null)
            return true;
        if (removeMetadata is not null && keepMetadata is not null)
            return false;

        string[] candidates = ReadLiteralProjectReferencePropertyAssignmentCandidates(
            declaration.PropertyDefinitions,
            declaration.InitialProperties,
            evaluatedConditionProperties,
            declaration.DefiningProjectPath,
            (removeMetadata ?? keepMetadata)!.Value);
        if (candidates.Length == 0)
            return false;

        var removalOutcomes = new HashSet<bool>();
        foreach (string candidate in candidates)
        {
            if (candidate.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                candidate.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
                !TryUnescapeMsBuildLiteral(candidate, out string? decoded))
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in decoded!.Split(
                         new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (name.Trim().Length > 0)
                    names.Add(name.Trim());
            }
            removalOutcomes.Add(removeMetadata is not null
                ? names.Contains(metadataName)
                : !names.Contains(metadataName));
        }

        if (removalOutcomes.Count != 1)
            return false;
        removesMetadata = removalOutcomes.Single();
        return true;
    }

    private static bool DoesProjectReferenceItemSpecMatch(
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IEnumerable<string> identityBaseDirectories,
        StringComparison comparison,
        string itemSpec)
        => DoesProjectReferenceItemSpecMatch(
            referencedPath,
            declaration,
            evaluatedConditionProperties,
            identityBaseDirectories,
            comparison,
            itemSpec,
            out _);

    private static bool DoesProjectReferenceItemSpecMatch(
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IEnumerable<string> identityBaseDirectories,
        StringComparison comparison,
        string itemSpec,
        out bool matchIsCertain)
    {
        string[] candidates = IsComputedProjectReferenceItemSpec(itemSpec)
             ? ReadLiteralProjectReferencePropertyAssignmentCandidates(
                 declaration.PropertyDefinitions,
                 declaration.InitialProperties,
                 evaluatedConditionProperties,
                declaration.DefiningProjectPath,
                itemSpec)
            : [itemSpec];
        if (candidates.Length == 0)
        {
            matchIsCertain = false;
            return false;
        }

        bool anyMatchingCandidate = false;
        bool anyNonMatchingCandidate = false;
        bool anyUnknownCandidate = false;
        foreach (string candidate in candidates)
        {
            bool candidateMatches = false;
            bool candidateIsCertain = true;
            foreach (string individualItemSpec in candidate.Split(
                         new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string expandedItemSpec in ExpandEvaluatedProjectItemList(
                             individualItemSpec,
                             declaration.EvaluatedItemLists))
                {
                    if (expandedItemSpec.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                        expandedItemSpec.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                        expandedItemSpec.IndexOf("%(", StringComparison.Ordinal) >= 0)
                    {
                        candidateIsCertain = false;
                        continue;
                    }

                    foreach (string baseDirectory in identityBaseDirectories.Distinct(
                                 IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    {
                        if (TryResolveLiteralProjectReferencePath(
                                baseDirectory,
                                expandedItemSpec,
                                out string? declaredPath) &&
                            string.Equals(declaredPath, referencedPath, comparison))
                        {
                            candidateMatches = true;
                            break;
                        }

                        if (TryMatchProjectReferenceGlob(
                                baseDirectory,
                                expandedItemSpec,
                                referencedPath,
                                comparison))
                        {
                            candidateMatches = true;
                            break;
                        }
                    }
                    if (candidateMatches)
                        break;
                }
                if (candidateMatches)
                    break;
            }

            if (candidateMatches)
                anyMatchingCandidate = true;
            else if (candidateIsCertain)
                anyNonMatchingCandidate = true;
            else
                anyUnknownCandidate = true;
        }

        matchIsCertain = !anyUnknownCandidate &&
                         !(anyMatchingCandidate && anyNonMatchingCandidate);
        return anyMatchingCandidate;
    }

    private static IEnumerable<string> ExpandEvaluatedProjectItemList(
        string itemSpec,
        IReadOnlyDictionary<string, EvaluatedProjectItem[]> evaluatedItemLists)
    {
        Match itemList = Regex.Match(
            itemSpec.Trim(),
            @"^@\(\s*(?<name>[A-Za-z_][A-Za-z0-9_.-]*)\s*(?:->\s*(?<quote>['""])(?<transform>.*?)\k<quote>)?\s*(?:,\s*(?<separatorQuote>['""])(?<separator>.*?)\k<separatorQuote>)?\s*\)$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!itemList.Success ||
            !evaluatedItemLists.TryGetValue(
                itemList.Groups["name"].Value,
                out EvaluatedProjectItem[]? items))
        {
            return new[] { itemSpec };
        }

        if (!itemList.Groups["transform"].Success)
            return items.Select(item => item.FullPath).ToArray();
        if (itemList.Groups["separator"].Success &&
            (!TryUnescapeMsBuildLiteral(itemList.Groups["separator"].Value, out string? separator) ||
             !string.Equals(separator, ";", StringComparison.Ordinal)))
        {
            return new[] { itemSpec };
        }

        var expanded = new List<string>();
        foreach (EvaluatedProjectItem item in items)
        {
            string value = Regex.Replace(
                itemList.Groups["transform"].Value,
                @"%\((?:(?<item>[A-Za-z_][A-Za-z0-9_.-]*)\.)?(?<metadata>[A-Za-z_][A-Za-z0-9_.-]*)\)",
                match => (!match.Groups["item"].Success ||
                          match.Groups["item"].Value.Equals(
                              itemList.Groups["name"].Value,
                              StringComparison.OrdinalIgnoreCase)) &&
                         item.Metadata.TryGetValue(
                        match.Groups["metadata"].Value,
                        out string? metadataValue)
                    ? metadataValue
                    : match.Value,
                RegexOptions.CultureInvariant);
            if (value.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("%(", StringComparison.Ordinal) >= 0)
            {
                return new[] { itemSpec };
            }
            expanded.Add(value);
        }
        return expanded;
    }

    private static List<LiteralProjectReferenceMetadataAssignment>
        ReadActiveLiteralProjectReferenceMetadataAssignments(
            PreprocessedProjectReferenceDeclaration declaration,
            IReadOnlyDictionary<string, string> evaluatedConditionProperties,
            string metadataName)
    {
        XElement projectReference = declaration.Element;
        var assignments = projectReference.Attributes()
            .Where(attribute => attribute.Name.LocalName.Equals(
                metadataName,
                StringComparison.OrdinalIgnoreCase))
            .Select(attribute => new LiteralProjectReferenceMetadataAssignment(
                attribute.Value,
                declaration,
                evaluatedConditionProperties))
            .ToList();
        foreach (XElement element in projectReference.Elements().Where(element =>
                     element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                     !IsDefinitelyInactiveMsBuildElement(
                         element,
                         evaluatedConditionProperties,
                         declaration.DefiningProjectPath)))
        {
            var assignment = new LiteralProjectReferenceMetadataAssignment(
                element.Value,
                declaration,
                evaluatedConditionProperties);
            assignments = IsDefinitelyActiveMsBuildElement(
                element,
                evaluatedConditionProperties,
                declaration.DefiningProjectPath)
                ? [assignment]
                : MergeLiteralProjectReferenceMetadataAssignments(assignments, [assignment]);
        }
        return assignments;
    }

    private static List<LiteralProjectReferenceMetadataAssignment>
        MergeLiteralProjectReferenceMetadataAssignments(
            IEnumerable<LiteralProjectReferenceMetadataAssignment> first,
            IEnumerable<LiteralProjectReferenceMetadataAssignment> second)
    {
        return first.Concat(second)
            .GroupBy(BuildLiteralProjectReferenceMetadataAssignmentKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static string BuildLiteralProjectReferenceMetadataAssignmentKey(
        LiteralProjectReferenceMetadataAssignment assignment)
    {
        string path = IsWindows()
            ? assignment.DefiningProjectPath.ToUpperInvariant()
            : assignment.DefiningProjectPath;
        return path.Length + ":" + path + assignment.Value.Length + ":" + assignment.Value;
    }
}
