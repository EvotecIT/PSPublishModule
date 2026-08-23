using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const int MaximumProjectReferencePropertyContexts = 128;

    private static bool TryOverlayProjectReferenceProperties(
        IReadOnlyList<Dictionary<string, string>> propertyContexts,
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName,
        string? assignments,
        bool preferEffectiveLiteralAssignments,
        out List<Dictionary<string, string>> results)
    {
        results = new List<Dictionary<string, string>>();
        if (string.IsNullOrEmpty(assignments))
        {
            results.AddRange(propertyContexts.Select(context =>
                new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase)));
            return true;
        }

        bool readLiteralTables = TryReadLiteralProjectReferencePropertyTables(
                item,
                declaringProjectPath,
                projectPathMetadataName,
                projectReferenceDeclarations,
                evaluatedConditionProperties,
                metadataName,
                assignments!,
                preferEffectiveLiteralAssignments,
                out Dictionary<string, string>[] overlays,
                out bool hadEffectiveAssignments);
        if (!readLiteralTables &&
            preferEffectiveLiteralAssignments &&
            !hadEffectiveAssignments)
        {
            results.AddRange(propertyContexts.Select(context =>
                new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase)));
            return true;
        }
        if (!readLiteralTables &&
            (preferEffectiveLiteralAssignments ||
             !TryReadProjectReferencePropertyTables(assignments!, out overlays)))
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
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName,
        string evaluatedAssignments,
        bool preferEffectiveLiteralAssignments,
        out Dictionary<string, string>[] tables,
        out bool hadEffectiveAssignments)
    {
        tables = Array.Empty<Dictionary<string, string>>();
        hadEffectiveAssignments = false;
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
            bool hasPostResolveAssignments = HasPostResolveProjectReferenceMetadataAssignment(
                declaringProjectPath,
                referencedPath,
                projectReferenceDeclarations,
                evaluatedConditionProperties,
                metadataName);
            LiteralProjectReferenceMetadataAssignment[] effectiveAssignments =
                ReadEffectiveLiteralProjectReferenceMetadataAssignments(
                    declaringProjectPath,
                    referencedPath,
                    projectReferenceDeclarations,
                    evaluatedConditionProperties,
                    metadataName,
                    includeTargetTime: false,
                    evaluatedAssignments);
            AddLiteralPropertyTables(effectiveAssignments);
            if (projectReferenceDeclarations.Any(declaration =>
                    declaration.IsTargetTime && declaration.RunsBeforeResolveReferences))
            {
                effectiveAssignments = ReadEffectiveLiteralProjectReferenceMetadataAssignments(
                    declaringProjectPath,
                    referencedPath,
                    projectReferenceDeclarations,
                    evaluatedConditionProperties,
                    metadataName,
                    includeTargetTime: true,
                    evaluatedAssignments);
                results.Clear();
                keys.Clear();
                AddLiteralPropertyTables(effectiveAssignments);
            }

            hadEffectiveAssignments = effectiveAssignments.Length > 0;
            tables = results.ToArray();
            return tables.Length > 0;

            void AddLiteralPropertyTables(
                IEnumerable<LiteralProjectReferenceMetadataAssignment> assignments)
            {
                foreach (LiteralProjectReferenceMetadataAssignment assignment in assignments)
                {
                    foreach (string candidateAssignments in ReadLiteralProjectReferencePropertyAssignmentCandidates(
                                 assignment.PropertyDefinitions,
                                 assignment.InitialProperties,
                                 assignment.ConditionProperties,
                                 assignment.DefiningProjectPath,
                                 assignment.Value))
                    {
                        bool parsed = preferEffectiveLiteralAssignments
                            ? candidateAssignments.IndexOf("$(", StringComparison.Ordinal) >= 0 &&
                              !hasPostResolveAssignments
                                ? TryReadLiteralProjectReferencePropertyTable(
                                    candidateAssignments,
                                    evaluatedAssignments,
                                    out Dictionary<string, string>? table)
                                : TryReadDeclaredProjectReferencePropertyTable(
                                    candidateAssignments,
                                    out table)
                            : TryReadLiteralProjectReferencePropertyTable(
                                candidateAssignments,
                                evaluatedAssignments,
                                out table);
                        if (!parsed)
                        {
                            continue;
                        }

                        if (keys.Add(BuildProjectReferencePropertyTableKey(table!)))
                            results.Add(table!);
                    }
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPostResolveProjectReferenceMetadataAssignment(
        string declaringProjectPath,
        string referencedPath,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> declarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataName)
    {
        foreach (PreprocessedProjectReferenceDeclaration declaration in declarations.Where(declaration =>
                     declaration.IsTargetTime && !declaration.RunsBeforeResolveReferences))
        {
            IReadOnlyDictionary<string, string> conditionProperties = BuildTargetTimeConditionProperties(
                evaluatedConditionProperties,
                declaration.RuntimePropertyDefinitions);
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

            if (ReadActiveLiteralProjectReferenceMetadataAssignments(
                    declaration,
                    conditionProperties,
                    metadataName).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ReadLiteralProjectReferencePropertyAssignmentCandidates(
        IReadOnlyList<PreprocessedProjectPropertyDefinition> propertyDefinitions,
        IReadOnlyDictionary<string, string> initialProperties,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        string metadataDefinitionPath,
        string? rawAssignments)
    {
        if (string.IsNullOrWhiteSpace(rawAssignments))
            return Array.Empty<string>();

        var propertyValueCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
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

            if (!propertyValueCache.TryGetValue(propertyName!, out string[]? values))
            {
                values = ReadLiteralMsBuildPropertyDefinitions(
                        propertyDefinitions,
                        initialProperties,
                        evaluatedConditionProperties,
                        propertyName!)
                    .Concat(evaluatedConditionProperties.TryGetValue(propertyName!, out string? evaluatedValue)
                            && IsSafeEvaluatedProjectReferencePropertyExpansion(evaluatedValue)
                        ? new[] { evaluatedValue }
                        : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                propertyValueCache[propertyName!] = values;
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
        if (string.IsNullOrEmpty(rawAssignments))
            return false;

        if (rawAssignments!.IndexOf("$(", StringComparison.Ordinal) >= 0)
        {
            return TryReadEvaluatedProjectReferencePropertyFunctionTable(
                rawAssignments,
                evaluatedAssignments,
                out table);
        }

        if (rawAssignments.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(rawAssignments, out string? decodedAssignments) ||
            !string.Equals(
                decodedAssignments!.Trim(),
                evaluatedAssignments.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }

        return TryReadDeclaredProjectReferencePropertyTable(rawAssignments, out table);
    }

    private static bool TryReadDeclaredProjectReferencePropertyTable(
        string? rawAssignments,
        out Dictionary<string, string>? table)
    {
        table = null;
        if (string.IsNullOrEmpty(rawAssignments) ||
            rawAssignments!.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("%(", StringComparison.Ordinal) >= 0)
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

    private static bool TryReadEvaluatedProjectReferencePropertyFunctionTable(
        string rawAssignments,
        string evaluatedAssignments,
        out Dictionary<string, string>? table)
    {
        table = null;
        if (rawAssignments.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            rawAssignments.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryReadProjectReferencePropertySegments(rawAssignments, out string[] rawNames, out _) ||
            !TryReadProjectReferencePropertySegments(
                evaluatedAssignments,
                out string[] evaluatedNames,
                out string[] evaluatedValues) ||
            rawNames.Length != evaluatedNames.Length)
        {
            return false;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < rawNames.Length; index++)
        {
            if (!string.Equals(rawNames[index], evaluatedNames[index], StringComparison.OrdinalIgnoreCase))
                return false;
            result[rawNames[index]] = evaluatedValues[index];
        }

        table = result;
        return result.Count > 0;
    }

    private static bool TryReadProjectReferencePropertySegments(
        string assignments,
        out string[] names,
        out string[] values)
    {
        var parsedNames = new List<string>();
        var parsedValues = new List<string>();
        foreach (string segment in assignments.Split(new[] { ';' }))
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            int separator = segment.IndexOf('=');
            if (separator <= 0 ||
                !TryUnescapeMsBuildLiteral(segment.Substring(0, separator).Trim(), out string? name) ||
                string.IsNullOrWhiteSpace(name))
            {
                names = Array.Empty<string>();
                values = Array.Empty<string>();
                return false;
            }

            parsedNames.Add(name!.Trim());
            parsedValues.Add(segment.Substring(separator + 1).Trim());
        }

        names = parsedNames.ToArray();
        values = parsedValues.ToArray();
        return names.Length > 0;
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
