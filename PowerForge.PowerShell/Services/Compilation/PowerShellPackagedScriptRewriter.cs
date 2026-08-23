using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>
/// Preserves file-backed script semantics when source is embedded and invoked through AddScript.
/// </summary>
internal static class PowerShellPackagedScriptRewriter
{
    internal static string Rewrite(string sourcePath)
    {
        var ast = Parser.ParseFile(sourcePath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script could not be parsed while preserving script semantics.");

        var exits = ast.FindAll(static node => node is ExitStatementAst, searchNestedScriptBlocks: true)
            .Cast<ExitStatementAst>()
            .ToArray();
        ValidateExits(ast, exits);

        var invocationPaths = FindInvocationPaths(ast).ToArray();
        var replacements = exits.Select(exit => CreateExitReplacement(exit, invocationPaths))
            .Concat(invocationPaths
                .Where(path => !exits.Any(exit => Contains(exit.Extent, path.Extent)))
                .Select(static path => new SourceReplacement(
                    path.Extent.StartOffset,
                    path.Extent.EndOffset,
                    "[System.Environment]::ProcessPath")))
            .OrderByDescending(static replacement => replacement.StartOffset)
            .ToArray();

        var source = new StringBuilder(File.ReadAllText(sourcePath));
        foreach (var replacement in replacements)
        {
            source.Remove(replacement.StartOffset, replacement.EndOffset - replacement.StartOffset);
            source.Insert(replacement.StartOffset, replacement.Text);
        }

        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        prologueEndOffset += replacements
            .Where(replacement => replacement.StartOffset < prologueEndOffset)
            .Sum(static replacement => replacement.Text.Length - (replacement.EndOffset - replacement.StartOffset));
        var pathSemantics = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') pathSemantics.AppendLine();
        pathSemantics.AppendLine("$script:PSCommandPath = [System.Environment]::ProcessPath");
        pathSemantics.AppendLine("$script:PSScriptRoot = [System.IO.Path]::GetDirectoryName($script:PSCommandPath)");
        source.Insert(prologueEndOffset, pathSemantics.ToString());
        return source.ToString();
    }

    private static void ValidateExits(ScriptBlockAst ast, ExitStatementAst[] exits)
    {
        if (exits.Length > 0 && ast.FindAll(static node => node is TrapStatementAst, searchNestedScriptBlocks: true).Any())
            throw new InvalidOperationException("Packaged scripts that combine exit with trap are not supported because exception instrumentation would change trap semantics.");
        if (exits.Any(exit => exits.Any(other => !ReferenceEquals(exit, other) && Contains(exit.Extent, other.Extent))))
            throw new InvalidOperationException("Packaged executable generation does not support nested exit statements.");
        foreach (var exit in exits)
        {
            for (var parent = exit.Parent; parent is not null && !ReferenceEquals(parent, ast); parent = parent.Parent)
            {
                if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                    parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, ast) ||
                    parent is TryStatementAst tryStatement && tryStatement.CatchClauses.Count > 0)
                    throw new InvalidOperationException($"exit at line {exit.Extent.StartLineNumber} cannot be packaged safely because exception instrumentation would change nested or catch behavior.");
            }
        }
    }

    private static SourceReplacement CreateExitReplacement(ExitStatementAst exit, MemberExpressionAst[] invocationPaths)
    {
        var expression = exit.Pipeline?.Extent.Text;
        var exitCode = "0";
        if (!string.IsNullOrWhiteSpace(expression))
        {
            var rewritten = new StringBuilder(expression);
            foreach (var path in invocationPaths
                         .Where(path => Contains(exit.Pipeline!.Extent, path.Extent))
                         .OrderByDescending(static path => path.Extent.StartOffset))
            {
                var offset = path.Extent.StartOffset - exit.Pipeline!.Extent.StartOffset;
                rewritten.Remove(offset, path.Extent.EndOffset - path.Extent.StartOffset);
                rewritten.Insert(offset, "[System.Environment]::ProcessPath");
            }
            exitCode = "[int](" + rewritten + ")";
        }
        return new SourceReplacement(
            exit.Extent.StartOffset,
            exit.Extent.EndOffset,
            "throw [PowerForge.Compiled.PowerForgeScriptExitException]::new(" + exitCode + ")");
    }

    private static bool Contains(IScriptExtent outer, IScriptExtent inner)
        => inner.StartOffset >= outer.StartOffset && inner.EndOffset <= outer.EndOffset;

    private static IEnumerable<MemberExpressionAst> FindInvocationPaths(ScriptBlockAst ast)
    {
        foreach (var member in ast.FindAll(static node => node is MemberExpressionAst, searchNestedScriptBlocks: true).Cast<MemberExpressionAst>())
        {
            if (!IsTopLevelInvocationPath(member, ast)) continue;
            yield return member;
        }
    }

    private static bool IsTopLevelInvocationPath(MemberExpressionAst member, ScriptBlockAst root)
    {
        if (member.Member is not StringConstantExpressionAst path || !path.Value.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
            member.Expression is not MemberExpressionAst command ||
            command.Member is not StringConstantExpressionAst myCommand || !myCommand.Value.Equals("MyCommand", StringComparison.OrdinalIgnoreCase) ||
            command.Expression is not VariableExpressionAst invocation || !invocation.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var parent = member.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, root))
                return false;
        }
        return true;
    }

    private readonly struct SourceReplacement
    {
        internal SourceReplacement(int startOffset, int endOffset, string text)
        {
            StartOffset = startOffset;
            EndOffset = endOffset;
            Text = text;
        }

        internal int StartOffset { get; }
        internal int EndOffset { get; }
        internal string Text { get; }
    }
}
