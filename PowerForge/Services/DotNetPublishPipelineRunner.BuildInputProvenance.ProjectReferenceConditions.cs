using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static IReadOnlyDictionary<string, string> ReadEvaluatedProjectReferenceConditionProperties(
        ProjectEvaluationRequest request,
        IReadOnlyCollection<string> propertyDefinitionPaths)
    {
        string[] propertyNames = ReadProjectReferenceConditionPropertyNames(
            propertyDefinitionPaths.Append(request.ProjectPath));
        if (propertyNames.Length == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:MSBuildProjectFullPath"
        };
        foreach (string propertyName in propertyNames)
            arguments.Add("-getProperty:" + propertyName);
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        if (request.TargetFramework is not null)
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }
        arguments.Add("-p:BuildProjectReferences=false");

        try
        {
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

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string propertyName in propertyNames)
            {
                string? value = ReadItemText(properties, propertyName);
                if (value is not null)
                    result[propertyName] = value;
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string[] ReadProjectReferenceConditionPropertyNames(IEnumerable<string> projectPaths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string projectPath in projectPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                     .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            try
            {
                XDocument document = XDocument.Load(projectPath, LoadOptions.None);
                foreach (XElement projectReference in document.Descendants().Where(element =>
                             element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
                {
                    IEnumerable<XElement> conditionOwners = projectReference
                        .AncestorsAndSelf()
                        .Concat(projectReference.Descendants());
                    foreach (string condition in conditionOwners
                                 .Select(element => element.Attribute("Condition")?.Value)
                                 .Where(value => !string.IsNullOrWhiteSpace(value))!)
                    {
                        AddConditionPropertyNames(condition, names);
                    }

                    foreach (string expression in projectReference.Attributes()
                                 .Select(attribute => attribute.Value)
                                 .Concat(projectReference.Descendants().Select(element => element.Value))
                                 .Where(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        AddConditionPropertyNames(expression, names);
                    }

                    foreach (XElement branch in projectReference.Ancestors()
                                 .Where(element =>
                                     element.Name.LocalName.Equals("When", StringComparison.OrdinalIgnoreCase) ||
                                     element.Name.LocalName.Equals("Otherwise", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (string whenCondition in branch.ElementsBeforeSelf()
                                     .Where(element => element.Name.LocalName.Equals(
                                         "When",
                                         StringComparison.OrdinalIgnoreCase))
                                     .Select(element => element.Attribute("Condition")?.Value)
                                     .Where(value => !string.IsNullOrWhiteSpace(value))!)
                        {
                            AddConditionPropertyNames(whenCondition, names);
                        }
                    }
                }
            }
            catch
            {
                // Unknown conditions stay eligible so provenance remains fail closed.
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
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
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        foreach (XAttribute condition in element.AncestorsAndSelf()
                     .Select(candidate => candidate.Attribute("Condition"))
                     .OfType<XAttribute>())
        {
            if (TryEvaluateSimpleMsBuildCondition(condition.Value, evaluatedProperties, out bool active) && !active)
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
                if (TryEvaluateSimpleMsBuildCondition(whenCondition, evaluatedProperties, out bool selected) && selected)
                    return true;
            }
        }

        return false;
    }

    private static bool IsDefinitelyActiveMsBuildElement(
        XElement element,
        IReadOnlyDictionary<string, string> evaluatedProperties)
    {
        foreach (XAttribute condition in element.AncestorsAndSelf()
                     .Select(candidate => candidate.Attribute("Condition"))
                     .OfType<XAttribute>())
        {
            if (!TryEvaluateSimpleMsBuildCondition(condition.Value, evaluatedProperties, out bool active) ||
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
                if (!TryEvaluateSimpleMsBuildCondition(whenCondition, evaluatedProperties, out bool selected) ||
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
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0)
        {
            result = false;
            return false;
        }

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
