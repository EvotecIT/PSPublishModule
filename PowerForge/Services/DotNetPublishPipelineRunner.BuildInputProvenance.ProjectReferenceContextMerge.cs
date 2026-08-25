namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static EvaluatedProjectReference[] MergeResolvedProjectReferenceContexts(
        IEnumerable<EvaluatedProjectReference> rawReferences,
        IEnumerable<EvaluatedProjectReference> resolvedReferences,
        IEnumerable<EvaluatedProjectReference> publishEvaluatedReferences,
        ISet<string> mainEvaluationReferenceKeys)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        EvaluatedProjectReference[] raw = rawReferences.ToArray();
        var results = new Dictionary<string, EvaluatedProjectReference>(StringComparer.Ordinal);
        foreach (EvaluatedProjectReference resolved in resolvedReferences)
        {
            EvaluatedProjectReference[] matchingRaw = raw
                .Where(reference => string.Equals(
                    Path.GetFullPath(reference.ProjectPath),
                    Path.GetFullPath(resolved.ProjectPath),
                    comparison))
                .ToArray();
            if (matchingRaw.Length == 0)
            {
                results[BuildEvaluatedProjectReferenceKey(resolved)] = resolved;
                continue;
            }

            string resolvedKey = BuildEvaluatedProjectReferenceKey(resolved);
            if (matchingRaw.Any(rawReference => string.Equals(
                    BuildEvaluatedProjectReferenceKey(rawReference),
                    resolvedKey,
                    StringComparison.Ordinal)))
            {
                results[resolvedKey] = resolved;
                continue;
            }

            if (matchingRaw.Length != 1)
            {
                // ResolveReferences remains authoritative, while each raw branch is retained
                // independently. Never merge distinct raw property tables with each other.
                results[resolvedKey] = resolved;
                foreach (EvaluatedProjectReference rawCandidate in matchingRaw)
                {
                    var rawBranch = new EvaluatedProjectReference(
                        resolved.ProjectPath,
                        rawCandidate.TargetFramework ?? resolved.TargetFramework,
                        rawCandidate.GlobalProperties,
                        resolved.UndefineProperties
                            .Concat(rawCandidate.UndefineProperties)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray());
                    results[BuildEvaluatedProjectReferenceKey(rawBranch)] = rawBranch;
                }
                continue;
            }

            EvaluatedProjectReference rawReference = matchingRaw[0];
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in resolved.GlobalProperties)
                properties[property.Key] = property.Value;
            foreach (KeyValuePair<string, string> property in rawReference.GlobalProperties)
                properties[property.Key] = property.Value;

            var merged = new EvaluatedProjectReference(
                resolved.ProjectPath,
                rawReference.TargetFramework ?? resolved.TargetFramework,
                properties,
                resolved.UndefineProperties
                    .Concat(rawReference.UndefineProperties)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            results[BuildEvaluatedProjectReferenceKey(merged)] = merged;
        }

        foreach (EvaluatedProjectReference publishEvaluated in publishEvaluatedReferences)
        {
            string key = BuildEvaluatedProjectReferenceKey(publishEvaluated);
            if (!mainEvaluationReferenceKeys.Contains(key))
                results[key] = publishEvaluated;
        }

        return results.Values.ToArray();
    }
}
