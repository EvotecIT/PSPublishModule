using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const int MaximumProjectReferencePropertyContexts = 128;

    private static bool TryOverlayProjectReferenceProperties(
        IReadOnlyList<Dictionary<string, string>> propertyContexts,
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

        if (!TryReadProjectReferencePropertyTables(assignments!, out Dictionary<string, string>[] overlays))
            return false;

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

    private static bool TryReadProjectReferencePropertyTables(
        string assignments,
        out Dictionary<string, string>[] tables)
    {
        var results = new List<Dictionary<string, string>>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string[] segments = assignments.Split(new[] { ';' });
        if (!TryExpandProjectReferencePropertyTables(
                segments,
                index: 0,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                currentName: null,
                currentValue: string.Empty,
                results,
                keys))
        {
            tables = Array.Empty<Dictionary<string, string>>();
            return false;
        }

        tables = results.ToArray();
        return tables.Length > 0;
    }

    private static bool TryExpandProjectReferencePropertyTables(
        IReadOnlyList<string> segments,
        int index,
        Dictionary<string, string> completed,
        string? currentName,
        string currentValue,
        List<Dictionary<string, string>> results,
        HashSet<string> keys)
    {
        if (results.Count > MaximumProjectReferencePropertyContexts)
            return false;

        if (index >= segments.Count)
        {
            var result = new Dictionary<string, string>(completed, StringComparer.OrdinalIgnoreCase);
            if (currentName is not null)
                result[currentName] = currentValue;
            if (keys.Add(BuildProjectReferencePropertyTableKey(result)))
                results.Add(result);
            return results.Count <= MaximumProjectReferencePropertyContexts;
        }

        string segment = segments[index];
        int separator = segment.IndexOf('=');
        if (currentName is null)
        {
            if (separator <= 0)
            {
                return TryExpandProjectReferencePropertyTables(
                    segments,
                    index + 1,
                    completed,
                    currentName: null,
                    currentValue: string.Empty,
                    results,
                    keys);
            }

            string name = segment.Substring(0, separator).Trim();
            if (name.Length == 0)
            {
                return TryExpandProjectReferencePropertyTables(
                    segments,
                    index + 1,
                    completed,
                    currentName: null,
                    currentValue: string.Empty,
                    results,
                    keys);
            }

            return TryExpandProjectReferencePropertyTables(
                segments,
                index + 1,
                completed,
                name,
                segment.Substring(separator + 1).Trim(),
                results,
                keys);
        }

        if (!TryExpandProjectReferencePropertyTables(
                segments,
                index + 1,
                completed,
                currentName,
                currentValue + ";" + segment,
                results,
                keys))
        {
            return false;
        }

        if (segment.Length == 0)
        {
            return TryExpandProjectReferencePropertyTables(
                segments,
                index + 1,
                completed,
                currentName,
                currentValue,
                results,
                keys);
        }

        if (separator <= 0)
            return true;

        string nextName = segment.Substring(0, separator).Trim();
        if (nextName.Length == 0)
            return true;

        var nextCompleted = new Dictionary<string, string>(completed, StringComparer.OrdinalIgnoreCase)
        {
            [currentName] = currentValue
        };
        return TryExpandProjectReferencePropertyTables(
            segments,
            index + 1,
            nextCompleted,
            nextName,
            segment.Substring(separator + 1).Trim(),
            results,
            keys);
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
