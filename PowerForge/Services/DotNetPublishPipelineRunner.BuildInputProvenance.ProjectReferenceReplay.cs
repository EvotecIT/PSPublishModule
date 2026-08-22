using System.Text;
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
            IReadOnlyDictionary<string, string[]> evaluatedItemLists,
            bool isTargetTime,
            bool runsBeforeResolveReferences)
        {
            Element = element;
            DefiningProjectPath = definingProjectPath;
            PropertyDefinitions = propertyDefinitions;
            RuntimePropertyDefinitions = runtimePropertyDefinitions;
            EvaluatedItemLists = evaluatedItemLists;
            IsTargetTime = isTargetTime;
            RunsBeforeResolveReferences = runsBeforeResolveReferences;
        }

        internal XElement Element { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> RuntimePropertyDefinitions { get; }

        internal IReadOnlyDictionary<string, string[]> EvaluatedItemLists { get; }

        internal bool IsTargetTime { get; }

        internal bool RunsBeforeResolveReferences { get; }
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
            ConditionProperties = conditionProperties;
        }

        internal string Value { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal IReadOnlyDictionary<string, string> ConditionProperties { get; }
    }

    private sealed class LiteralProjectReferenceItemState
    {
        internal LiteralProjectReferenceItemState(IEnumerable<LiteralProjectReferenceMetadataAssignment> assignments)
        {
            Assignments = assignments.ToList();
        }

        internal List<LiteralProjectReferenceMetadataAssignment> Assignments { get; set; }
    }

    private static LiteralProjectReferenceMetadataAssignment[]
        ReadEffectiveLiteralProjectReferenceMetadataAssignments(
            string declaringProjectPath,
            string referencedPath,
            IReadOnlyList<PreprocessedProjectReferenceDeclaration> declarations,
            IReadOnlyDictionary<string, string> evaluatedConditionProperties,
            string metadataName,
            bool includeTargetTime)
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
                    declaration.DefiningProjectPath) ||
                !DoesProjectReferenceDeclarationMatch(
                    declaringProjectPath,
                    referencedPath,
                    declaration,
                    declarationConditionProperties))
            {
                continue;
            }

            bool isInclude = HasMsBuildAttribute(projectReference, "Include");
            bool isUpdate = HasMsBuildAttribute(projectReference, "Update");
            bool isRemove = HasMsBuildAttribute(projectReference, "Remove");
            bool definitelyActive = IsDefinitelyActiveMsBuildElement(
                projectReference,
                declarationConditionProperties,
                declaration.DefiningProjectPath);
            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    declarationConditionProperties,
                    metadataName);
            if (isRemove)
            {
                if (definitelyActive)
                    states.Clear();
                continue;
            }

            if (isInclude)
            {
                states.Add(new LiteralProjectReferenceItemState(
                    declaredAssignments.Count > 0 ? declaredAssignments : defaults));
                continue;
            }

            if (isUpdate && declaredAssignments.Count > 0)
            {
                foreach (LiteralProjectReferenceItemState state in states)
                {
                    state.Assignments = definitelyActive
                        ? new List<LiteralProjectReferenceMetadataAssignment>(declaredAssignments)
                        : MergeLiteralProjectReferenceMetadataAssignments(
                            state.Assignments,
                            declaredAssignments);
                }
            }
        }

        return states
            .SelectMany(state => state.Assignments)
            .GroupBy(BuildLiteralProjectReferenceMetadataAssignmentKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsProjectReferenceItemDefinition(XElement projectReference)
        => projectReference.Parent?.Name.LocalName.Equals(
            "ItemDefinitionGroup",
            StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasMsBuildAttribute(XElement element, string attributeName)
        => element.Attributes().Any(attribute => attribute.Name.LocalName.Equals(
            attributeName,
            StringComparison.OrdinalIgnoreCase));

    private static bool DoesProjectReferenceDeclarationMatch(
        string declaringProjectPath,
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties)
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
            return !identity.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                   exclude is null ||
                   !DoesProjectReferenceItemSpecMatch(
                       referencedPath,
                       declaration,
                       evaluatedConditionProperties,
                       identityBaseDirectories,
                       comparison,
                       exclude.Value);
        }

        return false;
    }

    private static bool DoesProjectReferenceItemSpecMatch(
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IEnumerable<string> identityBaseDirectories,
        StringComparison comparison,
        string itemSpec)
    {
        string[] candidates = IsComputedProjectReferenceItemSpec(itemSpec)
            ? ReadLiteralProjectReferencePropertyAssignmentCandidates(
                declaration.PropertyDefinitions,
                evaluatedConditionProperties,
                declaration.DefiningProjectPath,
                itemSpec)
            : [itemSpec];
        foreach (string candidate in candidates)
        {
            foreach (string individualItemSpec in candidate.Split(
                         new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string expandedItemSpec in ExpandEvaluatedProjectItemList(
                             individualItemSpec,
                             declaration.EvaluatedItemLists))
                {
                    foreach (string baseDirectory in identityBaseDirectories.Distinct(
                                 IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    {
                        if (TryResolveLiteralProjectReferencePath(
                                baseDirectory,
                                expandedItemSpec,
                                out string? declaredPath) &&
                            string.Equals(declaredPath, referencedPath, comparison))
                        {
                            return true;
                        }

                        if (TryMatchProjectReferenceGlob(
                                baseDirectory,
                                expandedItemSpec,
                                referencedPath,
                                comparison))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandEvaluatedProjectItemList(
        string itemSpec,
        IReadOnlyDictionary<string, string[]> evaluatedItemLists)
    {
        Match itemList = Regex.Match(
            itemSpec.Trim(),
            @"^@\(([A-Za-z_][A-Za-z0-9_.-]*)\)$",
            RegexOptions.CultureInvariant);
        return itemList.Success &&
               evaluatedItemLists.TryGetValue(itemList.Groups[1].Value, out string[]? paths)
            ? paths
            : new[] { itemSpec };
    }

    private static bool TryMatchProjectReferenceGlob(
        string definingDirectory,
        string? itemSpec,
        string referencedPath,
        StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(itemSpec) ||
            itemSpec!.IndexOfAny(new[] { '*', '?' }) < 0 ||
            itemSpec.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(itemSpec, out string? unescapedItemSpec))
        {
            return false;
        }

        string fullPattern = Path.GetFullPath(Path.IsPathRooted(unescapedItemSpec!)
            ? unescapedItemSpec!
            : Path.Combine(definingDirectory, unescapedItemSpec!));
        string normalizedPattern = fullPattern.Replace('\\', '/');
        string normalizedPath = Path.GetFullPath(referencedPath).Replace('\\', '/');
        string expression = BuildProjectReferenceGlobExpression(normalizedPattern);
        RegexOptions options = RegexOptions.CultureInvariant;
        if (comparison == StringComparison.OrdinalIgnoreCase)
            options |= RegexOptions.IgnoreCase;
        return Regex.IsMatch(normalizedPath, expression, options, TimeSpan.FromSeconds(1));
    }

    private static string BuildProjectReferenceGlobExpression(string pattern)
    {
        var expression = new StringBuilder("^");
        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '*')
            {
                bool recursive = index + 1 < pattern.Length && pattern[index + 1] == '*';
                bool followedBySeparator = recursive &&
                    index + 2 < pattern.Length &&
                    pattern[index + 2] == '/';
                expression.Append(followedBySeparator
                    ? "(?:.*/)?"
                    : recursive
                        ? ".*"
                        : "[^/]*");
                if (followedBySeparator)
                    index += 2;
                else if (recursive)
                    index++;
            }
            else if (character == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }
        expression.Append('$');
        return expression.ToString();
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
