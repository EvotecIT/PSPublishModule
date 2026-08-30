using System.Text;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool HasOnlyControlledConditionFileInputs(
        XDocument document,
        string declaringPath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        IReadOnlyCollection<(XDocument Document, string DeclaringPath)> relatedDocuments,
        IReadOnlyDictionary<string, string>? evaluatedGlobalProperties,
        Func<string, bool>? isControlledInput)
    {
        foreach (XAttribute conditionAttribute in document.Descendants()
                     .SelectMany(element => element.Attributes())
                     .Where(attribute => attribute.Name.LocalName.Equals(
                         "Condition",
                         StringComparison.OrdinalIgnoreCase)))
        {
            if (evaluatedGlobalProperties is not null &&
                conditionAttribute.Parent is not null &&
                IsDefinitelyInactiveMsBuildElement(
                    conditionAttribute.Parent,
                    evaluatedGlobalProperties,
                    declaringPath))
            {
                continue;
            }
            string condition = DecodeMsBuildEscapes(conditionAttribute.Value);
            if (!TryReadExistsConditionOperands(condition, out string[] declaredOperands))
                return false;
            if (!TryExpandControlledTaskInputValues(
                    condition,
                    declaringPath,
                    taskInputBaseDirectory,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedConditions,
                    consumingElement: conditionAttribute.Parent))
            {
                // Conditions without an Exists call do not consume file-system state.
                // Preserve ordinary unevaluated feature conditions, but fail closed when
                // a declared Exists operand could not be expanded safely.
                if (declaredOperands.Length == 0)
                    continue;
                return false;
            }
            foreach (string expandedCondition in expandedConditions)
            {
                if (!TryReadReachableExistsConditionOperands(expandedCondition, out string[] operands))
                    return false;
                foreach (string operand in operands)
                {
                    if (!TryExpandControlledTaskInputValues(
                            operand,
                            declaringPath,
                            taskInputBaseDirectory,
                            relatedDocuments,
                            evaluatedGlobalProperties,
                            out string[] expandedValues,
                            consumingElement: conditionAttribute.Parent) ||
                        expandedValues.Length == 0)
                        return false;

                    foreach (string expandedValue in expandedValues)
                    {
                        string candidate = expandedValue.Trim().Trim('\'', '"');
                        if (candidate.Length == 0 ||
                            candidate.IndexOf('*') >= 0 ||
                            candidate.IndexOf('?') >= 0 ||
                            ContainsUnresolvedBuildExpression(candidate) ||
                            !TryResolveControlledTaskInputPath(
                                candidate,
                                declaringPath,
                                taskInputBaseDirectory,
                                declaringAllowedRoot,
                                taskInputAllowedRoot,
                                out string inputPath))
                            return false;

                        if (isControlledInput?.Invoke(inputPath) is true)
                            continue;
                        string allowedRoot = IsSameOrBelowBuildInputPath(inputPath, declaringAllowedRoot)
                            ? declaringAllowedRoot
                            : taskInputAllowedRoot;
                        if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                            isControlledInput is not null)
                            return false;
                        if (HasReparsePointInExistingAncestors(inputPath, allowedRoot))
                            return false;
                    }
                }
            }
        }
        return true;
    }

    private static bool TryReadReachableExistsConditionOperands(
        string expression,
        out string[] operands)
    {
        var values = new List<string>();
        bool success = TryReadReachableExistsConditionOperands(expression, values, 0);
        operands = success ? values.ToArray() : Array.Empty<string>();
        return success;
    }

    private static bool TryReadReachableExistsConditionOperands(
        string expression,
        ICollection<string> operands,
        int depth)
    {
        if (depth >= 64)
            return false;

        expression = TrimOuterConditionParentheses(expression.Trim());
        if (TrySplitTopLevelCondition(expression, "or", out string[] orParts))
        {
            foreach (string part in orParts)
            {
                if (!TryReadReachableExistsConditionOperands(part, operands, depth + 1))
                    return false;
                if (TryEvaluateSimpleMsBuildBooleanExpression(part, out bool value) && value)
                    break;
            }

            return true;
        }

        if (TrySplitTopLevelCondition(expression, "and", out string[] andParts))
        {
            foreach (string part in andParts)
            {
                if (!TryReadReachableExistsConditionOperands(part, operands, depth + 1))
                    return false;
                if (TryEvaluateSimpleMsBuildBooleanExpression(part, out bool value) && !value)
                    break;
            }

            return true;
        }

        if (expression.StartsWith("!", StringComparison.Ordinal))
        {
            return TryReadReachableExistsConditionOperands(
                expression.Substring(1),
                operands,
                depth + 1);
        }

        if (!TryReadExistsConditionOperands(expression, out string[] values))
            return false;
        foreach (string value in values)
            operands.Add(value);
        return true;
    }

    private static bool HasReparsePointInExistingAncestors(string path, string root)
    {
        string current = Path.GetFullPath(path);
        string boundary = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (!string.Equals(
                   current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   boundary,
                   comparison))
        {
            try
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, comparison))
                return true;
            current = parent;
        }
        return false;
    }

    private static bool TryReadExistsConditionOperands(string expression, out string[] operands)
    {
        var values = new List<string>();
        for (int index = 0; index < expression.Length; index++)
        {
            if (!IsExistsFunctionAt(expression, index, out int openingParenthesis))
                continue;

            var value = new StringBuilder();
            int depth = 1;
            char quote = '\0';
            int cursor = openingParenthesis + 1;
            for (; cursor < expression.Length; cursor++)
            {
                char character = expression[cursor];
                if ((character == '\'' || character == '"') &&
                    (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                    value.Append(character);
                    continue;
                }
                if (quote == '\0')
                {
                    if (character == '(')
                        depth++;
                    else if (character == ')' && --depth == 0)
                        break;
                }
                value.Append(character);
            }
            if (cursor >= expression.Length || quote != '\0' || depth != 0)
            {
                operands = Array.Empty<string>();
                return false;
            }
            string operand = value.ToString().Trim().Trim('\'', '"');
            if (operand.Length == 0)
            {
                operands = Array.Empty<string>();
                return false;
            }
            values.Add(operand);
            index = cursor;
        }

        operands = values.ToArray();
        return true;
    }

    private static bool IsExistsFunctionAt(
        string expression,
        int index,
        out int openingParenthesis)
    {
        openingParenthesis = -1;
        const string name = "Exists";
        if (index + name.Length > expression.Length ||
            !expression.Substring(index, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (index > 0 && (char.IsLetterOrDigit(expression[index - 1]) || expression[index - 1] is '_' or '.')))
        {
            return false;
        }
        int cursor = index + name.Length;
        if (cursor < expression.Length &&
            (char.IsLetterOrDigit(expression[cursor]) || expression[cursor] is '_' or '.'))
        {
            return false;
        }
        while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor]))
            cursor++;
        if (cursor >= expression.Length || expression[cursor] != '(')
            return false;
        openingParenthesis = cursor;
        return true;
    }
}
