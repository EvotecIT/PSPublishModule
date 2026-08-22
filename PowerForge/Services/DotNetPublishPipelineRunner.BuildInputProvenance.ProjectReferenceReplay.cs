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
            bool isTargetTime)
        {
            Element = element;
            DefiningProjectPath = definingProjectPath;
            PropertyDefinitions = propertyDefinitions;
            IsTargetTime = isTargetTime;
        }

        internal XElement Element { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }

        internal bool IsTargetTime { get; }
    }

    private sealed class LiteralProjectReferenceMetadataAssignment
    {
        internal LiteralProjectReferenceMetadataAssignment(
            string value,
            PreprocessedProjectReferenceDeclaration declaration)
        {
            Value = value;
            DefiningProjectPath = declaration.DefiningProjectPath;
            PropertyDefinitions = declaration.PropertyDefinitions;
        }

        internal string Value { get; }

        internal string DefiningProjectPath { get; }

        internal IReadOnlyList<PreprocessedProjectPropertyDefinition> PropertyDefinitions { get; }
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
                     IsProjectReferenceItemDefinition(declaration.Element) &&
                     !IsDefinitelyInactiveMsBuildElement(
                         declaration.Element,
                         evaluatedConditionProperties)))
        {
            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    evaluatedConditionProperties,
                    metadataName);
            if (declaredAssignments.Count == 0)
                continue;

            defaults = IsDefinitelyActiveMsBuildElement(
                declaration.Element,
                evaluatedConditionProperties)
                ? declaredAssignments
                : MergeLiteralProjectReferenceMetadataAssignments(defaults, declaredAssignments);
        }

        var states = new List<LiteralProjectReferenceItemState>();
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations)
        {
            XElement projectReference = declaration.Element;
            if ((!includeTargetTime && declaration.IsTargetTime) ||
                IsProjectReferenceItemDefinition(projectReference) ||
                IsDefinitelyInactiveMsBuildElement(projectReference, evaluatedConditionProperties) ||
                !DoesProjectReferenceDeclarationMatch(
                    declaringProjectPath,
                    referencedPath,
                    declaration,
                    evaluatedConditionProperties))
            {
                continue;
            }

            bool isInclude = HasMsBuildAttribute(projectReference, "Include");
            bool isUpdate = HasMsBuildAttribute(projectReference, "Update");
            bool isRemove = HasMsBuildAttribute(projectReference, "Remove");
            bool definitelyActive = IsDefinitelyActiveMsBuildElement(
                projectReference,
                evaluatedConditionProperties);
            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    evaluatedConditionProperties,
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
        foreach (string itemSpec in declaration.Element.Attributes()
                     .Where(attribute =>
                         attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                         attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                         attribute.Name.LocalName.Equals("Remove", StringComparison.OrdinalIgnoreCase))
                     .Select(attribute => attribute.Value))
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
                foreach (string individualItemSpec in candidate.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (string baseDirectory in identityBaseDirectories.Distinct(
                                 IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    {
                        if (TryResolveLiteralProjectReferencePath(
                                baseDirectory,
                                individualItemSpec,
                                out string? declaredPath) &&
                            string.Equals(declaredPath, referencedPath, comparison))
                        {
                            return true;
                        }

                        if (TryMatchProjectReferenceGlob(
                                baseDirectory,
                                individualItemSpec,
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
                expression.Append(recursive ? ".*" : "[^/]*");
                if (recursive)
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
                declaration))
            .ToList();
        foreach (XElement element in projectReference.Elements().Where(element =>
                     element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                     !IsDefinitelyInactiveMsBuildElement(element, evaluatedConditionProperties)))
        {
            var assignment = new LiteralProjectReferenceMetadataAssignment(element.Value, declaration);
            assignments = IsDefinitelyActiveMsBuildElement(element, evaluatedConditionProperties)
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
