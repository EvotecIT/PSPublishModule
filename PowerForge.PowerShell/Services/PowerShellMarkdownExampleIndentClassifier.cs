using System;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed class PowerShellMarkdownExampleIndentClassifier : IMarkdownExampleIndentClassifier
{
    public static PowerShellMarkdownExampleIndentClassifier Instance { get; } = new();

    private PowerShellMarkdownExampleIndentClassifier()
    {
    }

    public bool ShouldRemoveSharedIndentAfterFirstLine(string candidateCode)
    {
        if (string.IsNullOrWhiteSpace(candidateCode))
            return false;

        try
        {
            var ast = Parser.ParseInput(candidateCode, out _, out var errors);
            if (errors.Length > 0)
                return false;

            var statements = ast.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
            if (statements.Length <= 1)
                return false;

            // Mixed command/output examples are ambiguous when the output is valid PowerShell syntax.
            // Keep this parser-backed and avoid command-name or keyword allow/deny lists here.
            var firstLine = GetFirstNonBlankLine(candidateCode);
            if (firstLine.Length == 0)
                return false;

            if (ParsesWithoutErrors(firstLine))
                return statements[0] is AssignmentStatementAst;

            if (statements[0] is AssignmentStatementAst assignment)
                return ContainsStructuredMultilineRightHandSide(assignment);

            return statements[0] is not AssignmentStatementAst
                && statements[0].Extent.StartLineNumber == 1
                && statements[0].Extent.EndLineNumber > 1;
        }
        catch (Exception)
        {
            return false;
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

    private static string GetFirstNonBlankLine(string code)
    {
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return string.Empty;
    }
}
