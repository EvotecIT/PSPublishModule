using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const int MaximumProjectReferencePropertyContexts = 128;

    private sealed class PreprocessedProjectReferenceDeclaration
    {
        internal PreprocessedProjectReferenceDeclaration(XElement element, string definingProjectPath)
        {
            Element = element;
            DefiningProjectPath = definingProjectPath;
        }

        internal XElement Element { get; }

        internal string DefiningProjectPath { get; }
    }

    private sealed class LiteralProjectReferenceMetadataAssignment
    {
        internal LiteralProjectReferenceMetadataAssignment(string value, string definingProjectPath)
        {
            Value = value;
            DefiningProjectPath = definingProjectPath;
        }

        internal string Value { get; }

        internal string DefiningProjectPath { get; }
    }

    private sealed class LiteralProjectReferenceItemState
    {
        internal LiteralProjectReferenceItemState(IEnumerable<LiteralProjectReferenceMetadataAssignment> assignments)
        {
            Assignments = assignments.ToList();
        }

        internal List<LiteralProjectReferenceMetadataAssignment> Assignments { get; set; }
    }

    private static bool TryOverlayProjectReferenceProperties(
        IReadOnlyList<Dictionary<string, string>> propertyContexts,
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName,
        string? assignments,
        out List<Dictionary<string, string>> results)
    {
        results = new List<Dictionary<string, string>>();
        if (string.IsNullOrEmpty(assignments))
        {
            results.AddRange(propertyContexts.Select(context =>
                new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase)));
            return true;
        }

        if (!TryReadLiteralProjectReferencePropertyTables(
                item,
                declaringProjectPath,
                projectPathMetadataName,
                propertyDefinitionPaths,
                projectReferenceDeclarations,
                evaluatedConditionProperties,
                metadataName,
                assignments!,
                out Dictionary<string, string>[] overlays) &&
            !TryReadProjectReferencePropertyTables(assignments!, out overlays))
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Dictionary<string, string> propertyContext in propertyContexts)
        {
            foreach (Dictionary<string, string> overlay in overlays)
            {
                var result = new Dictionary<string, string>(propertyContext, StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> property in overlay)
                    result[property.Key] = property.Value;
                if (keys.Add(BuildProjectReferencePropertyTableKey(result)))
                    results.Add(result);
                if (results.Count > MaximumProjectReferencePropertyContexts)
                    return false;
            }
        }

        return results.Count > 0;
    }

    private static bool TryReadLiteralProjectReferencePropertyTables(
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName,
        string evaluatedAssignments,
        out Dictionary<string, string>[] tables)
    {
        tables = Array.Empty<Dictionary<string, string>>();
        string? referencedProject = ReadItemText(item, projectPathMetadataName);
        if (string.IsNullOrWhiteSpace(referencedProject))
        {
            return false;
        }

        try
        {
            string referencedPath = Path.GetFullPath(referencedProject!);
            var results = new List<Dictionary<string, string>>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string[] declarationProjects = new[]
                {
                    ReadItemText(item, "DefiningProjectFullPath"),
                    declaringProjectPath
                }
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Concat(propertyDefinitionPaths.Where(path =>
                    !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
                .Select(path => Path.GetFullPath(path!))
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            LiteralProjectReferenceMetadataAssignment[] effectiveAssignments =
                ReadEffectiveLiteralProjectReferenceMetadataAssignments(
                    item,
                    declaringProjectPath,
                    referencedPath,
                    projectReferenceDeclarations,
                    evaluatedConditionProperties,
                    metadataName);
            foreach (LiteralProjectReferenceMetadataAssignment assignment in effectiveAssignments)
            {
                foreach (string candidateAssignments in ReadLiteralProjectReferencePropertyAssignmentCandidates(
                             declarationProjects,
                             evaluatedConditionProperties,
                             assignment.DefiningProjectPath,
                             assignment.Value))
                {
                    if (!TryReadLiteralProjectReferencePropertyTable(
                            candidateAssignments,
                            evaluatedAssignments,
                            out Dictionary<string, string>? table))
                    {
                        continue;
                    }

                    if (keys.Add(BuildProjectReferencePropertyTableKey(table!)))
                        results.Add(table!);
                }
            }

            tables = results.ToArray();
            return tables.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static LiteralProjectReferenceMetadataAssignment[]
        ReadEffectiveLiteralProjectReferenceMetadataAssignments(
            JsonElement item,
            string declaringProjectPath,
            string referencedPath,
            IReadOnlyList<PreprocessedProjectReferenceDeclaration> declarations,
            IReadOnlyDictionary<string, string> evaluatedConditionProperties,
            string metadataName)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var defaults = new List<LiteralProjectReferenceMetadataAssignment>();
        var states = new List<LiteralProjectReferenceItemState>();
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations)
        {
            XElement projectReference = declaration.Element;
            if (IsDefinitelyInactiveMsBuildElement(projectReference, evaluatedConditionProperties))
                continue;

            bool isItemDefinition = projectReference.Parent?.Name.LocalName.Equals(
                "ItemDefinitionGroup",
                StringComparison.OrdinalIgnoreCase) == true;
            bool isInclude = projectReference.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase));
            bool isUpdate = projectReference.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase));
            if (!isItemDefinition &&
                !DoesProjectReferenceDeclarationMatch(
                    item,
                    declaringProjectPath,
                    referencedPath,
                    declaration,
                    comparison))
            {
                continue;
            }

            List<LiteralProjectReferenceMetadataAssignment> declaredAssignments =
                ReadActiveLiteralProjectReferenceMetadataAssignments(
                    projectReference,
                    declaration.DefiningProjectPath,
                    evaluatedConditionProperties,
                    metadataName);
            bool definitelyActive = IsDefinitelyActiveMsBuildElement(
                projectReference,
                evaluatedConditionProperties);
            if (isItemDefinition)
            {
                if (declaredAssignments.Count > 0)
                {
                    defaults = definitelyActive
                        ? declaredAssignments
                        : MergeLiteralProjectReferenceMetadataAssignments(defaults, declaredAssignments);
                }
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
            .GroupBy(
                BuildLiteralProjectReferenceMetadataAssignmentKey,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool DoesProjectReferenceDeclarationMatch(
        JsonElement item,
        string declaringProjectPath,
        string referencedPath,
        PreprocessedProjectReferenceDeclaration declaration,
        StringComparison comparison)
    {
        string definingDirectory = Path.GetDirectoryName(declaration.DefiningProjectPath)!;
        string[] identityBaseDirectories =
        [
            Path.GetDirectoryName(declaringProjectPath)!,
            definingDirectory
        ];
        bool evaluatedIdentityMatchesReference = new[]
            {
                ReadItemText(item, "OriginalItemSpec"),
                ReadItemText(item, "Identity")
            }
            .Where(itemSpec => !string.IsNullOrWhiteSpace(itemSpec))
            .Any(itemSpec => identityBaseDirectories
                .Distinct(IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .Any(baseDirectory =>
                    TryResolveLiteralProjectReferencePath(
                        baseDirectory,
                        itemSpec,
                        out string? identityPath) &&
                    string.Equals(identityPath, referencedPath, comparison)));
        return declaration.Element.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("Update", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Value)
            .Any(itemSpec => identityBaseDirectories
                    .Distinct(IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal)
                    .Any(baseDirectory =>
                        TryResolveLiteralProjectReferencePath(
                            baseDirectory,
                            itemSpec,
                            out string? declaredPath) &&
                        string.Equals(declaredPath, referencedPath, comparison)) ||
                (evaluatedIdentityMatchesReference && IsComputedProjectReferenceItemSpec(itemSpec)));
    }

    private static List<LiteralProjectReferenceMetadataAssignment>
        ReadActiveLiteralProjectReferenceMetadataAssignments(
            XElement projectReference,
            string definingProjectPath,
            IReadOnlyDictionary<string, string> evaluatedConditionProperties,
            string metadataName)
    {
        var assignments = projectReference.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => new LiteralProjectReferenceMetadataAssignment(
                attribute.Value,
                definingProjectPath))
            .ToList();
        foreach (XElement element in projectReference.Elements().Where(element =>
                     element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase) &&
                     !IsDefinitelyInactiveMsBuildElement(element, evaluatedConditionProperties)))
        {
            var assignment = new LiteralProjectReferenceMetadataAssignment(
                element.Value,
                definingProjectPath);
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
            .GroupBy(
                BuildLiteralProjectReferenceMetadataAssignmentKey,
                StringComparer.Ordinal)
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

    private static string[] ReadLiteralProjectReferencePropertyAssignmentCandidates(
        IEnumerable<string> propertyDefinitionPaths,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataDefinitionPath,
        string? rawAssignments)
    {
        if (string.IsNullOrWhiteSpace(rawAssignments))
            return Array.Empty<string>();

        var propertyDefinitions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        string fileScopedAssignments = ExpandMsBuildThisFileProperties(rawAssignments!, metadataDefinitionPath);
        var candidates = new HashSet<string>(StringComparer.Ordinal) { fileScopedAssignments };
        pending.Enqueue(fileScopedAssignments);
        while (pending.Count > 0 && candidates.Count <= MaximumProjectReferencePropertyContexts)
        {
            string candidate = pending.Dequeue();
            if (!TryFindSimpleMsBuildPropertyExpression(
                    candidate,
                    out int expressionStart,
                    out int expressionLength,
                    out string? propertyName))
            {
                continue;
            }

            if (!propertyDefinitions.TryGetValue(propertyName!, out string[]? values))
            {
                values = ReadLiteralMsBuildPropertyDefinitions(
                        propertyDefinitionPaths,
                        evaluatedConditionProperties,
                        propertyName!)
                    .Concat(evaluatedConditionProperties.TryGetValue(propertyName!, out string? evaluatedValue)
                            && IsSafeEvaluatedProjectReferencePropertyExpansion(evaluatedValue)
                        ? new[] { evaluatedValue }
                        : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                propertyDefinitions[propertyName!] = values;
            }

            foreach (string value in values)
            {
                string expanded = candidate.Substring(0, expressionStart) +
                                  value +
                                  candidate.Substring(expressionStart + expressionLength);
                if (candidates.Add(expanded))
                    pending.Enqueue(expanded);
                if (candidates.Count > MaximumProjectReferencePropertyContexts)
                    return [fileScopedAssignments];
            }
        }

        return candidates.ToArray();
    }

    private static bool IsSafeEvaluatedProjectReferencePropertyExpansion(string value)
        => value.IndexOf(';') < 0 && value.IndexOf('=') < 0;

    private static string[] ReadLiteralMsBuildPropertyDefinitions(
        IEnumerable<string> propertyDefinitionPaths,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string propertyName)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string propertyDefinitionPath in propertyDefinitionPaths)
        {
            try
            {
                XDocument document = XDocument.Load(propertyDefinitionPath, LoadOptions.None);
                foreach (XElement property in document.Descendants().Where(element =>
                             element.Parent?.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) == true &&
                             element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!IsDefinitelyInactiveMsBuildElement(property, evaluatedConditionProperties))
                        values.Add(ExpandMsBuildThisFileProperties(property.Value, propertyDefinitionPath));
                }
            }
            catch
            {
                // Only exact literal definitions that decode to the evaluated value are trusted.
            }
        }

        return values.ToArray();
    }

    private static string ExpandMsBuildThisFileProperties(string value, string definingProjectPath)
    {
        string fullPath = Path.GetFullPath(definingProjectPath);
        string directory = Path.GetDirectoryName(fullPath)! + Path.DirectorySeparatorChar;
        string root = Path.GetPathRoot(directory) ?? string.Empty;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildThisFileFullPath"] = fullPath,
            ["MSBuildThisFileDirectory"] = directory,
            ["MSBuildThisFileDirectoryNoRoot"] = directory.Substring(root.Length),
            ["MSBuildThisFile"] = Path.GetFileName(fullPath),
            ["MSBuildThisFileName"] = Path.GetFileNameWithoutExtension(fullPath),
            ["MSBuildThisFileExtension"] = Path.GetExtension(fullPath)
        };
        foreach (KeyValuePair<string, string> replacement in replacements)
        {
            value = ReplaceOrdinalIgnoreCase(
                value,
                "$(" + replacement.Key + ")",
                replacement.Value);
        }
        return value;
    }

    private static bool TryFindSimpleMsBuildPropertyExpression(
        string value,
        out int expressionStart,
        out int expressionLength,
        out string? propertyName)
    {
        expressionStart = value.IndexOf("$(", StringComparison.Ordinal);
        expressionLength = 0;
        propertyName = null;
        if (expressionStart < 0)
            return false;

        int expressionEnd = value.IndexOf(')', expressionStart + 2);
        if (expressionEnd < 0)
            return false;

        string candidate = value.Substring(expressionStart + 2, expressionEnd - expressionStart - 2).Trim();
        if (candidate.Length == 0 ||
            candidate.IndexOfAny(new[] { '$', '(', ')', ';', '=' }) >= 0)
        {
            return false;
        }

        propertyName = candidate;
        expressionLength = expressionEnd - expressionStart + 1;
        return true;
    }

    private static bool TryResolveLiteralProjectReferencePath(
        string definingDirectory,
        string? include,
        out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(include) ||
            include!.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            include.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            include.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            include.IndexOf('*') >= 0 ||
            include.IndexOf('?') >= 0 ||
            !TryUnescapeMsBuildLiteral(include, out string? unescapedInclude))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.IsPathRooted(unescapedInclude!)
            ? unescapedInclude!
            : Path.Combine(definingDirectory, unescapedInclude!));
        return true;
    }

    private static bool IsComputedProjectReferenceItemSpec(string? itemSpec)
        => !string.IsNullOrWhiteSpace(itemSpec) &&
           (itemSpec!.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            itemSpec.IndexOf('*') >= 0 ||
            itemSpec.IndexOf('?') >= 0);

    private static bool TryReadLiteralProjectReferencePropertyTable(
        string? rawAssignments,
        string evaluatedAssignments,
        out Dictionary<string, string>? table)
    {
        table = null;
        if (string.IsNullOrEmpty(rawAssignments) ||
            rawAssignments!.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(rawAssignments, out string? decodedAssignments) ||
            !string.Equals(
                decodedAssignments!.Trim(),
                evaluatedAssignments.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string segment in rawAssignments.Split(new[] { ';' }))
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            int separator = segment.IndexOf('=');
            if (separator <= 0 ||
                !TryUnescapeMsBuildLiteral(segment.Substring(0, separator).Trim(), out string? name) ||
                !TryUnescapeMsBuildLiteral(segment.Substring(separator + 1).Trim(), out string? value) ||
                string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            result[name!.Trim()] = value!;
        }

        table = result;
        return result.Count > 0;
    }

    private static bool TryUnescapeMsBuildLiteral(string value, out string? unescaped)
    {
        try
        {
            unescaped = Uri.UnescapeDataString(value);
            return true;
        }
        catch
        {
            unescaped = null;
            return false;
        }
    }

    private static bool TryReadProjectReferencePropertyTables(
        string assignments,
        out Dictionary<string, string>[] tables)
    {
        string[] segments = assignments
            .Split(new[] { ';' })
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length != 1)
        {
            tables = Array.Empty<Dictionary<string, string>>();
            return false;
        }

        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string segment in segments)
        {
            int separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                tables = Array.Empty<Dictionary<string, string>>();
                return false;
            }

            string name = segment.Substring(0, separator).Trim();
            if (name.Length == 0)
            {
                tables = Array.Empty<Dictionary<string, string>>();
                return false;
            }

            table[name] = segment.Substring(separator + 1).Trim();
        }

        tables = table.Count == 0 ? Array.Empty<Dictionary<string, string>>() : [table];
        return tables.Length > 0;
    }

    private static string BuildProjectReferencePropertyTableKey(
        IReadOnlyDictionary<string, string> properties)
    {
        var key = new StringBuilder();
        foreach (KeyValuePair<string, string> property in properties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(property.Key));
            AppendProjectReferenceKeySegment(key, property.Value);
        }
        return key.ToString();
    }
}
