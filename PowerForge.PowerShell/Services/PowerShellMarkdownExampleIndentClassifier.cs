using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed class PowerShellMarkdownExampleIndentClassifier : IMarkdownExampleIndentClassifier
{
    public static PowerShellMarkdownExampleIndentClassifier Instance { get; } = new();

    private PowerShellMarkdownExampleIndentClassifier()
    {
    }

    public string NormalizeAfterSharedIndent(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        try
        {
            var normalized = code.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var ast = Parser.ParseInput(normalized, out _, out var errors);
            var errorLines = GetErrorLines(errors);

            var statements = ast.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
            if (statements.Length <= 1)
                return normalized;

            var firstStatementIndex = FindFirstUsableStatementIndex(statements, errorLines);
            if (firstStatementIndex < 0)
                return normalized;

            // Mixed command/output examples are ambiguous when the output is valid PowerShell syntax.
            // Keep this parser-backed and avoid command-name or keyword allow/deny lists here.
            var firstStatement = statements[firstStatementIndex];
            var firstLine = lines[firstStatement.Extent.StartLineNumber - 1];
            if (firstLine.Length == 0)
                return normalized;

            var linesToNormalize = FindLinesToNormalize(statements, firstStatementIndex, errorLines, firstLine);
            if (linesToNormalize.Count == 0)
                return normalized;

            var indent = GetCommonIndent(lines, linesToNormalize);
            if (indent < 8)
                return normalized;

            foreach (var lineIndex in linesToNormalize)
            {
                lines[lineIndex] = RemoveLeadingWhitespace(lines[lineIndex], indent);
            }

            return string.Join("\n", lines);
        }
        catch (Exception)
        {
            return code;
        }
    }

    private static HashSet<int> GetErrorLines(ParseError[] errors)
    {
        var lines = new HashSet<int>();
        foreach (var error in errors)
        {
            var start = Math.Max(1, error.Extent.StartLineNumber);
            var end = Math.Max(start, error.Extent.EndLineNumber);
            for (var line = start; line <= end; line++)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static int FindFirstUsableStatementIndex(StatementAst[] statements, HashSet<int> errorLines)
    {
        for (var i = 0; i < statements.Length; i++)
        {
            if (!errorLines.Contains(statements[i].Extent.StartLineNumber))
                return i;
        }

        return -1;
    }

    private static HashSet<int> FindLinesToNormalize(
        StatementAst[] statements,
        int firstStatementIndex,
        HashSet<int> errorLines,
        string firstLine)
    {
        var firstStatement = statements[firstStatementIndex];
        var lines = new HashSet<int>();
        if (ParsesWithoutErrors(firstLine))
        {
            if (firstStatement is not AssignmentStatementAst)
                return lines;

            AddStatementLines(statements, firstStatementIndex + 1, errorLines, lines);
            return lines;
        }

        if (firstStatement is AssignmentStatementAst assignment)
        {
            if (!ContainsStructuredMultilineRightHandSide(assignment))
                return lines;

            AddStatementLines(firstStatement, errorLines, lines, skipFirstLine: true);
            AddStatementLines(statements, firstStatementIndex + 1, errorLines, lines);
            return lines;
        }

        if (firstStatement.Extent.EndLineNumber <= firstStatement.Extent.StartLineNumber)
            return lines;

        AddStatementLines(firstStatement, errorLines, lines, skipFirstLine: true);
        AddStatementLines(statements, firstStatementIndex + 1, errorLines, lines);
        return lines;
    }

    private static void AddStatementLines(
        StatementAst[] statements,
        int startIndex,
        HashSet<int> errorLines,
        HashSet<int> lines)
    {
        for (var i = startIndex; i < statements.Length; i++)
        {
            AddStatementLines(statements[i], errorLines, lines, skipFirstLine: false);
        }
    }

    private static void AddStatementLines(
        StatementAst statement,
        HashSet<int> errorLines,
        HashSet<int> lines,
        bool skipFirstLine)
    {
        var start = statement.Extent.StartLineNumber + (skipFirstLine ? 1 : 0);
        for (var line = start; line <= statement.Extent.EndLineNumber; line++)
        {
            if (!errorLines.Contains(line))
                lines.Add(line - 1);
        }
    }

    private static bool ParsesWithoutErrors(string code)
    {
        Parser.ParseInput(code, out _, out var errors);
        return errors.Length == 0;
    }

    private static bool ContainsStructuredMultilineRightHandSide(AssignmentStatementAst assignment)
        => assignment.Right.Find(
            static ast => ast is ArrayExpressionAst
                || ast is ArrayLiteralAst
                || ast is HashtableAst
                || ast is ScriptBlockExpressionAst,
            searchNestedScriptBlocks: true) is not null;

    private static int GetCommonIndent(string[] lines, HashSet<int> lineIndexes)
    {
        var common = int.MaxValue;
        foreach (var lineIndex in lineIndexes)
        {
            if (lineIndex < 0 || lineIndex >= lines.Length || string.IsNullOrWhiteSpace(lines[lineIndex]))
                continue;

            common = Math.Min(common, CountLeadingWhitespace(lines[lineIndex]));
        }

        return common == int.MaxValue ? 0 : common;
    }

    private static int CountLeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
        {
            count++;
        }

        return count;
    }

    private static string RemoveLeadingWhitespace(string line, int count)
    {
        var remove = 0;
        while (remove < line.Length && remove < count && (line[remove] == ' ' || line[remove] == '\t'))
        {
            remove++;
        }

        return remove == 0 ? line : line.Substring(remove);
    }
}
