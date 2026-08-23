using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void ApplyScheduledTargetPropertyDefinitions(
        XElement target,
        List<Dictionary<string, string>> propertyContexts,
        ISet<string> immutableGlobalProperties,
        ISet<string> scheduledPropertyNames)
    {
        foreach (XElement definition in target.Descendants().Where(element =>
                     element.Parent?.Name.LocalName.Equals(
                         "PropertyGroup",
                         StringComparison.OrdinalIgnoreCase) == true))
        {
            string propertyName = definition.Name.LocalName;
            if (!scheduledPropertyNames.Contains(propertyName) ||
                immutableGlobalProperties.Contains(propertyName))
                continue;

            var nextContexts = new List<Dictionary<string, string>>();
            foreach (Dictionary<string, string> context in propertyContexts)
            {
                if (IsDefinitelyInactiveMsBuildElement(definition, context))
                {
                    nextContexts.Add(context);
                    continue;
                }

                bool definitelyActive = IsDefinitelyActiveMsBuildElement(definition, context);
                if (!definitelyActive)
                    nextContexts.Add(context);
                if (TryExpandTargetTimePropertyValue(definition.Value, context, out string? value))
                {
                    var assigned = new Dictionary<string, string>(
                        context,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        [propertyName] = value!
                    };
                    nextContexts.Add(assigned);
                }
            }

            propertyContexts.Clear();
            propertyContexts.AddRange(DeduplicateScheduledTargetPropertyContexts(nextContexts));
            if (propertyContexts.Count == 0)
                propertyContexts.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<Dictionary<string, string>> DeduplicateScheduledTargetPropertyContexts(
        IEnumerable<Dictionary<string, string>> contexts)
    {
        Dictionary<string, string>[] distinct = contexts
            .GroupBy(BuildScheduledTargetPropertyContextKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (distinct.Length > MaximumProjectReferencePropertyContexts)
        {
            throw new InvalidOperationException(
                "Scheduled target property replay exceeded the bounded provenance context limit.");
        }
        return distinct;
    }

    private static string BuildScheduledTargetPropertyContextKey(
        IReadOnlyDictionary<string, string> context)
    {
        var key = new StringBuilder();
        foreach (KeyValuePair<string, string> property in context.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            AppendProjectReferenceKeySegment(key, property.Key.ToUpperInvariant());
            AppendProjectReferenceKeySegment(key, property.Value);
        }
        return key.ToString();
    }

    private static IEnumerable<string> ReadExpandedMsBuildTargetList(
        string? value,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        string expanded = ExpandKnownMsBuildTargetPropertyFunctions(value!, evaluatedProperties);
        expanded = Regex.Replace(
            expanded,
            @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
            match => evaluatedProperties.TryGetValue(match.Groups[1].Value, out string? propertyValue)
                ? propertyValue
                : match.Value,
            RegexOptions.CultureInvariant);
        return expanded.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0 && entry.IndexOf("$(", StringComparison.Ordinal) < 0)
            .ToArray();
    }

    private static string ExpandKnownMsBuildTargetPropertyFunctions(
        string value,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        return Regex.Replace(
            value,
            @"\$\(\[System\.String\]::Concat\((?<arguments>.*?)\)\)",
            match => TryExpandStringConcatArguments(
                match.Groups["arguments"].Value,
                evaluatedProperties,
                out string? expanded)
                    ? expanded!
                    : match.Value,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static bool TryExpandStringConcatArguments(
        string arguments,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        out string? expanded)
    {
        expanded = null;
        MatchCollection matches = Regex.Matches(
            arguments,
            @"(?:^|,)\s*(?:(?<quote>['""])(?<literal>.*?)\k<quote>|\$\((?<property>[A-Za-z_][A-Za-z0-9_.-]*)\))\s*(?=,|$)",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (matches.Count == 0 ||
            string.Concat(matches.Cast<Match>().Select(match => match.Value)).Replace(" ", string.Empty) !=
            arguments.Replace(" ", string.Empty))
        {
            return false;
        }

        var result = new StringBuilder();
        foreach (Match match in matches)
        {
            if (match.Groups["literal"].Success)
            {
                result.Append(match.Groups["literal"].Value);
            }
            else if (evaluatedProperties.TryGetValue(
                         match.Groups["property"].Value,
                         out string? propertyValue))
            {
                result.Append(propertyValue);
            }
            else
            {
                return false;
            }
        }

        expanded = result.ToString();
        return true;
    }

    private static bool TryReadPreprocessedImportPath(string comment, out string? importPath)
    {
        importPath = comment
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(candidate => Path.IsPathRooted(candidate) && File.Exists(candidate));
        if (importPath is null)
            return false;

        importPath = Path.GetFullPath(importPath);
        return true;
    }
}
