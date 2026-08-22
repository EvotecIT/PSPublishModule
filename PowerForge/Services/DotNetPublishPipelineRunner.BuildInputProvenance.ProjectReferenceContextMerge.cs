namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static EvaluatedProjectReference[] MergeResolvedProjectReferenceContexts(
        IEnumerable<EvaluatedProjectReference> rawReferences,
        IEnumerable<EvaluatedProjectReference> resolvedReferences)
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

            foreach (EvaluatedProjectReference rawReference in matchingRaw)
            {
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
        }

        return results.Values.ToArray();
    }
}
