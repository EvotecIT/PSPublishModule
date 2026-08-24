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

        var fileResolvedUsing = ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
            .Cast<UsingStatementAst>()
            .FirstOrDefault(static statement => statement.UsingStatementKind != UsingStatementKind.Namespace);
        if (fileResolvedUsing is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support '{fileResolvedUsing.Extent.Text}' because using module/assembly directives are resolved before the embedded script receives file-backed path metadata.");
        }

        var dotSource = ast.FindAll(
                static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .FirstOrDefault();
        if (dotSource is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support dot-sourced command '{dotSource.Extent.Text}' because the dependency is not embedded with file-backed path semantics.");
        }

        ValidateHostInteraction(ast);

        var explicitNamedBlock = FindExplicitNamedBlock(ast);
        if (explicitNamedBlock is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support the explicit '{explicitNamedBlock.BlockKind.ToString().ToLowerInvariant()}' named block because embedded AddScript execution cannot preserve script pipeline lifecycle semantics.");
        }

        var exits = ast.FindAll(static node => node is ExitStatementAst, searchNestedScriptBlocks: true)
            .Cast<ExitStatementAst>()
            .ToArray();
        ValidateExits(ast, exits);

        var invocationPaths = FindInvocationPaths(ast).ToArray();
        var parameterBindingPaths = FindParameterBindingPaths(ast).ToArray();
        var replacements = exits.Select(exit => CreateExitReplacement(exit, invocationPaths))
            .Concat(invocationPaths
                .Where(path => !exits.Any(exit => Contains(exit.Extent, path.Extent)))
                .Select(static path => new SourceReplacement(
                    path.Extent.StartOffset,
                    path.Extent.EndOffset,
                    "[System.Environment]::ProcessPath")))
            .Concat(parameterBindingPaths.Select(static path => new SourceReplacement(
                path.Extent.StartOffset,
                path.Extent.EndOffset,
                path.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase)
                    ? "$([System.IO.Path]::GetDirectoryName([System.Environment]::ProcessPath))"
                    : "$([System.Environment]::ProcessPath)")))
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

    private static NamedBlockAst? FindExplicitNamedBlock(ScriptBlockAst ast)
    {
        if (ast.DynamicParamBlock is not null) return ast.DynamicParamBlock;
        if (ast.BeginBlock is not null) return ast.BeginBlock;
        if (ast.ProcessBlock is not null) return ast.ProcessBlock;
        if (ast.GetType().GetProperty("CleanBlock")?.GetValue(ast) is NamedBlockAst cleanBlock) return cleanBlock;
        return ast.EndBlock is { Unnamed: false } ? ast.EndBlock : null;
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

    private static IEnumerable<VariableExpressionAst> FindParameterBindingPaths(ScriptBlockAst ast)
    {
        if (ast.ParamBlock is null) yield break;
        foreach (var parameter in ast.ParamBlock.Parameters)
        {
            foreach (var variable in parameter
                         .FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
                         .Cast<VariableExpressionAst>())
            {
                if (variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase) ||
                    variable.VariablePath.UserPath.Equals("PSCommandPath", StringComparison.OrdinalIgnoreCase))
                    yield return variable;
            }
        }
    }

    private static bool IsTopLevelInvocationPath(MemberExpressionAst member, ScriptBlockAst root)
    {
        if (member.Member is not StringConstantExpressionAst path ||
            (!path.Value.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
             !path.Value.Equals("Definition", StringComparison.OrdinalIgnoreCase)) ||
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

    private static void ValidateHostInteraction(ScriptBlockAst ast)
    {
        var hostReference = ast.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("Host", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault();
        if (hostReference is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support interactive PSHost access '{hostReference.Extent.Text}'. Use a typed console entry point or keep this script on pwsh -File.");
        }

        var interactiveCommand = ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .FirstOrDefault(static command => IsInteractiveCommand(command.GetCommandName()));
        if (interactiveCommand is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support interactive command '{interactiveCommand.GetCommandName()}' because the embedded runspace has no console-backed PSHost.");
        }
    }

    private static bool IsInteractiveCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        var unqualifiedName = commandName!.Split('\\').Last();
        return unqualifiedName.Equals("Read-Host", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Get-Credential", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Show-Command", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Out-GridView", StringComparison.OrdinalIgnoreCase);
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
