using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const int MaximumProjectReferencePropertyContexts = 128;

    private static bool TryOverlayProjectReferenceProperties(
        IReadOnlyList<Dictionary<string, string>> propertyContexts,
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
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
            StringComparison comparison = IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var results = new List<Dictionary<string, string>>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string[] candidateProjects = new[]
                {
                    ReadItemText(item, "DefiningProjectFullPath"),
                    declaringProjectPath
                }
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => Path.GetFullPath(path!))
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            foreach (string candidateProject in candidateProjects)
            {
                string definingDirectory = Path.GetDirectoryName(candidateProject)!;
                XDocument document = XDocument.Load(candidateProject, LoadOptions.None);
                foreach (XElement projectReference in document.Descendants().Where(element =>
                             element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
                {
                    string? include = projectReference.Attribute("Include")?.Value;
                    if (!TryResolveLiteralProjectReferencePath(definingDirectory, include, out string? declaredPath) ||
                        !string.Equals(declaredPath, referencedPath, comparison))
                    {
                        continue;
                    }

                    string? rawAssignments = projectReference.Attributes().FirstOrDefault(attribute =>
                            attribute.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase))?.Value
                        ?? projectReference.Elements().FirstOrDefault(element =>
                            element.Name.LocalName.Equals(metadataName, StringComparison.OrdinalIgnoreCase))?.Value;
                    if (!TryReadLiteralProjectReferencePropertyTable(
                            rawAssignments,
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
