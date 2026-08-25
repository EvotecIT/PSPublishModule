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
        foreach (string condition in document.Descendants()
                     .SelectMany(element => element.Attributes())
                     .Where(attribute => attribute.Name.LocalName.Equals(
                         "Condition",
                         StringComparison.OrdinalIgnoreCase))
                     .Select(attribute => DecodeMsBuildEscapes(attribute.Value)))
        {
            if (!TryExpandControlledTaskInputValues(
                    condition,
                    declaringPath,
                    relatedDocuments,
                    evaluatedGlobalProperties,
                    out string[] expandedConditions))
            {
                return false;
            }
            foreach (string expandedCondition in expandedConditions)
            {
                if (!TryReadExistsConditionOperands(expandedCondition, out string[] operands))
                    return false;
                foreach (string operand in operands)
                {
                    if (!TryExpandControlledTaskInputValues(
                            operand,
                            declaringPath,
                            relatedDocuments,
                            evaluatedGlobalProperties,
                            out string[] expandedValues) ||
                        expandedValues.Length == 0)
                    {
                        return false;
                    }

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
                        {
                            return false;
                        }

                        if (isControlledInput?.Invoke(inputPath) is true)
                            continue;
                        string allowedRoot = IsSameOrBelowBuildInputPath(inputPath, declaringAllowedRoot)
                            ? declaringAllowedRoot
                            : taskInputAllowedRoot;
                        if ((File.Exists(inputPath) || Directory.Exists(inputPath)) &&
                            isControlledInput is not null)
                        {
                            return false;
                        }
                        if (HasReparsePointInExistingAncestors(inputPath, allowedRoot))
                            return false;
                    }
                }
            }
        }
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
