using System.Text.Json;

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
        out string[] removals)
    {
        removals = Array.Empty<string>();
        if (!preferEffectiveLiteralAssignments)
        {
            removals = ReadProjectReferencePropertyNames(
                ReadItemText(item, "UndefineProperties"),
                ReadItemText(item, "GlobalPropertiesToRemove"),
                string.Join(";", taskWidePropertyRemovals));
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
                            declaration.IsTargetTime && declaration.RunsBeforeResolveReferences));
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

            removals = results.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
