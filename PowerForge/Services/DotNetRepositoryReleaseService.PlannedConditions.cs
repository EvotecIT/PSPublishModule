using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static bool ConditionMatches(
        string? condition,
        IReadOnlyDictionary<string, string> properties,
        string conditionDirectory)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var expanded = Regex.Replace(condition!, @"\$\((?<name>[A-Za-z_][A-Za-z0-9_.-]*)\)", match =>
            properties.TryGetValue(match.Groups["name"].Value, out var replacement) ? replacement : string.Empty);
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because condition '{condition}' contains an unresolved MSBuild expression.");
        }

        ValidateConditionExpression(expanded);
        return EvaluateConditionExpression(expanded, conditionDirectory);
    }

    private static void ValidateConditionExpression(string condition)
    {
        var trimmed = TrimEnclosingConditionParentheses(condition.Trim());
        var branches = SplitTopLevelCondition(trimmed, "Or");
        if (branches.Count == 1)
            branches = SplitTopLevelCondition(trimmed, "And");
        if (branches.Count > 1)
        {
            foreach (var branch in branches)
                ValidateConditionExpression(branch);
            return;
        }

        if (MatchExistsCondition(trimmed).Success || bool.TryParse(trimmed, out _) || MatchComparisonCondition(trimmed).Success)
            return;
        throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because condition '{condition}' requires unsupported MSBuild evaluation.");
    }

    private static bool EvaluateConditionExpression(string condition, string conditionDirectory)
    {
        var trimmed = TrimEnclosingConditionParentheses(condition.Trim());
        var orBranches = SplitTopLevelCondition(trimmed, "Or");
        if (orBranches.Count > 1)
        {
            foreach (var branch in orBranches)
            {
                if (EvaluateConditionExpression(branch, conditionDirectory))
                    return true;
            }
            return false;
        }

        var andBranches = SplitTopLevelCondition(trimmed, "And");
        if (andBranches.Count > 1)
        {
            foreach (var branch in andBranches)
            {
                if (!EvaluateConditionExpression(branch, conditionDirectory))
                    return false;
            }
            return true;
        }

        return EvaluateSimpleCondition(trimmed, conditionDirectory);
    }

    private static IReadOnlyList<string> SplitTopLevelCondition(string condition, string operation)
    {
        var branches = new List<string>();
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var index = 0; index < condition.Length; index++)
        {
            var current = condition[index];
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current == '\'' || current == '"')
            {
                quote = current;
                continue;
            }
            if (current == '(')
            {
                depth++;
                continue;
            }
            if (current == ')')
            {
                depth--;
                if (depth < 0)
                    ThrowMalformedCondition(condition);
                continue;
            }
            if (depth != 0 || !IsConditionOperatorAt(condition, index, operation))
                continue;

            var branch = condition.Substring(start, index - start).Trim();
            if (branch.Length == 0)
                ThrowMalformedCondition(condition);
            branches.Add(branch);
            index += operation.Length - 1;
            start = index + 1;
        }

        if (quote != '\0' || depth != 0)
            ThrowMalformedCondition(condition);
        var finalBranch = condition.Substring(start).Trim();
        if (finalBranch.Length == 0)
            ThrowMalformedCondition(condition);
        branches.Add(finalBranch);
        return branches;
    }

    private static bool IsConditionOperatorAt(string condition, int index, string operation)
    {
        if (index + operation.Length > condition.Length ||
            !string.Equals(condition.Substring(index, operation.Length), operation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var beforeIsBoundary = index == 0 || !IsConditionIdentifierCharacter(condition[index - 1]);
        var after = index + operation.Length;
        var afterIsBoundary = after == condition.Length || !IsConditionIdentifierCharacter(condition[after]);
        return beforeIsBoundary && afterIsBoundary;
    }

    private static bool IsConditionIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    private static string TrimEnclosingConditionParentheses(string condition)
    {
        while (condition.Length >= 2 && condition[0] == '(' && FindMatchingParenthesis(condition, 0) == condition.Length - 1)
            condition = condition.Substring(1, condition.Length - 2).Trim();
        return condition;
    }

    private static int FindMatchingParenthesis(string condition, int openingIndex)
    {
        var depth = 0;
        char quote = '\0';
        for (var index = openingIndex; index < condition.Length; index++)
        {
            var current = condition[index];
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current == '\'' || current == '"')
            {
                quote = current;
                continue;
            }
            if (current == '(')
                depth++;
            else if (current == ')' && --depth == 0)
                return index;
        }
        ThrowMalformedCondition(condition);
        return -1;
    }

    private static bool EvaluateSimpleCondition(string condition, string conditionDirectory)
    {
        var exists = MatchExistsCondition(condition);
        if (exists.Success)
        {
            var path = ResolvePlannedPath(conditionDirectory, exists.Groups["path"].Value);
            var result = File.Exists(path) || Directory.Exists(path);
            return exists.Groups["not"].Success ? !result : result;
        }
        if (bool.TryParse(condition, out var boolean))
            return boolean;

        var match = MatchComparisonCondition(condition);
        if (!match.Success)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because condition '{condition}' requires unsupported MSBuild evaluation.");
        var left = UnquoteConditionOperand(match.Groups["left"].Value.Trim());
        var right = UnquoteConditionOperand(match.Groups["right"].Value.Trim());
        var equal = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        var comparisonOperator = match.Groups["operator"].Value;
        if (comparisonOperator == "==")
            return equal;
        if (comparisonOperator == "!=")
            return !equal;

        var comparison = CompareConditionOperands(left, right, condition);
        return comparisonOperator == ">" ? comparison > 0 :
            comparisonOperator == ">=" ? comparison >= 0 :
            comparisonOperator == "<" ? comparison < 0 : comparison <= 0;
    }

    private static Match MatchExistsCondition(string condition)
        => Regex.Match(condition, "^(?<not>!)?\\s*Exists\\(\\s*(?<quote>['\\\"])(?<path>.*?)\\k<quote>\\s*\\)$", RegexOptions.IgnoreCase);

    private static Match MatchComparisonCondition(string condition)
        => Regex.Match(
            condition,
            "^\\s*(?<left>'[^']*'|\\\"[^\\\"]*\\\"|[^=!<>]+?)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<right>'[^']*'|\\\"[^\\\"]*\\\"|[^=!<>]+?)\\s*$");

    private static string UnquoteConditionOperand(string operand)
        => operand.Length >= 2 && ((operand[0] == '\'' && operand[operand.Length - 1] == '\'') || (operand[0] == '"' && operand[operand.Length - 1] == '"'))
            ? operand.Substring(1, operand.Length - 2)
            : operand;

    private static int CompareConditionOperands(string left, string right, string originalCondition)
    {
        var normalizedLeft = left.TrimStart('v', 'V');
        var normalizedRight = right.TrimStart('v', 'V');
        if (Version.TryParse(normalizedLeft, out var leftVersion) && Version.TryParse(normalizedRight, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);
        if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber) &&
            decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }
        throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because relational condition '{originalCondition}' does not compare numeric or version values.");
    }

    private static void ThrowMalformedCondition(string condition)
        => throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because condition '{condition}' is malformed.");
}
