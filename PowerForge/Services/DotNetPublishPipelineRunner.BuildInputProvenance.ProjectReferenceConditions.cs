using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static IReadOnlyDictionary<string, string> BuildTargetTimeConditionProperties(
        IReadOnlyDictionary<string, string> evaluatedProperties,
        IReadOnlyList<PreprocessedProjectPropertyDefinition> runtimePropertyDefinitions)
    {
        if (runtimePropertyDefinitions.Count == 0)
            return evaluatedProperties;

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> property in evaluatedProperties)
            properties[property.Key] = property.Value;
        foreach (PreprocessedProjectPropertyDefinition definition in runtimePropertyDefinitions)
        {
            string propertyName = definition.Element.Name.LocalName;
            if (IsDefinitelyInactiveMsBuildElement(
                    definition.Element,
                    properties,
                    definition.DefiningProjectPath))
                continue;

            if (!IsDefinitelyActiveMsBuildElement(
                    definition.Element,
                    properties,
                    definition.DefiningProjectPath) ||
                !TryExpandTargetTimePropertyValue(definition.Element.Value, properties, out string? value))
            {
                properties.Remove(propertyName);
                continue;
            }

            properties[propertyName] = value!;
        }
        return properties;
    }

    private static bool TryExpandTargetTimePropertyValue(
        string value,
        IReadOnlyDictionary<string, string> properties,
        out string? expanded)
    {
        expanded = Regex.Replace(
            value,
            @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
            match => properties.TryGetValue(match.Groups[1].Value, out string? propertyValue)
                ? propertyValue
                : match.Value,
            RegexOptions.CultureInvariant);
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0 ||
            !TryUnescapeMsBuildLiteral(expanded, out string? unescaped))
        {
            expanded = null;
            return false;
        }
        expanded = unescaped;
        return true;
    }

    private static IReadOnlyDictionary<string, string> ReadEvaluatedProjectReferenceConditionProperties(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> propertyDefinitionPaths)
    {
        string[] propertyNames = ReadProjectReferenceConditionPropertyNames(
            propertyDefinitionPaths.Append(request.ProjectPath));
        return ReadEvaluatedProjectProperties(request, propertyNames);
    }

    private static IReadOnlyDictionary<string, string> ReadEvaluatedProjectProperties(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> propertyNames)
    {
        if (propertyNames.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var commonArguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet"
        };
        if (request.Configuration is not null)
            commonArguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        if (request.HasExplicitTargetFramework)
            commonArguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            commonArguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
        try
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] propertyBatch in BuildEvaluatedPropertyQueryBatches(
                         commonArguments,
                         propertyNames))
            {
                string[] arguments = commonArguments
                    .Concat(new[] { "-getProperty:MSBuildProjectFullPath" })
                    .Concat(propertyBatch.Select(propertyName => "-getProperty:" + propertyName))
                    .ToArray();
                var process = RunBuildInputEvaluationProcess(
                    "dotnet",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    arguments,
                    request.EnvironmentVariables,
                    TimeSpan.FromMinutes(2));
                if (process.ExitCode != 0 || process.TimedOut)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                int jsonStart = process.StdOut.IndexOf('{');
                int jsonEnd = process.StdOut.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < jsonStart)
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using JsonDocument document = JsonDocument.Parse(
                    process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
                if (!document.RootElement.TryGetProperty("Properties", out JsonElement properties))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string propertyName in propertyBatch)
                {
                    string? value = ReadItemText(properties, propertyName);
                    if (value is not null)
                        result[propertyName] = value;
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static string[][] BuildEvaluatedPropertyQueryBatches(
        IReadOnlyCollection<string> commonArguments,
        IEnumerable<string> propertyNames,
        int maximumCommandLength = 24000)
    {
        int commonLength = commonArguments.Sum(argument => argument.Length + 3) +
                           "-getProperty:MSBuildProjectFullPath".Length + 3;
        var batches = new List<string[]>();
        var current = new List<string>();
        int currentLength = commonLength;
        foreach (string propertyName in propertyNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            int argumentLength = "-getProperty:".Length + propertyName.Length + 3;
            if (current.Count > 0 && currentLength + argumentLength > maximumCommandLength)
            {
                batches.Add(current.ToArray());
                current.Clear();
                currentLength = commonLength;
            }
            current.Add(propertyName);
            currentLength += argumentLength;
        }
        if (current.Count > 0)
            batches.Add(current.ToArray());
        return batches.ToArray();
    }

    private static void AddConditionPropertyNames(string condition, ISet<string> names)
    {
        foreach (Match match in Regex.Matches(
                     condition,
                     @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
                     RegexOptions.CultureInvariant))
        {
            names.Add(match.Groups[1].Value);
        }
    }

    private static bool IsDefinitelyInactiveMsBuildElement(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? definingProjectPath = null)
    {
        foreach (XAttribute condition in element.AncestorsAndSelf()
                     .Select(candidate => candidate.Attribute("Condition"))
                     .OfType<XAttribute>())
        {
            string conditionValue = definingProjectPath is null
                ? condition.Value
                : ExpandMsBuildThisFileProperties(condition.Value, definingProjectPath);
            if (TryEvaluateSimpleMsBuildCondition(conditionValue, evaluatedProperties, out bool active) && !active)
                return true;
        }

        foreach (XElement branch in element.AncestorsAndSelf()
                     .Where(candidate =>
                         candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string whenCondition in branch.ElementsBeforeSelf()
                         .Where(candidate => candidate.Name.LocalName.Equals(
                             "When",
                             StringComparison.OrdinalIgnoreCase))
                         .Select(candidate => candidate.Attribute("Condition")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value))!)
            {
                string conditionValue = definingProjectPath is null
                    ? whenCondition
                    : ExpandMsBuildThisFileProperties(whenCondition, definingProjectPath);
                if (TryEvaluateSimpleMsBuildCondition(conditionValue, evaluatedProperties, out bool selected) && selected)
                    return true;
            }
        }

        return false;
    }

    private static bool IsDefinitelyActiveMsBuildElement(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        string? definingProjectPath = null)
    {
        foreach (XAttribute condition in element.AncestorsAndSelf()
                     .Select(candidate => candidate.Attribute("Condition"))
                     .OfType<XAttribute>())
        {
            string conditionValue = definingProjectPath is null
                ? condition.Value
                : ExpandMsBuildThisFileProperties(condition.Value, definingProjectPath);
            if (!TryEvaluateSimpleMsBuildCondition(conditionValue, evaluatedProperties, out bool active) ||
                !active)
            {
                return false;
            }
        }

        foreach (XElement branch in element.AncestorsAndSelf()
                     .Where(candidate =>
                         candidate.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string whenCondition in branch.ElementsBeforeSelf()
                         .Where(candidate => candidate.Name.LocalName.Equals(
                             "When",
                             StringComparison.OrdinalIgnoreCase))
                         .Select(candidate => candidate.Attribute("Condition")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value))!)
            {
                string conditionValue = definingProjectPath is null
                    ? whenCondition
                    : ExpandMsBuildThisFileProperties(whenCondition, definingProjectPath);
                if (!TryEvaluateSimpleMsBuildCondition(conditionValue, evaluatedProperties, out bool selected) ||
                    selected)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryEvaluateSimpleMsBuildCondition(
        string condition,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        out bool result)
    {
        string expanded = Regex.Replace(
            condition,
            @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)",
            match => evaluatedProperties.TryGetValue(match.Groups[1].Value, out string? value)
                ? value
                : match.Value,
            RegexOptions.CultureInvariant);
        return TryEvaluateSimpleMsBuildBooleanExpression(expanded, out result);
    }

    private static bool TryEvaluateSimpleMsBuildBooleanExpression(string expression, out bool result)
    {
        expression = TrimOuterConditionParentheses(expression.Trim());
        if (TrySplitTopLevelCondition(expression, "or", out string[] orParts))
        {
            bool hasUnknown = false;
            foreach (string part in orParts)
            {
                if (!TryEvaluateSimpleMsBuildBooleanExpression(part, out bool partResult))
                {
                    hasUnknown = true;
                    continue;
                }

                if (partResult)
                {
                    result = true;
                    return true;
                }
            }

            result = false;
            return !hasUnknown;
        }

        if (TrySplitTopLevelCondition(expression, "and", out string[] andParts))
        {
            bool hasUnknown = false;
            foreach (string part in andParts)
            {
                if (!TryEvaluateSimpleMsBuildBooleanExpression(part, out bool partResult))
                {
                    hasUnknown = true;
                    continue;
                }

                if (!partResult)
                {
                    result = false;
                    return true;
                }
            }

            result = true;
            return !hasUnknown;
        }

        if (expression.StartsWith("!", StringComparison.Ordinal) &&
            TryEvaluateSimpleMsBuildBooleanExpression(expression.Substring(1), out bool negated))
        {
            result = !negated;
            return true;
        }

        if (bool.TryParse(expression, out result))
            return true;

        Match comparison = Regex.Match(
            expression,
            "^\\s*(['\\\"])(.*?)\\1\\s*(==|!=)\\s*(['\\\"])(.*?)\\4\\s*$",
            RegexOptions.CultureInvariant);
        if (!comparison.Success)
        {
            result = false;
            return false;
        }

        if (ContainsUnresolvedBuildExpression(comparison.Groups[2].Value) ||
            ContainsUnresolvedBuildExpression(comparison.Groups[5].Value))
        {
            result = false;
            return false;
        }

        bool equal = string.Equals(
            comparison.Groups[2].Value,
            comparison.Groups[5].Value,
            StringComparison.OrdinalIgnoreCase);
        result = comparison.Groups[3].Value == "==" ? equal : !equal;
        return true;
    }

    private static bool TrySplitTopLevelCondition(
        string expression,
        string conditionOperator,
        out string[] parts)
    {
        var results = new List<string>();
        int partStart = 0;
        int depth = 0;
        char quote = '\0';
        for (int index = 0; index < expression.Length; index++)
        {
            char character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character == '\'' || character == '"')
            {
                quote = character;
                continue;
            }
            if (character == '(')
            {
                depth++;
                continue;
            }
            if (character == ')')
            {
                if (depth == 0)
                {
                    parts = Array.Empty<string>();
                    return false;
                }
                depth--;
                continue;
            }
            if (depth != 0 ||
                index + conditionOperator.Length > expression.Length ||
                !expression.Substring(index, conditionOperator.Length).Equals(
                    conditionOperator,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsConditionOperatorBoundary(expression, index - 1) ||
                !IsConditionOperatorBoundary(expression, index + conditionOperator.Length))
            {
                continue;
            }

            results.Add(expression.Substring(partStart, index - partStart));
            index += conditionOperator.Length - 1;
            partStart = index + 1;
        }

        if (quote != '\0' || depth != 0 || results.Count == 0)
        {
            parts = Array.Empty<string>();
            return false;
        }

        results.Add(expression.Substring(partStart));
        parts = results.ToArray();
        return true;
    }

    private static bool IsConditionOperatorBoundary(string expression, int index)
        => index < 0 ||
           index >= expression.Length ||
           !(char.IsLetterOrDigit(expression[index]) || expression[index] == '_');

    private static string TrimOuterConditionParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[expression.Length - 1] == ')')
        {
            int depth = 0;
            char quote = '\0';
            bool wrapsEntireExpression = true;
            for (int index = 0; index < expression.Length; index++)
            {
                char character = expression[index];
                if (quote != '\0')
                {
                    if (character == quote)
                        quote = '\0';
                    continue;
                }

                if (character == '\'' || character == '"')
                {
                    quote = character;
                    continue;
                }
                if (character == '(')
                    depth++;
                else if (character == ')' && --depth == 0 && index != expression.Length - 1)
                {
                    wrapsEntireExpression = false;
                    break;
                }
            }

            if (!wrapsEntireExpression || depth != 0 || quote != '\0')
                break;
            expression = expression.Substring(1, expression.Length - 2).Trim();
        }

        return expression;
    }
}
